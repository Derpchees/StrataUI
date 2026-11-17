# Dibuja los punteros de Strata UI (blanco y negro) y los deja en src\Strata.Shell\Assets\Cursors.
# Formas vectoriales en una cuadrícula de 32; cada .cur lleva varios tamaños (PNG) y los de espera
# son .ani. Correr de nuevo después de cambiar una forma: los archivos generados van en el repositorio.
param([string]$Out = (Join-Path $PSScriptRoot '..\src\Strata.Shell\Assets\Cursors'))

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
Add-Type -TypeDefinition @'
public static class CursorLoader {
  [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
  public static extern System.IntPtr LoadImage(System.IntPtr inst, string name, uint type, int cx, int cy, uint flags);
}
'@

$Sizes = 32, 48, 64, 96, 128
$Scale = 0.8   # el dibujo ocupa ~26 de 32: deja lugar a la sombra y mide como los de Windows
$FrameCount = 12

$Variants = [ordered]@{
    White = @{ Fill = '#FFFFFF'; Line = '#1B1B1B' }
    Black = @{ Fill = '#1B1B1B'; Line = '#FFFFFF' }
}

# ── Geometría ────────────────────────────────────────────────────────────────

function P([string]$d) { [System.Windows.Media.Geometry]::Parse($d) }
function RR($x, $y, $w, $h, $r) { New-Object System.Windows.Media.RectangleGeometry((New-Object System.Windows.Rect($x, $y, $w, $h)), $r, $r) }
function Circle($cx, $cy, $r) { New-Object System.Windows.Media.EllipseGeometry((New-Object System.Windows.Point($cx, $cy)), $r, $r) }
function Union([System.Windows.Media.Geometry[]]$parts) {
    $g = $parts[0]
    foreach ($p in $parts | Select-Object -Skip 1) { $g = [System.Windows.Media.Geometry]::Combine($g, $p, 'Union', $null) }
    $g.GetOutlinedPathGeometry()  # un solo contorno, sin costuras entre las piezas
}
# Esquinas redondeadas: la forma con su borde ensanchado (crece r/2 hacia afuera).
function Soft($geo, $r) { Union @($geo, (Stroke $geo $r)) }
function Cut($a, $b) { [System.Windows.Media.Geometry]::Combine($a, $b, 'Exclude', $null) }
function Turn($geo, $angle, $cx = 16, $cy = 16, $dx = 0, $dy = 0) {
    $t = New-Object System.Windows.Media.TransformGroup
    $t.Children.Add((New-Object System.Windows.Media.RotateTransform($angle, $cx, $cy)))
    $t.Children.Add((New-Object System.Windows.Media.TranslateTransform($dx, $dy)))
    $g = $geo.Clone(); $g.Transform = $t
    [System.Windows.Media.Geometry]::Combine($g, [System.Windows.Media.Geometry]::Empty, 'Union', $null)
}
# Trazo grueso de redondeado (arcos del indicador de espera, líneas) convertido en relleno.
function Stroke($geo, $width) {
    $pen = New-Object System.Windows.Media.Pen([System.Windows.Media.Brushes]::Black, $width)
    $pen.StartLineCap = 'Round'; $pen.EndLineCap = 'Round'; $pen.LineJoin = 'Round'
    $geo.GetWidenedPathGeometry($pen).GetOutlinedPathGeometry()  # sin el contorno, el trazo ensanchado se rellena a rayas
}
function Arc($cx, $cy, $r, $from, $sweep) {
    $a = $from * [math]::PI / 180; $b = ($from + $sweep) * [math]::PI / 180
    $x1 = $cx + $r * [math]::Cos($a); $y1 = $cy + $r * [math]::Sin($a)
    $x2 = $cx + $r * [math]::Cos($b); $y2 = $cy + $r * [math]::Sin($b)
    $large = [int]($sweep -gt 180)
    P ([string]::Format([Globalization.CultureInfo]::InvariantCulture, 'M {0},{1} A {2},{2} 0 {3} 1 {4},{5}', $x1, $y1, $r, $large, $x2, $y2))
}
function Glyph([string]$text, $cx, $cy, $em) {
    $ft = New-Object System.Windows.Media.FormattedText($text, [Globalization.CultureInfo]::InvariantCulture, 'LeftToRight',
        (New-Object System.Windows.Media.Typeface((New-Object System.Windows.Media.FontFamily('Segoe UI')), 'Normal', 'Bold', 'Normal')),
        $em, [System.Windows.Media.Brushes]::Black, 1.0)
    $g = $ft.BuildGeometry((New-Object System.Windows.Point(0, 0)))
    $b = $g.Bounds
    Turn $g 0 0 0 ($cx - $b.X - $b.Width / 2) ($cy - $b.Y - $b.Height / 2)
}

# Cada capa: geometría, relleno (Fill/Line/$null) y si lleva contorno.
function L($geo, $fill = 'Fill', [bool]$outline = $true) { @{ G = $geo; F = $fill; O = $outline } }

$arrow = Soft (P 'M 6,4.5 L 6,23.5 L 11.2,18.8 L 19.5,18.8 Z') 2.4  # sin cola: una punta limpia
$ns = Soft (P 'M 16,2.5 L 22,9 L 18,9 L 18,23 L 22,23 L 16,29.5 L 10,23 L 14,23 L 14,9 L 10,9 Z') 1.6
$nsMove = P 'M 16,2 L 20.5,7 L 17.6,7 L 17.6,25 L 20.5,25 L 16,30 L 11.5,25 L 14.4,25 L 14.4,7 L 11.5,7 Z'  # puntas chicas: con las grandes la cruz parece un rombo

function Spinner($cx, $cy, $r, $width, $frame) {
    $ring = Cut (Circle $cx $cy ($r + $width / 2)) (Circle $cx $cy ($r - $width / 2))
    $arc = Stroke (Arc $cx $cy $r ($frame * 360 / $FrameCount - 90) 110) ($width * 0.55)
    @((L $ring), (L $arc 'Line' $false))
}

$hand = Union @(
    (RR 10.5 2 5 16 2.5), (RR 15.5 9.5 4.6 9 2.3), (RR 20.1 10.5 4.4 9 2.2), (RR 24.5 12.5 4 8 2),
    (RR 10.5 13 18 16 5), (Turn (RR 3.5 16 11 5 2.5) 32 14 18.5))
$handLines = Union @(
    (Stroke (P 'M 15.5,12.5 L 15.5,16.5') 0.9), (Stroke (P 'M 20.1,13.5 L 20.1,17') 0.9), (Stroke (P 'M 24.5,15 L 24.5,18') 0.9))

$pen = Union @((P 'M 16,3 L 12.8,9.5 L 19.2,9.5 Z'), (RR 12.8 9.5 6.4 15 1), (RR 12.8 25.5 6.4 3.5 1.5))

$Cursors = [ordered]@{
    arrow       = @{ Hot = 5, 3;   Layers = { @(L $arrow) } }
    help        = @{ Hot = 5, 3;   Layers = { @((L $arrow), (L (Circle 24 24 5.5)), (L (Glyph '?' 24 24 8.5) 'Line' $false)) } }
    working     = @{ Hot = 5, 3;   Layers = { param($f) @((L $arrow), (L (Circle 24 24 5.5))) + (Spinner 24 24 3 2.6 $f) }; Animated = $true }
    busy        = @{ Hot = 16, 16; Layers = { param($f) Spinner 16 16 9 4.6 $f }; Animated = $true }
    cross       = @{ Hot = 16, 16; Layers = { @(L (Union @((RR 2.5 14.6 10.5 2.8 1.4), (RR 19 14.6 10.5 2.8 1.4), (RR 14.6 2.5 2.8 10.5 1.4), (RR 14.6 19 2.8 10.5 1.4)))) } }
    text        = @{ Hot = 16, 16; Layers = { @(L (Union @((RR 14.6 5 2.8 22 0), (RR 11 3.6 10 2.8 1.4), (RR 11 25.6 10 2.8 1.4)))) } }
    pen         = @{ Hot = 4, 4;   Layers = { @((L (Turn $pen -45 16 3 -12 1)), (L (Turn (Stroke (P 'M 13.3,9.5 L 18.7,9.5') 0.9) -45 16 3 -12 1) 'Line' $false)) } }
    unavailable = @{ Hot = 16, 16; Layers = { @(L (Union @((Cut (Circle 16 16 11) (Circle 16 16 7.4)), (Turn (RR 6 14.2 20 3.6 0) 45)))) } }
    vert        = @{ Hot = 16, 16; Layers = { @(L $ns) } }
    horz        = @{ Hot = 16, 16; Layers = { @(L (Turn $ns 90)) } }
    dgn1        = @{ Hot = 16, 16; Layers = { @(L (Turn $ns -45)) } }
    dgn2        = @{ Hot = 16, 16; Layers = { @(L (Turn $ns 45)) } }
    move        = @{ Hot = 16, 16; Layers = { @(L (Union @($nsMove, (Turn $nsMove 90)))) } }
    alternate   = @{ Hot = 16, 2.5; Layers = { @(L (P 'M 16,2.5 L 23,10 L 18.2,10 L 18.2,29 L 13.8,29 L 13.8,10 L 9,10 Z')) } }
    link        = @{ Hot = 13, 2;  Layers = { @((L $hand), (L $handLines 'Line' $false)) } }
    pin         = @{ Hot = 5, 3;   Layers = { @((L $arrow), (L (P 'M 24,30.5 C 21,27 19,24.5 19,22 A 5,5 0 1 1 29,22 C 29,24.5 27,27 24,30.5 Z')), (L (Circle 24 22 1.8) 'Line' $false)) } }
    person      = @{ Hot = 5, 3;   Layers = { @((L $arrow), (L (Union @((Circle 24 20 3), (P 'M 18.5,30.5 A 5.5,5.5 0 0 1 29.5,30.5 Z'))))) } }
}

# ── Dibujo y archivos ────────────────────────────────────────────────────────

function Png($layers, $px, $colors) {
    $fill = New-Object System.Windows.Media.SolidColorBrush([System.Windows.Media.ColorConverter]::ConvertFromString($colors.Fill))
    $line = New-Object System.Windows.Media.SolidColorBrush([System.Windows.Media.ColorConverter]::ConvertFromString($colors.Line))
    $outline = New-Object System.Windows.Media.Pen($line, 1.5); $outline.LineJoin = 'Round'
    $dv = New-Object System.Windows.Media.DrawingVisual
    $shadow = New-Object System.Windows.Media.Effects.DropShadowEffect
    $shadow.Color = [System.Windows.Media.Colors]::Black; $shadow.Opacity = 0.28; $shadow.Direction = 270
    $shadow.ShadowDepth = $px / 64; $shadow.BlurRadius = $px / 32 * 3
    $dv.Effect = $shadow
    $dc = $dv.RenderOpen()
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform(($px / 32 * $Scale), ($px / 32 * $Scale))))
    foreach ($l in $layers) {
        $brush = switch ($l.F) { 'Fill' { $fill } 'Line' { $line } default { $null } }
        $dc.DrawGeometry($brush, $(if ($l.O) { $outline } else { $null }), $l.G)
    }
    $dc.Close()
    $bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($px, $px, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($dv)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms = New-Object IO.MemoryStream; $enc.Save($ms); $ms.ToArray()
}

