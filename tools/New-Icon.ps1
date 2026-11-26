# Creates the multi-resolution Windows icon from an original power-and-arrow mark.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path (Split-Path -Parent $PSScriptRoot) 'assets'
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($size in $sizes) {
    $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($size / 256.0, $size / 256.0)
    $outline = [Drawing.Drawing2D.GraphicsPath]::new()
    $outline.AddArc(8, 8, 105, 105, 180, 90)
    $outline.AddArc(143, 8, 105, 105, 270, 90)
    $outline.AddArc(143, 143, 105, 105, 0, 90)
    $outline.AddArc(8, 143, 105, 105, 90, 90)
    $outline.CloseFigure()
    $palette = [Drawing.Drawing2D.LinearGradientBrush]::new(
        [Drawing.RectangleF]::new(0, 0, 256, 256),
        [Drawing.Color]::FromArgb(37, 63, 94), [Drawing.Color]::FromArgb(10, 19, 36), 120)
    $graphics.FillPath($palette, $outline)
    $edge = [Drawing.Pen]::new([Drawing.Color]::FromArgb(83, 115, 143), 5)
    if ($size -ge 32) { $graphics.DrawPath($edge, $outline) }
    $ring = [Drawing.Pen]::new([Drawing.Color]::FromArgb(100, 228, 235), 23)
    $ring.StartCap = $ring.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawArc($ring, 57, 61, 142, 142, -47, 274)
    $arrow = [Drawing.Pen]::new([Drawing.Color]::FromArgb(218, 255, 161), 19)
    $arrow.StartCap = $arrow.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $arrow.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $graphics.DrawLine($arrow, 128, 122, 128, 49)
    $points = [Drawing.Point[]]@([Drawing.Point]::new(103, 72), [Drawing.Point]::new(128, 46), [Drawing.Point]::new(153, 72))
    $graphics.DrawLines($arrow, $points)
    $png = [IO.MemoryStream]::new()
    $bitmap.Save($png, [Drawing.Imaging.ImageFormat]::Png)
    if ($size -eq 256) { [IO.File]::WriteAllBytes((Join-Path $assets 'PowerOw.png'), $png.ToArray()) }
    [pscustomobject]@{ Size = $size; Bytes = $png.ToArray() }
    $png.Dispose()
    foreach ($object in $arrow, $ring, $edge, $palette, $outline, $graphics, $bitmap) { $object.Dispose() }
}
$stream = [IO.File]::Create((Join-Path $assets 'PowerOw.ico'))
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($image in $images) {
        $dimension = [byte]($image.Size % 256)
        $writer.Write($dimension)
        $writer.Write($dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$image.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $image.Bytes.Length
    }
    foreach ($image in $images) { $writer.Write([byte[]]$image.Bytes) }
} finally { $writer.Dispose() }
