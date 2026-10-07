$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path $PSScriptRoot '../src/XenMotion.UI/Assets'
[IO.Directory]::CreateDirectory($assets) | Out-Null
# A vector-native crossed ribbon, rasterized only for Windows icon formats.
$svg = @'
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">
<defs><linearGradient id="x" x2="1" y2="1"><stop stop-color="#99ffe4"/><stop offset="1" stop-color="#42bfff"/></linearGradient></defs>
<rect x="8" y="8" width="240" height="240" rx="64" fill="#101e30" stroke="#588e9e" stroke-width="2"/>
<path d="M54 55H99L202 201H157Z" fill="url(#x)"/>
<path d="M158 55H202L143 139L121 107Z M111 148L90 178L73 201H29L89 116Z" fill="#d9fff5"/>
<path d="M65 61H95L187 191" fill="none" stroke="#e3fff9" stroke-opacity=".7" stroke-width="3"/>
</svg>
'@
[IO.File]::WriteAllText((Join-Path $assets 'xenmotion-logo.svg'), $svg)
$bitmap = [Drawing.Bitmap]::new(256,256)
$g = [Drawing.Graphics]::FromImage($bitmap)
$g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([Drawing.Color]::Transparent)
$path = [Drawing.Drawing2D.GraphicsPath]::new()
foreach($r in @(@(8,8),@(120,8),@(120,120),@(8,120))) {
    $angle = if($r[0] -eq 8 -and $r[1] -eq 8){180} elseif($r[1] -eq 8){270} elseif($r[0] -eq 120){0} else{90}
    $path.AddArc($r[0],$r[1],128,128,$angle,90)
}
$path.CloseFigure()
$bg=[Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#101e30'))
$rim=[Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#588e9e'),2)
$g.FillPath($bg,$path); $g.DrawPath($rim,$path)
$gradient=[Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Point]::new(54,55),[Drawing.Point]::new(202,201),[Drawing.ColorTranslator]::FromHtml('#99ffe4'),[Drawing.ColorTranslator]::FromHtml('#42bfff'))
$white=[Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#d9fff5'))
function Points($pairs) { [Drawing.Point[]]($pairs | ForEach-Object { [Drawing.Point]::new($_[0],$_[1]) }) }
$g.FillPolygon($gradient,(Points @(@(54,55),@(99,55),@(202,201),@(157,201))))
$g.FillPolygon($white,(Points @(@(158,55),@(202,55),@(143,139),@(121,107))))
$g.FillPolygon($white,(Points @(@(111,148),@(73,201),@(29,201),@(89,116))))
$bitmap.Save((Join-Path $assets 'xenmotion-logo.png'),[Drawing.Imaging.ImageFormat]::Png)
$stream=[IO.MemoryStream]::new()
$bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
$bytes=$stream.ToArray()
$writer=[IO.BinaryWriter]::new([IO.File]::Create((Join-Path $assets 'xenmotion-logo.ico')))
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]1)
$writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0)
$writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$bytes.Length); $writer.Write([uint32]22); $writer.Write($bytes)
$writer.Dispose(); $stream.Dispose(); $gradient.Dispose(); $white.Dispose(); $bg.Dispose(); $rim.Dispose(); $path.Dispose(); $g.Dispose(); $bitmap.Dispose()
