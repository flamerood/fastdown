$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $root 'artifacts\build-tools\InnoSetup\ISCC.exe'
if (Test-Path -LiteralPath $compiler) { Write-Output $compiler; return }
$headers = @{'User-Agent'='Fastdown-builder'; Accept='application/vnd.github+json'}
if ($env:GH_TOKEN) { $headers.Authorization = 'Bearer ' + $env:GH_TOKEN }
$release = Invoke-RestMethod -Uri 'https://api.github.com/repos/jrsoftware/issrc/releases/tags/is-6_7_3' -Headers $headers
$asset = $release.assets | Where-Object name -EQ 'innosetup-6.7.3.exe' | Select-Object -First 1
if (!$asset -or $asset.digest -notmatch '^sha256:([a-f0-9]{64})$') { throw 'Resmi derleyici hash kaydi bulunamadi.' }
$hash = $Matches[1]
$download = Join-Path $root 'artifacts\build-tools\innosetup-6.7.3.exe'
New-Item -ItemType Directory -Force -Path (Split-Path $download -Parent) | Out-Null
Invoke-WebRequest -UseBasicParsing -Uri $asset.browser_download_url -OutFile $download
if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $hash) { throw 'Derleyici hash dogrulamasi basarisiz.' }
$signature = Get-AuthenticodeSignature -LiteralPath $download
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Pyrsys B.V.') { throw 'Resmi derleyici imzasi dogrulanamadi.' }
$destination = Split-Path $compiler -Parent
$process = Start-Process -FilePath $download -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER','/NOICONS','/TASKS=',('/DIR="'+$destination+'"')) -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $compiler)) { throw 'Kurulum derleyicisi hazirlanamadi.' }
Write-Output $compiler
