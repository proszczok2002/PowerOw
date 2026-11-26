$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path (Split-Path -Parent $PSScriptRoot) 'assets'
$mark = [Drawing.Image]::FromFile((Join-Path $assets 'PowerOw.png'))
$surface = [Drawing.Bitmap]::new(328, 628, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
$small = [Drawing.Bitmap]::new(120, 120, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
try {
    $graphics = [Drawing.Graphics]::FromImage($surface)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $bounds = [Drawing.Rectangle]::new(0, 0, 328, 628)
    $tone = [Drawing.Drawing2D.LinearGradientBrush]::new($bounds, [Drawing.Color]::FromArgb(18, 32, 53),
        [Drawing.Color]::FromArgb(29, 58, 83), 62)
    $graphics.FillRectangle($tone, $bounds)
    foreach ($size in 208, 262, 322, 396, 480, 560, 650) {
        $color = if ($size -le 322) { [Drawing.Color]::FromArgb(65, 123, 151) } else { [Drawing.Color]::FromArgb(42, 83, 110) }
        $pen = [Drawing.Pen]::new($color, 1.6)
        $graphics.DrawEllipse($pen, (328 - $size) / 2, (628 - $size) / 2, $size, $size)
        $pen.Dispose()
    }
    $graphics.DrawImage($mark, 75, 225, 178, 178)
    $graphics.Dispose()
    $tone.Dispose()
    $surface.Save((Join-Path $assets 'PowerOw-Setup.bmp'), [Drawing.Imaging.ImageFormat]::Bmp)
    $graphics = [Drawing.Graphics]::FromImage($small)
    $graphics.Clear([Drawing.Color]::White)
    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.DrawImage($mark, 7, 7, 106, 106)
    $graphics.Dispose()
    $small.Save((Join-Path $assets 'PowerOw-Setup-Small.bmp'), [Drawing.Imaging.ImageFormat]::Bmp)
}
finally {
    $mark.Dispose()
    $surface.Dispose()
    $small.Dispose()
}
