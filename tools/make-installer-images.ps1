#Requires -Version 7
<#
.SYNOPSIS
    Generates the Inno Setup wizard images in installer\images from src\RoboXopy\Assets\Logo.png.

.DESCRIPTION
    Inno Setup picks the image that best matches the wizard's DPI, so one file is written per DPI step:
    - large-<w>x<h>.png: the panel on the left of the Setup Completed page (the logo's gradient with its
      white sync glyph).
    - small-<n>.png: the logo in the top-right corner of the other pages.
    Run this again only when the logo changes; the generated images are checked in.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$logoPath = Join-Path $root 'src\RoboXopy\Assets\Logo.png'
$outDir = Join-Path $root 'installer\images'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

# Image area sizes Inno Setup uses at 100%-250% DPI (see the WizardImageFile and WizardSmallImageFile docs).
$largeSizes = @(@(202, 386), @(269, 515), @(336, 643), @(403, 772), @(430, 824), @(498, 953), @(534, 1022))
$smallSizes = @(58, 77, 97, 116, 124, 143, 159)

$top = [Drawing.Color]::FromArgb(44, 150, 214)
$bottom = [Drawing.Color]::FromArgb(6, 173, 158)

function New-Graphics([Drawing.Bitmap] $bitmap) {
    $g = [Drawing.Graphics]::FromImage($bitmap)
    $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
    return $g
}

$logo = [Drawing.Bitmap]::new($logoPath)
try {
    # Lift the white glyph off the logo's gradient: whiteness above the local background red level becomes alpha.
    $glyph = [Drawing.Bitmap]::new($logo.Width, $logo.Height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $minX = $logo.Width; $minY = $logo.Height; $maxX = 0; $maxY = 0
    for ($y = 0; $y -lt $logo.Height; $y++) {
        $background = 46 - 0.18 * $y
        for ($x = 0; $x -lt $logo.Width; $x++) {
            $c = $logo.GetPixel($x, $y)
            if ($c.A -lt 255) { continue }
            $white = [Math]::Min($c.R, [Math]::Min($c.G, $c.B))
            $alpha = [int][Math]::Round([Math]::Clamp(($white - $background) / (255 - $background), 0, 1) * 255)
            if ($alpha -lt 8) { continue }
            $glyph.SetPixel($x, $y, [Drawing.Color]::FromArgb($alpha, 255, 255, 255))
            $minX = [Math]::Min($minX, $x); $maxX = [Math]::Max($maxX, $x)
            $minY = [Math]::Min($minY, $y); $maxY = [Math]::Max($maxY, $y)
        }
    }
    $glyphRect = [Drawing.Rectangle]::new($minX, $minY, $maxX - $minX + 1, $maxY - $minY + 1)

    foreach ($size in $largeSizes) {
        $w, $h = $size
        $bitmap = [Drawing.Bitmap]::new($w, $h, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = New-Graphics $bitmap
        try {
            $brush = [Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Rectangle]::new(0, 0, $w, $h), $top, $bottom, 70.0)
            $g.FillRectangle($brush, 0, 0, $w, $h)
            $brush.Dispose()

            $glyphWidth = [int]($w * 0.5)
            $glyphHeight = [int]($glyphWidth * $glyphRect.Height / $glyphRect.Width)
            $dest = [Drawing.Rectangle]::new([int](($w - $glyphWidth) / 2), [int]($h * 0.36 - $glyphHeight / 2), $glyphWidth, $glyphHeight)
            $g.DrawImage($glyph, $dest, $glyphRect, [Drawing.GraphicsUnit]::Pixel)
        }
        finally {
            $g.Dispose()
        }

        $bitmap.Save((Join-Path $outDir "large-${w}x${h}.png"), [Drawing.Imaging.ImageFormat]::Png)
        $bitmap.Dispose()
    }

    foreach ($n in $smallSizes) {
        $bitmap = [Drawing.Bitmap]::new($n, $n, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = New-Graphics $bitmap
        try {
            $g.DrawImage($logo, 0, 0, $n, $n)
        }
        finally {
            $g.Dispose()
        }

        $bitmap.Save((Join-Path $outDir "small-$n.png"), [Drawing.Imaging.ImageFormat]::Png)
        $bitmap.Dispose()
    }

    $glyph.Dispose()
}
finally {
    $logo.Dispose()
}

Get-ChildItem $outDir -Filter *.png | ForEach-Object { Write-Host "  $($_.Name)" }
