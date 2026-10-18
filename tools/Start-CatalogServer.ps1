param(
    [string]$DataRoot,
    [ValidateRange(1024,65535)][int]$Port = 5188,
    [switch]$Static
)
$ErrorActionPreference = 'Stop'
$workspacePath = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($DataRoot)) {
    & (Join-Path $PSScriptRoot 'Prepare-CatalogDemo.ps1')
    $DataRoot = Join-Path $workspacePath 'artifacts\catalog-demo'
}
$DataRoot = (Resolve-Path -LiteralPath $DataRoot).Path
if (-not (Test-Path -LiteralPath (Join-Path $DataRoot 'catalog.json'))) { throw 'The catalog directory must contain catalog.json.' }

if ($Static) {
    # Static mode serves catalog.json and the relative media files verbatim. It is the exact layout a
    # CDN, object store or static web host would publish, and it is what the desktop client's
    # "Online catalog file" mode reads.
    Write-Host "Static catalog: http://127.0.0.1:$Port/catalog.json"
    Write-Host 'Point Settings -> Catalog source -> "Online catalog file" at that URL.'
    $arguments = @('--urls', "http://127.0.0.1:$Port", '--static', '--Catalog:DataRoot', $DataRoot)
} else {
    Write-Host "Open in your browser: http://127.0.0.1:$Port/"
    $arguments = @('--urls', "http://127.0.0.1:$Port", '--Catalog:DataRoot', $DataRoot)
}

Write-Host 'Keep this terminal open. Press Ctrl+C to stop the local server.'
& dotnet run --project (Join-Path $workspacePath 'src\XenMotion.Server\XenMotion.Server.csproj') --configuration Release -- @arguments
if ($LASTEXITCODE -ne 0) { throw "Catalog server exited with code $LASTEXITCODE" }
