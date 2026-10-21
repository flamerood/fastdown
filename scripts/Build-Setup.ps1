param([Parameter(Mandatory=$true)][string]$PackageDirectory, [string]$Compiler)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$Compiler) { $Compiler = & (Join-Path $PSScriptRoot 'Install-SetupCompiler.ps1') | Select-Object -Last 1 }
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
$props = [xml](Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw)
$version = [string]$props.Project.PropertyGroup.Version
& $Compiler '/Qp' ('/DPackageRoot='+$package) ('/DAppVersion='+$version) (Join-Path $root 'installer\Fastdown.iss')
if ($LASTEXITCODE -ne 0) { throw 'Fastdown kurulum paketi derlenemedi.' }
$setup = Join-Path $root 'artifacts\Fastdown-Setup.exe'
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  Fastdown-Setup.exe" | Set-Content -LiteralPath ($setup + '.sha256') -Encoding ASCII
Write-Host "Tek dosya kurulum hazir: $setup"
