param([string]$ApplicationDirectory)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$projectRoot = Split-Path $PSScriptRoot -Parent
$destination = if ($ApplicationDirectory) { Join-Path ([IO.Path]::GetFullPath($ApplicationDirectory)) 'tools' } else { Join-Path $projectRoot 'Fastdown\tools' }
$cache = Join-Path $projectRoot '.test-data\video-tools'
New-Item -ItemType Directory -Force -Path $destination,$cache | Out-Null
$headers = @{'User-Agent'='Fastdown-video-tools'}
$apiHeaders = @{'User-Agent'='Fastdown-video-tools'; Accept='application/vnd.github+json'}
# CI runners share outbound IPs. Use the job's read-only token only for GitHub API metadata.
if ($env:GH_TOKEN) { $apiHeaders.Authorization = 'Bearer ' + $env:GH_TOKEN }
$records = @()
foreach ($spec in @(
    @{Repo='yt-dlp/yt-dlp';Asset='yt-dlp.exe'},
    @{Repo='yt-dlp/FFmpeg-Builds';Asset='ffmpeg-master-latest-win64-gpl.zip'},
    @{Repo='denoland/deno';Asset='deno-x86_64-pc-windows-msvc.zip'}
)) {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$($spec.Repo)/releases/latest" -Headers $apiHeaders
    $asset = $release.assets | Where-Object name -EQ $spec.Asset | Select-Object -First 1
    if (!$asset -or $asset.digest -notmatch '^sha256:([a-f0-9]{64})$') { throw 'Resmi SHA-256 bilgisi bulunamadi.' }
    $expected = $Matches[1]
    $download = Join-Path $cache $asset.name
    if (!(Test-Path -LiteralPath $download) -or (Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $expected) {
        Write-Host "$($spec.Repo) $($release.tag_name) indiriliyor..."
        Invoke-WebRequest -UseBasicParsing -Uri $asset.browser_download_url -Headers $headers -OutFile $download
    }
    if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $expected) { throw 'Indirilen aracin SHA-256 dogrulamasi basarisiz.' }
    if ($asset.name -like '*.exe') { Copy-Item -LiteralPath $download -Destination $destination -Force }
    else {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [System.IO.Compression.ZipFile]::OpenRead($download)
        try {
            foreach ($entry in $archive.Entries) {
                if ($entry.Name -in @('ffmpeg.exe','ffprobe.exe','deno.exe')) {
                    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $destination $entry.Name), $true)
                }
                elseif ($entry.Name -match '^(LICENSE|COPYING|README)' -and $entry.Length -gt 0) {
                    $licenseName = ($spec.Repo -replace '/','-') + '-' + $entry.Name
                    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $destination $licenseName), $true)
                }
            }
        } finally { $archive.Dispose() }
    }
    $records += @{repository=$spec.Repo;version=$release.tag_name;url=$asset.browser_download_url;sha256=$expected;source=$release.html_url}
}
foreach ($spec in @(
    @{Url='https://raw.githubusercontent.com/yt-dlp/yt-dlp/master/LICENSE';Name='yt-dlp-LICENSE'},
    @{Url='https://raw.githubusercontent.com/yt-dlp/yt-dlp/master/THIRD_PARTY_LICENSES.txt';Name='yt-dlp-THIRD_PARTY_LICENSES.txt'},
    @{Url='https://raw.githubusercontent.com/denoland/deno/main/LICENSE.md';Name='deno-LICENSE.md'},
    @{Url='https://www.gnu.org/licenses/gpl-3.0.txt';Name='GPL-3.0.txt'}
)) { Invoke-WebRequest -UseBasicParsing -Uri $spec.Url -Headers $headers -OutFile (Join-Path $destination $spec.Name) }
$records | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $destination 'versions.json') -Encoding UTF8
Write-Host 'Video araclari dogrulandi ve Fastdown\tools klasorune kuruldu.'
