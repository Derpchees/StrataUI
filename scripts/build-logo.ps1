<#
.SYNOPSIS
    Genera el logo de Strata UI (strataui.ico y docs\logo.png) desde su dibujo vectorial.
.EXAMPLE
    powershell -STA -File .\scripts\build-logo.ps1
#>
# Logo de Strata UI: tres capas isométricas con hueco entre ellas. Genera el .ico (16–256 px,
# cada tamaño dibujado desde el vector), un PNG grande y una hoja de vista previa.
param(
    [string]$Ico = (Join-Path $PSScriptRoot '..\src\Strata.Shell\Assets\strataui.ico'),
    [string]$Png = (Join-Path $PSScriptRoot '..\docs\logo.png'),
    [string]$Preview
)
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore
function C($hex) { [System.Windows.Media.ColorConverter]::ConvertFromString($hex) }
function Grad($a, $b) {
  $g = New-Object System.Windows.Media.LinearGradientBrush((C $a), (C $b), (New-Object System.Windows.Point 0,0), (New-Object System.Windows.Point 1,1)); $g.Freeze(); $g }
function Layer($cy) {
  # Rombo de esquinas redondeadas en una caja de 256: ancho 176, alto 0,56 del ancho.
  $w = 124.5; $r = 18
  $geo = New-Object System.Windows.Media.RectangleGeometry((New-Object System.Windows.Rect (-$w/2), (-$w/2), $w, $w), $r, $r)
  $t = New-Object System.Windows.Media.TransformGroup
  $t.Children.Add((New-Object System.Windows.Media.RotateTransform 45))
  $t.Children.Add((New-Object System.Windows.Media.ScaleTransform 1, 0.56))
  $t.Children.Add((New-Object System.Windows.Media.TranslateTransform 128, $cy))
  $geo.Transform = $t
  $geo.GetFlattenedPathGeometry() }
function Cut($geo, $over, $gap) {
  # Quita de una capa lo que tapa la de encima, más un hueco alrededor.
  $pen = New-Object System.Windows.Media.Pen ([System.Windows.Media.Brushes]::Black), (2*$gap)
  $pen.LineJoin = 'Round'
  $hole = [System.Windows.Media.Geometry]::Combine($over, $over.GetWidenedPathGeometry($pen), 'Union', $null)
  [System.Windows.Media.Geometry]::Combine($geo, $hole, 'Exclude', $null) }
function DrawLogo($dc, $gap) {
  # Las capas ocupan ~176×174 de 256: se agrandan hasta casi todo el cuadro.
  $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform 1.36, 1.36, 128, 130))
  $dc.PushTransform((New-Object System.Windows.Media.TranslateTransform 0, -2))
  $gap = $gap / 1.36
  $top = Layer 92; $mid = Layer 130; $bot = Layer 168
  $dc.DrawGeometry((Grad '#6E5ADB' '#3570DE'), $null, (Cut $bot $mid $gap))
  $dc.DrawGeometry((Grad '#8F7BF2' '#4E8EF0'), $null, (Cut $mid $top $gap))
  $dc.DrawGeometry((Grad '#B9A6FF' '#6CB2FF'), $null, $top)
  $dc.Pop(); $dc.Pop() }
function Render($size) {
  $v = New-Object System.Windows.Media.DrawingVisual
  $dc = $v.RenderOpen()
  $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform ($size/256), ($size/256)))
  # En tamaños chicos el hueco crece un poco para no desaparecer.
  DrawLogo $dc ([Math]::Max(7, 256/$size*0.9))
  $dc.Pop(); $dc.Close()
  $bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
  $bmp.Render($v); $bmp.Freeze(); $bmp }
function PngBytes($bmp) {
  $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
  $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
  $ms = New-Object IO.MemoryStream; $enc.Save($ms); $ms.ToArray() }

if ($Ico) {
  $sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
  $images = New-Object 'System.Collections.Generic.List[byte[]]'
  foreach ($s in $sizes) { $images.Add([byte[]](PngBytes (Render $s))) }
  $fs = [IO.File]::Create($Ico); $bw = New-Object IO.BinaryWriter $fs
  $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
  $offset = 6 + 16 * $sizes.Count
  for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $d = $images[$i]
    $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$d.Length); $bw.Write([UInt32]$offset)
    $offset += $d.Length }
  foreach ($d in $images) { $bw.Write($d) }
  $bw.Close() }
if ($Png) { [IO.File]::WriteAllBytes($Png, (PngBytes (Render 512))) }
if ($Preview) {
  $W = 900; $H = 360
  $v = New-Object System.Windows.Media.DrawingVisual; $dc = $v.RenderOpen()
  $dark = New-Object System.Windows.Media.SolidColorBrush (C '#1F1F23'); $light = New-Object System.Windows.Media.SolidColorBrush (C '#F4F4F6')
  $dc.DrawRectangle($dark, $null, (New-Object System.Windows.Rect 0,0,$W,($H/2)))
  $dc.DrawRectangle($light, $null, (New-Object System.Windows.Rect 0,($H/2),$W,($H/2)))
  foreach ($row in 0,1) {
    $x = 20
    foreach ($s in 160, 64, 48, 32, 24, 20, 16) {
      $bmp = Render $s
      $dc.DrawImage($bmp, (New-Object System.Windows.Rect $x, ($row*$H/2 + (180-$s)/2), $s, $s))
      $x += $s + 40 } }
  $dc.Close()
  $sheet = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($W, $H, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
  $sheet.Render($v)
  [IO.File]::WriteAllBytes($Preview, (PngBytes $sheet)) }
"ok"
