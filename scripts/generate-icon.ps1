$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outDir = Join-Path $PSScriptRoot '..\NetMeter\assets'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$icoPath = Join-Path $outDir 'app.ico'

function Add-Pt([float]$x, [float]$y) {
    New-Object System.Drawing.PointF($x, $y)
}

function New-AppBitmap([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    $rect = New-Object System.Drawing.Rectangle(0, 0, $s, $s)
    $r = [int]($s * 0.22)
    $d = $r * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect,
        [System.Drawing.Color]::FromArgb(255, 30, 136, 229),
        [System.Drawing.Color]::FromArgb(255, 13, 71, 161), 45.0)
    $g.FillPath($bg, $path)

    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
    $lblue = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 227, 242, 253))

    $upHead = @((Add-Pt 0.30 0.08), (Add-Pt 0.12 0.30), (Add-Pt 0.48 0.30))
    $g.FillPolygon($white, $upHead)
    $g.FillRectangle($white, [float](0.24 * $s), [float](0.30 * $s), [float](0.12 * $s), [float](0.22 * $s))

    $downHead = @((Add-Pt 0.52 0.70), (Add-Pt 0.88 0.70), (Add-Pt 0.70 0.92))
    $g.FillPolygon($lblue, $downHead)
    $g.FillRectangle($lblue, [float](0.62 * $s), [float](0.48 * $s), [float](0.16 * $s), [float](0.22 * $s))

    $white.Dispose(); $lblue.Dispose(); $bg.Dispose(); $path.Dispose(); $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($sz in $sizes) {
    $bmp = New-AppBitmap $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , ($ms.ToArray())
    $ms.Dispose()
    $bmp.Dispose()
}

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$pngs.Count)

$offset = 6 + 16 * $pngs.Count
for ($i = 0; $i -lt $pngs.Count; $i++) {
    $sz = $sizes[$i]
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$dim)
    $bw.Write([byte]$dim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$pngs[$i].Length)
    $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $bw.Write($png) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())
$bw.Dispose()
$ms.Dispose()

$sizeBytes = (Get-Item $icoPath).Length
Write-Host "Icon written: $icoPath ($sizeBytes bytes, $($pngs.Count) sizes)"
