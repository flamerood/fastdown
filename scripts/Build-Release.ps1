param(
    [ValidateSet('win-x64')][string]$Runtime = 'win-x64',
    [switch]$SkipVideoTools
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outputRoot = Join-Path $projectRoot "artifacts\release-$stamp"
$package = Join-Path $outputRoot 'Fastdown-Windows-x64'
$app = Join-Path $package 'Fastdown'
New-Item -ItemType Directory -Force -Path $app | Out-Null
foreach ($project in @('src/Fastdown.App/Fastdown.App.csproj','src/Fastdown.BrowserHost/Fastdown.BrowserHost.csproj')) {
    & dotnet publish $project -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -o $app
    if ($LASTEXITCODE -ne 0) { throw "Publish basarisiz: $project" }
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'extension') -Destination $package -Recurse
New-Item -ItemType Directory -Path (Join-Path $package 'scripts') | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-VideoTools.ps1') -Destination (Join-Path $package 'scripts')
foreach ($file in @('Baslat.cmd','Eklenti-Kur.cmd','Eklenti-Kaldir.cmd','Video-Araclarini-Guncelle.cmd','README.md','VIDEO-TOOLS.md','KURULUM.md','LICENSE')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination $package
}
if (!$SkipVideoTools) {
    $existing = Join-Path $projectRoot 'Fastdown\tools'
    $missing = @('yt-dlp.exe','ffmpeg.exe','ffprobe.exe','deno.exe') | Where-Object { !(Test-Path -LiteralPath (Join-Path $existing $_)) }
    if (!$missing) { Copy-Item -LiteralPath $existing -Destination $app -Recurse }
    else { & (Join-Path $PSScriptRoot 'Install-VideoTools.ps1') -ApplicationDirectory $app }
}
$archive = Join-Path $projectRoot 'artifacts\Fastdown-Windows-x64.zip'
Compress-Archive -LiteralPath $package -DestinationPath $archive -CompressionLevel Fastest -Force
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  Fastdown-Windows-x64.zip" | Set-Content -LiteralPath ($archive + '.sha256') -Encoding ASCII
Write-Host "Hazir: $archive"
