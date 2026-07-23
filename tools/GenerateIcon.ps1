param([string]$OutputPath = (Join-Path $PSScriptRoot '..\Assets\Dust.ico'))

Add-Type -AssemblyName System.Drawing
$assetDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $assetDirectory | Out-Null

$bitmap = New-Object System.Drawing.Bitmap 32, 32, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.Clear([System.Drawing.Color]::Transparent)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None

$soot = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(14, 21, 21))
$reagent = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(119, 197, 152))

$outer = [System.Drawing.Point[]]@(
    (New-Object System.Drawing.Point 10,2), (New-Object System.Drawing.Point 22,2),
    (New-Object System.Drawing.Point 29,9), (New-Object System.Drawing.Point 29,22),
    (New-Object System.Drawing.Point 22,29), (New-Object System.Drawing.Point 9,29),
    (New-Object System.Drawing.Point 2,22), (New-Object System.Drawing.Point 2,10)
)
$inner = [System.Drawing.Point[]]@(
    (New-Object System.Drawing.Point 11,7), (New-Object System.Drawing.Point 21,7),
    (New-Object System.Drawing.Point 25,11), (New-Object System.Drawing.Point 25,21),
    (New-Object System.Drawing.Point 21,25), (New-Object System.Drawing.Point 11,25),
    (New-Object System.Drawing.Point 7,21), (New-Object System.Drawing.Point 7,11)
)
$graphics.FillPolygon($soot, $outer)
$graphics.FillPolygon($reagent, $inner)

$pngStream = New-Object System.IO.MemoryStream
$bitmap.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
$pngBytes = $pngStream.ToArray()
$fileStream = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::Create)
$writer = New-Object System.IO.BinaryWriter $fileStream
try {
    $writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]1)
    $writer.Write([Byte]32); $writer.Write([Byte]32); $writer.Write([Byte]0); $writer.Write([Byte]0)
    $writer.Write([UInt16]1); $writer.Write([UInt16]32)
    $writer.Write([UInt32]$pngBytes.Length); $writer.Write([UInt32]22)
    $writer.Write($pngBytes)
}
finally {
    $writer.Dispose(); $fileStream.Dispose(); $pngStream.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
    $soot.Dispose(); $reagent.Dispose()
}
