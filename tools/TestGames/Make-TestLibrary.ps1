# Builds the stand-in game once and copies it as four games into tools/TestGames/library, one folder each, ready to
# be picked as the games folder in c0n50le (System → Library → Games folder).
param([string[]]$Games = @('Pokemon Pokopia', 'Control Resonant', 'FIFA 27', 'GTA 6'))
$ErrorActionPreference = 'Stop'
$publish = Join-Path $PSScriptRoot 'bin/publish'
dotnet publish (Join-Path $PSScriptRoot 'TestGame.csproj') -c Release -o $publish | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
$library = Join-Path $PSScriptRoot 'library'
foreach ($game in $Games) {
    $folder = Join-Path $library $game
    New-Item -ItemType Directory -Force $folder | Out-Null
    Copy-Item (Join-Path $publish 'TestGame.exe') (Join-Path $folder "$game.exe") -Force
}
Get-ChildItem $library -Recurse -Filter *.exe | Select-Object FullName
