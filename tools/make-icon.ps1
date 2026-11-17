# Packages the selected Freshkit PNG into a multi-size Windows icon. No redraw or recolouring.
param([string]$Out = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\app.ico'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$sourcePath=Join-Path (Split-Path -Parent $PSScriptRoot) 'src\freshkit-logo.png'
$source=[Drawing.Image]::FromFile($sourcePath)
$sizes=256,64,48,40,32,24,20,16
try{
    $pngs=foreach($size in $sizes){
        $bitmap=[Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics=[Drawing.Graphics]::FromImage($bitmap)
        $memory=[IO.MemoryStream]::new()
        try{
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($source,[Drawing.Rectangle]::new(0,0,$size,$size))
            $bitmap.Save($memory,[Drawing.Imaging.ImageFormat]::Png)
            ,$memory.ToArray()
        }finally{$memory.Dispose();$graphics.Dispose();$bitmap.Dispose()}
    }
}finally{$source.Dispose()}
$writer=[IO.BinaryWriter]::new([IO.File]::Create($Out))
try{
    $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$sizes.Count)
    $offset=6+16*$sizes.Count
    for($i=0;$i -lt $sizes.Count;$i++){
        $dimension=if($sizes[$i] -eq 256){0}else{$sizes[$i]}
        $writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0)
        $writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$pngs[$i].Length);$writer.Write([uint32]$offset)
        $offset+=$pngs[$i].Length
    }
    foreach($png in $pngs){$writer.Write($png)}
}finally{$writer.Dispose()}
Get-Item -LiteralPath $Out