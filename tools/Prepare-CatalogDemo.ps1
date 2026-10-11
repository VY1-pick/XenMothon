param()
$ErrorActionPreference = 'Stop'
$workspacePath = Split-Path $PSScriptRoot -Parent
$dataPath = Join-Path $workspacePath 'artifacts\catalog-demo'
$manifestPath = Join-Path $dataPath 'catalog.json'
if (Test-Path -LiteralPath $manifestPath) { Write-Host "Existing catalog preserved: $manifestPath"; return }
New-Item -ItemType Directory -Path (Join-Path $dataPath 'media') -Force | Out-Null
Add-Type -AssemblyName System.Drawing
$items = foreach ($demo in @(
    @{ Id='20000000-0000-0000-0000-000000000001'; Title='Demo Aurora'; File='aurora_thumb.jpg'; Category='nature'; Tags=@('aurora','night') },
    @{ Id='20000000-0000-0000-0000-000000000002'; Title='Demo Tokyo'; File='neo_tokyo_thumb.jpg'; Category='cities'; Tags=@('tokyo','city','neon') }
)) {
    $sourcePath = Join-Path $workspacePath "src\XenMotion.UI\Assets\Catalog\$($demo.File)"
    if (-not (Test-Path -LiteralPath $sourcePath)) { throw "Missing demo artwork: $sourcePath" }
    Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $dataPath "media\$($demo.File)")
    $image = [System.Drawing.Image]::FromFile($sourcePath)
    try {
        @{
            id=$demo.Id; title=$demo.Title; description='Bundled demonstration artwork for local server verification.'
            author='XenMotion demo'; type='Image'; width=$image.Width; height=$image.Height; durationSeconds=0
            categoryId=$demo.Category; tags=$demo.Tags; isFeatured=$true; rating=0; ratingCount=0; downloadsCount=0
            createdAtUtc='2026-10-09T00:00:00Z'; updatedAtUtc='2026-10-09T00:00:00Z'
            mediaPath="media/$($demo.File)"; thumbnailPath="media/$($demo.File)"; previewPath="media/$($demo.File)"
        }
    } finally { $image.Dispose() }
}
$manifest = @{
    categories=@(
        @{id='all'; displayName='All Wallpapers'; description='All catalog wallpapers'; iconGlyph=[string][char]0xE71D; itemCount=0},
        @{id='nature'; displayName='Nature & Landscapes'; description='Natural scenes'; iconGlyph=[string][char]0xE707; itemCount=0},
        @{id='cities'; displayName='Cities & Architecture'; description='City scenes'; iconGlyph=[string][char]0xE80F; itemCount=0}
    )
    items=@($items)
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Host "Demo catalog ready: $manifestPath"
