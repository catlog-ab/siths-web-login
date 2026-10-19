# Ritar en butiksbild (1280x800, 24-bitars PNG utan alfa) som visar popupens tre lägen.
# Kör: powershell -File tools\make-screenshot.ps1 [-Name "Catlog SITHS"]
param([string]$Name = 'Catlog SITHS')
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); return $p
}
function C([int]$r, [int]$g, [int]$b, [int]$a = 255) { [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }
function Brush($c) { New-Object System.Drawing.SolidBrush $c }
function Font([float]$size, [string]$style = 'Regular') { New-Object System.Drawing.Font 'Segoe UI', $size, ([System.Drawing.FontStyle]$style), ([System.Drawing.GraphicsUnit]::Pixel) }

$W = 1280; $H = 800
$bmp = New-Object System.Drawing.Bitmap $W, $H, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'

# Bakgrund
$bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point $W, $H), (C 232 241 251), (C 246 248 250)
$g.FillRectangle((Brush (C 232 241 251)), 0, 0, $W, $H)
$g.SmoothingMode = 'None'
$g.FillRectangle($bg, -2, -2, $W + 4, $H + 4)
$g.SmoothingMode = 'AntiAlias'

# Rubrik
$center = New-Object System.Drawing.StringFormat; $center.Alignment = 'Center'
$g.DrawString('Logga in med SITHS-kortet', (Font 52 'Bold'), (Brush (C 13 37 63)), (New-Object System.Drawing.RectangleF 0, 70, $W, 70), $center)
$g.DrawString('Kortet i läsaren loggar in. Kortet ur loggar ut – direkt, i alla flikar.', (Font 26), (Brush (C 87 96 106)), (New-Object System.Drawing.RectangleF 0, 150, $W, 40), $center)

# Tre popup-lägen
$icon = [System.Drawing.Image]::FromFile((Join-Path $root 'extension\icons\128.png'))
$states = @(
    @{ step = '1'; caption = 'Sätt i kortet'; title = 'Inget kort i läsaren'; detail = 'Sätt i ditt SITHS-kort för att logga in.'; dot = (C 140 149 159) },
    @{ step = '2'; caption = 'Ange PIN';      title = 'Loggar in…';           detail = 'Ange din PIN-kod i dialogen.';            dot = (C 191 135 0) },
    @{ step = '3'; caption = 'Klart';         title = 'Inloggad';             detail = 'Anna Andersson';                           dot = (C 26 127 55) }
)
$pw = 340; $ph = 300; $gap = 50
$x0 = ($W - (3 * $pw + 2 * $gap)) / 2; $y0 = 270
for ($i = 0; $i -lt 3; $i++) {
    $s = $states[$i]; $x = $x0 + $i * ($pw + $gap); $y = $y0

    # Stegnummer och bildtext
    $g.FillEllipse((Brush (C 0 94 184)), $x, $y, 44, 44)
    $g.DrawString($s.step, (Font 24 'Bold'), [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF $x, ($y + 6), 44, 34), $center)
    $g.DrawString($s.caption, (Font 26 'Bold'), (Brush (C 13 37 63)), ($x + 56), ($y + 4))

    # Popupen
    $py = $y + 70
    $g.FillPath((Brush (C 0 0 0 28)), (RoundRect ($x + 4) ($py + 8) $pw $ph 14))
    $g.FillPath([System.Drawing.Brushes]::White, (RoundRect $x $py $pw $ph 14))
    $g.DrawPath((New-Object System.Drawing.Pen (C 208 215 222), 1.5), (RoundRect $x $py $pw $ph 14))

    # Ikon och namn överst
    $g.DrawImage($icon, ($x + 24), ($py + 22), 64, 64)
    $g.DrawString($Name, (Font 24 'Bold'), (Brush (C 31 35 40)), ($x + 100), ($py + 38))
    $g.DrawLine((New-Object System.Drawing.Pen (C 208 215 222), 1.5), ($x + 24), ($py + 106), ($x + $pw - 24), ($py + 106))

    $g.DrawString('SITHS-inloggning', (Font 18 'Bold'), (Brush (C 87 96 106)), ($x + 24), ($py + 126))
    $g.FillEllipse((Brush $s.dot), ($x + 26), ($py + 172), 16, 16)
    $g.DrawString($s.title, (Font 24 'Bold'), (Brush (C 31 35 40)), ($x + 52), ($py + 164))
    $g.DrawString($s.detail, (Font 19), (Brush (C 87 96 106)), (New-Object System.Drawing.RectangleF ($x + 24), ($py + 210), ($pw - 48), 70))
}

# Fot
$g.DrawString('Kräver Windows, kortläsare, Net iD eller SITHS eID samt kortprogrammet SithsWebLogin.', (Font 20), (Brush (C 87 96 106)), (New-Object System.Drawing.RectangleF 0, 730, $W, 30), $center)

$g.Dispose(); $icon.Dispose()
New-Item -ItemType Directory -Force (Join-Path $root 'docs\store') | Out-Null
$out = Join-Path $root 'docs\store\screenshot-1280x800.png'
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "Skapad: $out"
