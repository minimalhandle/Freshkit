# Generates src\app.ico (Freshkit logo) at 16-256 px using GDI+.
param([string]$Out = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\app.ico'))
Add-Type -AssemblyName System.Drawing
$sizes = 256,64,48,40,32,24,20,16
$pngs = foreach ($s in $sizes) {
    $bmp = New-Object Drawing.Bitmap $s, $s
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $r = [single]($s * 0.22); $d = $r * 2; $m = [single]($s * 0.03); $w = $s - 2 * $m
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc($m, $m, $d, $d, 180, 90); $path.AddArc($m + $w - $d, $m, $d, $d, 270, 90)
    $path.AddArc($m + $w - $d, $m + $w - $d, $d, $d, 0, 90); $path.AddArc($m, $m + $w - $d, $d, $d, 90, 90); $path.CloseFigure()
    $brush = New-Object Drawing.Drawing2D.LinearGradientBrush (New-Object Drawing.PointF 0,0), (New-Object Drawing.PointF $s,$s), ([Drawing.Color]::FromArgb(255,45,212,191)), ([Drawing.Color]::FromArgb(255,14,165,233))
    $g.FillPath($brush, $path)
    $pen = New-Object Drawing.Pen ([Drawing.Color]::White), ([single]([Math]::Max(1.6, $s * 0.085)))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $cx = $s / 2
    $g.DrawLine($pen, $cx, $s * 0.24, $cx, $s * 0.58)                                          # arrow shaft
    $g.DrawLines($pen, [Drawing.PointF[]]@((New-Object Drawing.PointF ($s*0.34),($s*0.44)), (New-Object Drawing.PointF $cx,($s*0.60)), (New-Object Drawing.PointF ($s*0.66),($s*0.44))))
    $g.DrawLines($pen, [Drawing.PointF[]]@((New-Object Drawing.PointF ($s*0.26),($s*0.64)), (New-Object Drawing.PointF ($s*0.26),($s*0.75)), (New-Object Drawing.PointF ($s*0.74),($s*0.75)), (New-Object Drawing.PointF ($s*0.74),($s*0.64))))  # tray
    $g.Dispose()
    $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    ,$ms.ToArray()
}
$fs = [IO.File]::Create($Out); $bw = New-Object IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $b = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$b); $bw.Write([byte]$b); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Dispose()
Get-Item $Out
