param(
    [string]$Video = '',
    [switch]$Economy,
    [switch]$ObserveBusy
)
$ErrorActionPreference = 'Stop'
$labRoot = $PSScriptRoot
$workspaceRoot = (Resolve-Path (Join-Path $labRoot '../..')).Path
$probePath = Join-Path $workspaceRoot 'artifacts/adaptive-quality-lab/probe/NativeProbe.exe'
if (-not (Test-Path -LiteralPath $probePath)) {
    & dotnet publish (Join-Path $labRoot 'NativeProbe/NativeProbe.csproj') -c Release -r win-x64 --self-contained true -o (Split-Path $probePath) --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Standalone probe build failed.' }
}
if (-not $Video) { $Video = (Read-Host 'Video path (leave empty for synthetic scenarios only)').Trim('"') }
$arguments = @('-B', (Join-Path $labRoot 'lab.py'), '--output', (Join-Path $workspaceRoot 'artifacts/adaptive-quality-lab-v2-final'))
$settingsPath = Join-Path $env:LOCALAPPDATA 'XenMotion/settings.json'
if (Test-Path -LiteralPath $settingsPath) { $arguments += @('--settings-file', $settingsPath) }
if ($Video) { $arguments += @('--video', $Video) }
if ($Economy) { $arguments += '--economy' }
if ($ObserveBusy) { $arguments += '--observe-busy' }
& python @arguments
if ($LASTEXITCODE -ne 0) { throw 'Quality lab execution failed.' }
$reportPath = Join-Path $workspaceRoot 'artifacts/adaptive-quality-lab-v2-final/report.html'
Start-Process -FilePath $reportPath -WindowStyle Hidden
