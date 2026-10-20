param([string]$Ffmpeg = "$env:LOCALAPPDATA\ms-playwright\ffmpeg-1011\ffmpeg-win64.exe")
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Ffmpeg)) { throw 'Pass the installed Playwright ffmpeg executable with -Ffmpeg.' }
Add-Type -AssemblyName System.Drawing
$fixtureFolder = Join-Path $PSScriptRoot 'fixtures'
$imageFolder = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\artifacts\quality-fixtures'))
New-Item -ItemType Directory -Path $fixtureFolder,$imageFolder -Force | Out-Null
foreach ($height in @(1080,720,480)) {
    $width = [int]($height * 16 / 9)
    $bitmap = New-Object System.Drawing.Bitmap $width,$height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::FromArgb(40,80,58))
    $image = Join-Path $imageFolder ($height.ToString() + '.jpg')
    $bitmap.Save($image,[System.Drawing.Imaging.ImageFormat]::Jpeg)
    $graphics.Dispose()
    $bitmap.Dispose()
    $output = Join-Path $fixtureFolder ('movie-' + $height + 'p.webm')
    & $Ffmpeg -hide_banner -loglevel error -y -f image2pipe -c:v mjpeg -i $image -frames:v 1 -c:v libvpx -b:v 100k -deadline realtime -cpu-used 8 $output
    if ($LASTEXITCODE -ne 0) { throw 'Fixture encoding failed.' }
    Write-Output ('Generated ' + $output)
}
