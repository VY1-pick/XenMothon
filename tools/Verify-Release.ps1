[CmdletBinding()]
param(
    [string]$PublishDirectory = "$PSScriptRoot\..\artifacts\publish\win-x64"
)

$ErrorActionPreference = 'Stop'
$publish = (Resolve-Path $PublishDirectory).Path
$exe = Join-Path $publish 'XenMotion.UI.exe'
$marker = Join-Path $publish 'XENMOTION-RELEASE.txt'

if (-not (Test-Path $exe)) { throw "Missing XenMotion.UI.exe in release directory: $publish" }
if (-not (Test-Path $marker)) { throw "Missing XENMOTION-RELEASE.txt in release directory: $publish" }
foreach ($required in @('Runtime/Python/python.exe','QualityProbe/NativeProbe.exe','tools/AdaptiveQualityLab/lab.py','tools/AdaptiveQualityLab/report-template.html','LICENSE','BETA-TERMS.txt','BETA-TERMS.fa.txt','SHA256SUMS.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publish $required))) { throw "Missing release dependency: $required" }
}
foreach ($entry in Get-Content -LiteralPath (Join-Path $publish 'SHA256SUMS.txt')) {
    if ($entry -notmatch '^([A-Fa-f0-9]{64})  (.+)$') { throw 'Invalid checksum manifest' }
    $expected = $Matches[1]; $relative = $Matches[2]
    $path = [IO.Path]::GetFullPath((Join-Path $publish $relative))
    if (-not $path.StartsWith($publish.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Checksum path escapes publish directory' }
    if ((Get-FileHash -LiteralPath $path).Hash -ne $expected) { throw "Checksum mismatch: $relative" }
}

$blocked = Get-ChildItem $publish -Recurse -Force | Where-Object { $_.Name -in @('settings.json','xenmotion_metadata.db') }
if ($blocked) { throw "Release output contains runtime user-data files: $($blocked.FullName -join ', ')" }

Write-Host "Release package structure looks valid: $publish" -ForegroundColor Green
Get-Item $exe | Select-Object FullName, Length, LastWriteTime
Get-Content $marker