function CurFile($layers, $hot, $colors) {
    $images = foreach ($s in $Sizes) { , (Png $layers $s $colors) }
    $ms = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter($ms)
    $w.Write([uint16]0); $w.Write([uint16]2); $w.Write([uint16]$Sizes.Count)
    $offset = 6 + 16 * $Sizes.Count
    for ($i = 0; $i -lt $Sizes.Count; $i++) {
        $s = $Sizes[$i]; $k = $s / 32 * $Scale
        $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16][math]::Round($hot[0] * $k)); $w.Write([uint16][math]::Round($hot[1] * $k))
        $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset); $offset += $images[$i].Length
    }
    foreach ($img in $images) { $w.Write([byte[]]$img) }
    $w.Flush(); $ms.ToArray()
}

function Chunk($w, [string]$id, [byte[]]$data) {
    $w.Write([Text.Encoding]::ASCII.GetBytes($id)); $w.Write([uint32]$data.Length); $w.Write($data)
    if ($data.Length % 2) { $w.Write([byte]0) }
}

function AniFile([byte[][]]$frames, $jiffies) {
    $anih = New-Object IO.MemoryStream; $h = New-Object IO.BinaryWriter($anih)
    foreach ($v in 36, $frames.Count, $frames.Count, 0, 0, 0, 0, $jiffies, 1) { $h.Write([uint32]$v) }  # AF_ICON
    $fram = New-Object IO.MemoryStream; $f = New-Object IO.BinaryWriter($fram)
    $f.Write([Text.Encoding]::ASCII.GetBytes('fram'))
    foreach ($fr in $frames) { Chunk $f 'icon' $fr }
    $body = New-Object IO.MemoryStream; $b = New-Object IO.BinaryWriter($body)
    $b.Write([Text.Encoding]::ASCII.GetBytes('ACON'))
    Chunk $b 'anih' $anih.ToArray()
    Chunk $b 'LIST' $fram.ToArray()
    $all = New-Object IO.MemoryStream; $a = New-Object IO.BinaryWriter($all)
    Chunk $a 'RIFF' $body.ToArray()
    $all.ToArray()
}

foreach ($variant in $Variants.Keys) {
    $dir = Join-Path $Out $variant
    New-Item -ItemType Directory -Force $dir | Out-Null
    foreach ($name in $Cursors.Keys) {
        $c = $Cursors[$name]
        if ($c.Animated) {
            $frames = for ($i = 0; $i -lt $FrameCount; $i++) { , (CurFile (& $c.Layers $i) $c.Hot $Variants[$variant]) }
            $file = Join-Path $dir "$name.ani"
            [IO.File]::WriteAllBytes($file, (AniFile $frames 5))
        } else {
            $file = Join-Path $dir "$name.cur"
            [IO.File]::WriteAllBytes($file, (CurFile (& $c.Layers) $c.Hot $Variants[$variant]))
        }
        # Comprobación: Windows debe poder cargarlo (IMAGE_CURSOR, LR_LOADFROMFILE).
        if ([CursorLoader]::LoadImage([IntPtr]::Zero, $file, 2, 48, 48, 0x10) -eq [IntPtr]::Zero) { throw "Windows no carga $file" }
    }
}
Write-Host "Punteros generados en $((Resolve-Path $Out).Path)"
