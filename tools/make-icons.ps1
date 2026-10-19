# Ritar tilläggets ikoner (16, 48, 128 px) och en butiksikon. Kör: powershell -File tools\make-icons.ps1
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# Ritar på 1024x1024 där motivet fyller (pad..1024-pad) och skalar sedan ned med hög kvalitet.
function Draw([int]$size, [float]$padFraction, [string]$path) {
    $S = 1024
    $big = New-Object System.Drawing.Bitmap $S, $S
    $g = [System.Drawing.Graphics]::FromImage($big)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = $S * $padFraction
    $w = $S - 2 * $pad
    $cardH = $w * 0.68
    $x = $pad; $y = ($S - $cardH) / 2

    # Skugga
    $shadow = RoundRect ($x + $w * 0.015) ($y + $w * 0.03) $w $cardH ($w * 0.09)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(60, 0, 0, 0))), $shadow)

    # Kortet med blå gradient
    $card = RoundRect $x $y $w $cardH ($w * 0.09)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF $x, $y), (New-Object System.Drawing.PointF ($x + $w), ($y + $cardH)), ([System.Drawing.Color]::FromArgb(0, 120, 212)), ([System.Drawing.Color]::FromArgb(0, 69, 140))
    $g.FillPath($grad, $card)

    # Chip
    $cw = $w * 0.24; $ch = $w * 0.19
    $cx = $x + $w * 0.11; $cy = $y + $cardH * 0.36
    $chip = RoundRect $cx $cy $cw $ch ($w * 0.035)
    $gold = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF $cx, $cy), (New-Object System.Drawing.PointF ($cx + $cw), ($cy + $ch)), ([System.Drawing.Color]::FromArgb(250, 214, 110)), ([System.Drawing.Color]::FromArgb(212, 160, 40))
    $g.FillPath($gold, $chip)
    if ($size -ge 48) {
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(150, 120, 80, 10)), ($w * 0.012)
        $g.DrawLine($pen, $cx, $cy + $ch / 2, $cx + $cw, $cy + $ch / 2)
        $g.DrawLine($pen, $cx + $cw * 0.36, $cy, $cx + $cw * 0.36, $cy + $ch)
        $g.DrawLine($pen, $cx + $cw * 0.64, $cy, $cx + $cw * 0.64, $cy + $ch)
    }

    # Grön bock i en vit cirkel nere till höger = "inloggad"
    $r = $w * 0.20
    $bx = $x + $w * 0.80; $by = $y + $cardH * 0.80
    $g.FillEllipse([System.Drawing.Brushes]::White, $bx - $r, $by - $r, 2 * $r, 2 * $r)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(26, 127, 55))), $bx - $r * 0.82, $by - $r * 0.82, 1.64 * $r, 1.64 * $r)
    $tick = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ($r * 0.26)
    $tick.StartCap = 'Round'; $tick.EndCap = 'Round'; $tick.LineJoin = 'Round'
    $g.DrawLines($tick, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ($bx - $r * 0.42), ($by + $r * 0.02)),
        (New-Object System.Drawing.PointF ($bx - $r * 0.10), ($by + $r * 0.32)),
        (New-Object System.Drawing.PointF ($bx + $r * 0.45), ($by - $r * 0.30))))

    # Rader som antyder text på kortet (bara på större storlekar)
    if ($size -ge 48) {
        $line = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(150, 255, 255, 255))
        $g.FillPath($line, (RoundRect ($x + $w * 0.11) ($y + $cardH * 0.16) ($w * 0.42) ($w * 0.045) ($w * 0.022)))
        $g.FillPath($line, (RoundRect ($x + $w * 0.11) ($y + $cardH * 0.78) ($w * 0.36) ($w * 0.035) ($w * 0.017)))
    }
    $g.Dispose()

    $out = New-Object System.Drawing.Bitmap $size, $size
    $g2 = [System.Drawing.Graphics]::FromImage($out)
    $g2.InterpolationMode = 'HighQualityBicubic'
    $g2.PixelOffsetMode = 'HighQuality'
    $g2.DrawImage($big, 0, 0, $size, $size)
    $g2.Dispose(); $big.Dispose()
    $out.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $out.Dispose()
}

# Tilläggets ikoner: fyller nästan hela ytan (små storlekar behöver allt utrymme).
Draw 16  0.02 (Join-Path $root 'extension\icons\16.png')
Draw 48  0.04 (Join-Path $root 'extension\icons\48.png')
Draw 128 0.10 (Join-Path $root 'extension\icons\128.png')
# Butiksikon: Google vill ha motivet inom 96x96 med 16 px genomskinlig kant.
New-Item -ItemType Directory -Force (Join-Path $root 'docs\store') | Out-Null
Draw 128 0.125 (Join-Path $root 'docs\store\icon-128.png')
Write-Host 'Ikoner skapade.'
