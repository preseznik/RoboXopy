# Generates src\RoboXopy\Assets\RoboXopy.ico (multi-size, PNG-compressed) and Logo.png.
# Run with Windows PowerShell or PowerShell 7 on Windows:  .\tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot "..\src\RoboXopy\Assets"
New-Item -ItemType Directory -Force -Path $assets | Out-Null

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Add-Arrow($g, [float]$cx, [float]$cy, [float]$radius, [float]$start, [float]$sweep, [float]$stroke, $color) {
    $pen = New-Object System.Drawing.Pen($color, $stroke)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($pen, $cx - $radius, $cy - $radius, $radius * 2, $radius * 2, $start, $sweep)
    $pen.Dispose()

    # Arrow head at the end of the arc, pointing along the direction of travel.
    $end = ($start + $sweep) * [Math]::PI / 180
    $ex = $cx + $radius * [Math]::Cos($end)
    $ey = $cy + $radius * [Math]::Sin($end)
    $tx = -[Math]::Sin($end) * [Math]::Sign($sweep)
    $ty = [Math]::Cos($end) * [Math]::Sign($sweep)
    $nx = [Math]::Cos($end)
    $ny = [Math]::Sin($end)
    $len = $stroke * 1.9
    $wid = $stroke * 1.55
    $pts = @(
        (New-Object System.Drawing.PointF(($ex + $tx * $len), ($ey + $ty * $len))),
        (New-Object System.Drawing.PointF(($ex + $nx * $wid - $tx * $stroke * 0.2), ($ey + $ny * $wid - $ty * $stroke * 0.2))),
        (New-Object System.Drawing.PointF(($ex - $nx * $wid - $tx * $stroke * 0.2), ($ey - $ny * $wid - $ty * $stroke * 0.2)))
    )
    $brush = New-Object System.Drawing.SolidBrush($color)
    $g.FillPolygon($brush, $pts)
    $brush.Dispose()
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [float]$size
    $inset = [Math]::Max(0.5, $s * 0.04)
    $rect = New-RoundedRect $inset $inset ($s - 2 * $inset) ($s - 2 * $inset) ($s * 0.22)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF($s, $s)),
        [System.Drawing.Color]::FromArgb(255, 0, 120, 212), [System.Drawing.Color]::FromArgb(255, 0, 178, 148))
    $g.FillPath($grad, $rect)

    # Subtle top highlight
    $hl = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF(0, $s)),
        [System.Drawing.Color]::FromArgb(48, 255, 255, 255), [System.Drawing.Color]::FromArgb(0, 255, 255, 255))
    $g.FillPath($hl, $rect)

    $white = [System.Drawing.Color]::White
    $stroke = [Math]::Max(1.6, $s * 0.085)
    $radius = $s * 0.24
    $c = $s / 2
    Add-Arrow $g $c $c $radius 200 125 $stroke $white
    Add-Arrow $g $c $c $radius 20 125 $stroke $white

    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($size -eq 256) { $bmp.Save((Join-Path $assets "Logo.png"), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    , $ms.ToArray()
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $w.Write($png) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets "RoboXopy.ico"), $out.ToArray())
Write-Host "Wrote $(Join-Path $assets 'RoboXopy.ico') and Logo.png"
