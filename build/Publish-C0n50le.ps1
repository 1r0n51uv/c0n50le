# Builds the two downloads into dist/: C0n50le-Setup-x64.exe (Inno Setup, per user) and C0n50le-Portable-x64.exe
# (one exe that keeps its data next to itself), then prints their SHA-256.
param([string]$Version = '0.1.0')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
$project = Join-Path $root 'src/C0n50le/C0n50le.csproj'
$setupDir = Join-Path $dist 'installer-publish'
$portableDir = Join-Path $dist 'portable-publish'
New-Item -ItemType Directory -Force $dist | Out-Null
$shared = @($project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', "-p:Version=$Version", '-p:DebugType=none', '-p:Platform=x64')
dotnet publish @shared -o $setupDir
if ($LASTEXITCODE -ne 0) { throw 'Installer publish failed' }
dotnet publish @shared -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -o $portableDir
if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed' }
Copy-Item (Join-Path $portableDir 'C0n50le.exe') (Join-Path $dist 'C0n50le-Portable-x64.exe') -Force

$compiler = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6/ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe')
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $compiler) { throw 'Inno Setup 6 is required' }
& $compiler "/DAppVersion=$Version" "/DSourceDir=$setupDir" "/DOutputDir=$dist" (Join-Path $PSScriptRoot 'C0n50le.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed' }
Get-FileHash (Join-Path $dist 'C0n50le-*-x64.exe') -Algorithm SHA256
