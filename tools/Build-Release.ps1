[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = "$PSScriptRoot\..\artifacts\publish\win-x64",
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot\..").Path
Set-Location $root

Write-Host "== XenMotion release build ==" -ForegroundColor Cyan
dotnet restore XenMotion.sln
if (-not $SkipTests) {
    dotnet test tests\XenMotion.Core.Tests\XenMotion.Core.Tests.csproj -c $Configuration --no-restore
}
dotnet build XenMotion.sln -c $Configuration --no-restore
dotnet publish src\XenMotion.UI\XenMotion.UI.csproj -c $Configuration -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=false -o $OutputDirectory

$version = (Select-String -Path 'src\XenMotion.UI\XenMotion.UI.csproj' -Pattern '<Version>([^<]+)</Version>').Matches.Groups[1].Value
$marker = Join-Path $OutputDirectory 'XENMOTION-RELEASE.txt'
@(
    "XenMotion release",
    "Version: $version",
    "Runtime: win-x64",
    "Configuration: $Configuration",
    "BuiltUtc: $([DateTime]::UtcNow.ToString('O'))"
) | Set-Content -Path $marker -Encoding UTF8

$hashFile = Join-Path $OutputDirectory 'SHA256SUMS.txt'
Get-ChildItem $OutputDirectory -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } |
    ForEach-Object { "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash, $_.Name } |
    Set-Content -Path $hashFile -Encoding UTF8

Write-Host "Release published to $OutputDirectory" -ForegroundColor Green
Write-Host "Version: $version" -ForegroundColor Green
