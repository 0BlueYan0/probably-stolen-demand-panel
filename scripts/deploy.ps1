param(
    [string]$GameDir = 'D:\SteamLibrary\steamapps\common\Probably Stolen'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

dotnet build (Join-Path $root 'src\DemandPanel') -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

$bin = Join-Path $root 'src\DemandPanel\bin\Release\netstandard2.1'
$target = Join-Path $GameDir 'Mods\DemandPanel'

if (Test-Path $target) {
    Remove-Item -Recurse -Force $target
}
New-Item -ItemType Directory -Force (Join-Path $target 'Localization') | Out-Null

Copy-Item (Join-Path $bin 'DemandPanel.dll') $target
Copy-Item (Join-Path $bin 'DemandPanel.pdb') $target
Copy-Item (Join-Path $root 'mod\manifest.xml') $target
Copy-Item (Join-Path $root 'mod\Localization\*.csv') (Join-Path $target 'Localization')

Write-Host "Deployed to $target"
