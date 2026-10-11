param(
    [string]$DataRoot,
    [ValidateRange(1024,65535)][int]$Port = 5188
)
$ErrorActionPreference = 'Stop'
$workspacePath = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($DataRoot)) {
    & (Join-Path $PSScriptRoot 'Prepare-CatalogDemo.ps1')
    $DataRoot = Join-Path $workspacePath 'artifacts\catalog-demo'
}
$DataRoot = (Resolve-Path -LiteralPath $DataRoot).Path
if (-not (Test-Path -LiteralPath (Join-Path $DataRoot 'catalog.json'))) { throw 'The catalog directory must contain catalog.json.' }
Write-Host "Open in your browser: http://127.0.0.1:$Port/"
Write-Host 'Keep this terminal open. Press Ctrl+C to stop the local server.'
& dotnet run --project (Join-Path $workspacePath 'src\XenMotion.Server\XenMotion.Server.csproj') --configuration Release -- --urls "http://127.0.0.1:$Port" --Catalog:DataRoot $DataRoot
if ($LASTEXITCODE -ne 0) { throw "Catalog server exited with code $LASTEXITCODE" }
