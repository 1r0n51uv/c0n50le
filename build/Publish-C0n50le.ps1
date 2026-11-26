# Builds the two downloads into dist/: C0n50le-Setup-x64.exe (Inno Setup, per user) and C0n50le-Portable-x64.zip (the
# app folder, to unpack anywhere: its data stays in c0n50le_Data next to the exe), then prints their SHA-256.
# The portable build is a zip, not a single exe: WinUI's activation needs its native libraries next to the exe.
param([string]$Version = '0.1.0')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
$project = Join-Path $root 'src/C0n50le/C0n50le.csproj'
$appDir = Join-Path $dist 'app'
New-Item -ItemType Directory -Force $dist | Out-Null
if (Test-Path $appDir) { Remove-Item $appDir -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --self-contained true "-p:Version=$Version" -p:DebugType=none -p:Platform=x64 -o $appDir
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
if (-not (Test-Path (Join-Path $appDir 'C0n50le.exe'))) { throw 'Publish did not produce C0n50le.exe' }

$zip = Join-Path $dist 'C0n50le-Portable-x64.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $appDir '*') -DestinationPath $zip -CompressionLevel Optimal

$compiler = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6/ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe')
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $compiler) { throw 'Inno Setup 6 is required' }
& $compiler "/DAppVersion=$Version" "/DSourceDir=$appDir" "/DOutputDir=$dist" (Join-Path $PSScriptRoot 'C0n50le.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed' }
Get-FileHash (Join-Path $dist 'C0n50le-Setup-x64.exe'), $zip -Algorithm SHA256
