param([string]$SetupPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$SetupPath) { $SetupPath = Join-Path $root 'artifacts\Fastdown-Setup.exe' }
$setup = (Resolve-Path -LiteralPath $SetupPath).Path
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{58F597F7-44F6-4E85-BD36-22C52B33C87D}_is1'
if (Test-Path -LiteralPath $uninstallKey) { throw 'Mevcut Fastdown kurulumu var; izole kurulum testi temiz Windows hesabi gerektirir.' }
$testRoot = [IO.Path]::GetFullPath((Join-Path $root ('.test-data\installer-'+(Get-Date -Format yyyyMMdd-HHmmss))))
if (!$testRoot.StartsWith(([IO.Path]::GetFullPath($root).TrimEnd('\')+'\'),[StringComparison]::OrdinalIgnoreCase)) { throw 'Gecersiz test klasoru.' }
$target = Join-Path $testRoot 'Fastdown kurulum Türkçe'
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$keys = @('Google\Chrome','Microsoft\Edge','Chromium') | ForEach-Object { 'HKCU:\Software\'+$_+'\NativeMessagingHosts\com.akis.download_manager' }
$saved = @{}
foreach ($key in $keys) { if (Test-Path -LiteralPath $key) { $saved[$key] = (Get-Item -LiteralPath $key).GetValue('') } }
$libraries = @{}
foreach ($brand in @('Fastdown','Akis')) { $file=Join-Path $env:LOCALAPPDATA "$brand\library.json"; if (Test-Path -LiteralPath $file) { $libraries[$file]=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash } }
try {
    $arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/MERGETASKS=!desktopicon',('/DIR="'+$target+'"'))
    foreach ($phase in @('install','upgrade')) {
        $process = Start-Process -FilePath $setup -ArgumentList ($arguments + ('/LOG="'+(Join-Path $testRoot "$phase.log")+'"')) -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw "Kurulum basarisiz: $phase ($($process.ExitCode))" }
        foreach ($file in @('Fastdown\Fastdown.exe','Fastdown\coreclr.dll','Fastdown\tools\yt-dlp.exe','Fastdown\tools\ffmpeg.exe','extension\manifest.json','Kurulum-Rehberi.html','unins000.exe')) { if (!(Test-Path -LiteralPath (Join-Path $target $file))) { throw "Eksik kurulum dosyasi: $file" } }
        foreach ($key in $keys) { if ((Get-Item -LiteralPath $key).GetValue('') -ne (Join-Path $target 'Fastdown\com.akis.download_manager.json')) { throw 'Tarayici koprusu kaydedilmedi.' } }
        foreach ($file in $libraries.Keys) { if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $libraries[$file]) { throw 'Kisisel indirme listesi degisti.' } }
        $marker=Join-Path $target 'kisisel-dosya.txt'
        if ($phase -eq 'install') { [IO.File]::WriteAllText($marker,'must survive upgrade and uninstall') }
        elseif (!(Test-Path -LiteralPath $marker)) { throw 'Yukseltme kullanici dosyasini sildi.' }
        Write-Output "PASS Installer ${phase}: files, native bridge and personal history"
    }
    $preview=Join-Path $testRoot 'installed-preview.png'
    $process=Start-Process -FilePath (Join-Path $target 'Fastdown\Fastdown.exe') -ArgumentList @('--preview',('"'+$preview+'"'),'updates') -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $preview)) { throw 'Kurulan uygulama acilmadi.' }
    $process=Start-Process -FilePath (Join-Path $target 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0 -or (Test-Path -LiteralPath (Join-Path $target 'Fastdown\Fastdown.exe')) -or !(Test-Path -LiteralPath $marker)) { throw 'Kaldirma dosya korumasi basarisiz.' }
    foreach ($file in $libraries.Keys) { if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $libraries[$file]) { throw 'Kaldirma kisisel gecmisi degistirdi.' } }
    Write-Output 'PASS Installer launch and uninstall: application works; personal files/history preserved'
} finally {
    foreach ($key in $saved.Keys) {
        $writableKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($key.Substring(6))
        try { $writableKey.SetValue('', $saved[$key], [Microsoft.Win32.RegistryValueKind]::String) } finally { $writableKey.Dispose() }
    }
}
