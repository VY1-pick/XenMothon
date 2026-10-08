[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = "$PSScriptRoot\..\artifacts\publish\0.9.0-beta.2",
    [switch]$SkipTests,
    [string]$InnoCompiler = "$PSScriptRoot\..\artifacts\build-dependencies\inno\ISCC.exe"
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot\..").Path
Set-Location $root
function Run-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed ($LASTEXITCODE)" }
}
Run-Dotnet -Arguments @('restore','XenMotion.sln')
$auditText = & dotnet list XenMotion.sln package --vulnerable --include-transitive --format json | Out-String
if ($LASTEXITCODE -ne 0) { throw 'Dependency audit failed' }
$audit = $auditText | ConvertFrom-Json
$findings = @($audit.projects.frameworks.topLevelPackages.vulnerabilities; $audit.projects.frameworks.transitivePackages.vulnerabilities) | Where-Object { $_ }
if ($findings) { throw 'Vulnerable dependencies found; release stopped' }
Run-Dotnet -Arguments @('build','XenMotion.sln','-c',$Configuration,'--no-restore')
if (-not $SkipTests) {
    Run-Dotnet -Arguments @('test','tests/XenMotion.Core.Tests/XenMotion.Core.Tests.csproj','-c',$Configuration,'--no-build','--no-restore')
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$publish = (Resolve-Path $OutputDirectory).Path
Run-Dotnet -Arguments @('publish','src/XenMotion.UI/XenMotion.UI.csproj','-c',$Configuration,'-r','win-x64','--self-contained','true','--no-restore','-p:PublishSingleFile=false','-o',$publish)
Run-Dotnet -Arguments @('publish','tools/AdaptiveQualityLab/NativeProbe/NativeProbe.csproj','-c',$Configuration,'-r','win-x64','--self-contained','true','-o',(Join-Path $publish 'QualityProbe'))
$runtimeZip = Join-Path $root 'artifacts/build-dependencies/python-3.13.16-embed-amd64.zip'
if (-not (Test-Path -LiteralPath $runtimeZip)) {
    New-Item -ItemType Directory -Path (Split-Path $runtimeZip) -Force | Out-Null
    Invoke-WebRequest 'https://www.python.org/ftp/python/3.13.16/python-3.13.16-embed-amd64.zip' -OutFile $runtimeZip
}
if ((Get-FileHash -LiteralPath $runtimeZip).Hash -ne '97dae5274cc54867065e8d5a3226e48c35017ed332a0fdb0e27d5b5821961297') { throw 'Embedded runtime checksum mismatch' }
Expand-Archive -LiteralPath $runtimeZip -DestinationPath (Join-Path $publish 'Runtime/Python') -Force
$optimizer = Join-Path $publish 'tools/AdaptiveQualityLab'
New-Item -ItemType Directory -Path $optimizer -Force | Out-Null
Copy-Item -LiteralPath tools/AdaptiveQualityLab/lab.py,tools/AdaptiveQualityLab/report-template.html -Destination $optimizer
Copy-Item -LiteralPath LICENSE,docs/legal/BETA-TERMS.txt,docs/legal/BETA-TERMS.fa.txt -Destination $publish
Copy-Item -LiteralPath docs/engineering/beta2-review.md -Destination (Join-Path $publish 'BETA-REVIEW.md')
$version = ([xml](Get-Content 'src/XenMotion.UI/XenMotion.UI.csproj' -Raw)).Project.PropertyGroup.Version
$commit = git rev-parse HEAD
@("XenMotion Private Beta", "Version: $version", "Commit: $commit", "Runtime: win-x64", "BuiltUtc: $([DateTime]::UtcNow.ToString('O'))") |
    Set-Content -LiteralPath (Join-Path $publish 'XENMOTION-RELEASE.txt') -Encoding UTF8
$hashFile = Join-Path $publish 'SHA256SUMS.txt'
Get-ChildItem -LiteralPath $publish -Recurse -File | Where-Object { $_.FullName -ne $hashFile } |
    Sort-Object FullName | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName).Hash,[IO.Path]::GetRelativePath($publish,$_.FullName) } |
    Set-Content -LiteralPath $hashFile -Encoding UTF8
& "$PSScriptRoot/Verify-Release.ps1" -PublishDirectory $publish
if (-not (Test-Path -LiteralPath $InnoCompiler)) { throw 'Inno Setup compiler missing; published application preserved.' }
$release = Join-Path $root "artifacts/releases/$version"
New-Item -ItemType Directory -Path $release -Force | Out-Null
& $InnoCompiler "/DPublishDir=$publish" "/DReleaseDir=$release" "$PSScriptRoot/XenMotion.iss"
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
Get-ChildItem -LiteralPath $release -Filter '*.exe' | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName).Hash,$_.Name } |
    Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding UTF8
Write-Host "Installer ready: $release"
