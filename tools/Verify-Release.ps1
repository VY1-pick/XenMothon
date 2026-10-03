[CmdletBinding()]
param(
    [string]$PublishDirectory = "$PSScriptRoot\..\artifacts\publish\win-x64"
)

$ErrorActionPreference = 'Stop'
$publish = (Resolve-Path $PublishDirectory).Path
$exe = Join-Path $publish 'Lumen.UI.exe'
$marker = Join-Path $publish 'LUMEN-RELEASE.txt'

if (-not (Test-Path $exe)) { throw "Missing Lumen.UI.exe in release directory: $publish" }
if (-not (Test-Path $marker)) { throw "Missing LUMEN-RELEASE.txt in release directory: $publish" }

$blocked = Get-ChildItem $publish -Recurse -Force | Where-Object { $_.Name -in @('settings.json','lumen_metadata.db') }
if ($blocked) { throw "Release output contains runtime user-data files: $($blocked.FullName -join ', ')" }

Write-Host "Release package structure looks valid: $publish" -ForegroundColor Green
Get-Item $exe | Select-Object FullName, Length, LastWriteTime
Get-Content $marker
