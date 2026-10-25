# Rebuild Assets/icon.ico from the same five-bar vector mark as Assets/icon.svg and the app header.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$iconRoot = Split-Path $PSScriptRoot
$iconDestination = Join-Path $iconRoot 'src/TimingAnalyzer.App/Assets/icon.ico'
$logoDestination = Join-Path $iconRoot 'src/TimingAnalyzer.App/Assets/TempoMap.png'
$iconSizes = @(16, 24, 32, 48, 64, 128, 256)
$iconImages = @()
foreach ($iconSize in $iconSizes) {
    $iconBitmap = New-Object Drawing.Bitmap($iconSize, $iconSize)
    $iconGraphics = [Drawing.Graphics]::FromImage($iconBitmap)
    $iconGraphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $iconGraphics.ScaleTransform($iconSize / 256.0, $iconSize / 256.0)
    $iconPath = New-Object Drawing.Drawing2D.GraphicsPath
    $iconPath.AddArc(0, 0, 112, 112, 180, 90); $iconPath.AddArc(144, 0, 112, 112, 270, 90)
    $iconPath.AddArc(144, 144, 112, 112, 0, 90); $iconPath.AddArc(0, 144, 112, 112, 90, 90); $iconPath.CloseFigure()
    $iconBrush = New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml('#111d29'))
    $iconGraphics.FillPath($iconBrush, $iconPath)
    $iconPen = New-Object Drawing.Pen([Drawing.ColorTranslator]::FromHtml('#54d6bd'), 18)
    $iconPen.StartCap = [Drawing.Drawing2D.LineCap]::Round; $iconPen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    foreach ($iconLine in @(@(44,100,156), @(86,64,192), @(128,86,170), @(170,44,212), @(212,100,156))) {
        $iconGraphics.DrawLine($iconPen, $iconLine[0], $iconLine[1], $iconLine[0], $iconLine[2])
    }
    $iconStream = New-Object IO.MemoryStream
    $iconBitmap.Save($iconStream, [Drawing.Imaging.ImageFormat]::Png)
    if ($iconSize -eq 256) { [IO.File]::WriteAllBytes($logoDestination, $iconStream.ToArray()) }
    $iconImages += ,$iconStream.ToArray()
    $iconStream.Dispose(); $iconPen.Dispose(); $iconBrush.Dispose(); $iconPath.Dispose(); $iconGraphics.Dispose(); $iconBitmap.Dispose()
}
$iconFile = [IO.File]::Create($iconDestination)
$iconWriter = New-Object IO.BinaryWriter($iconFile)
try {
    $iconWriter.Write([uint16]0); $iconWriter.Write([uint16]1); $iconWriter.Write([uint16]$iconSizes.Count)
    $iconOffset = 6 + 16 * $iconSizes.Count
    for ($iconIndex = 0; $iconIndex -lt $iconSizes.Count; $iconIndex++) {
        $iconDimension = if ($iconSizes[$iconIndex] -eq 256) { 0 } else { $iconSizes[$iconIndex] }
        $iconWriter.Write([byte]$iconDimension); $iconWriter.Write([byte]$iconDimension)
        $iconWriter.Write([uint16]0); $iconWriter.Write([uint16]1); $iconWriter.Write([uint16]32)
        $iconWriter.Write([uint32]$iconImages[$iconIndex].Length); $iconWriter.Write([uint32]$iconOffset)
        $iconOffset += $iconImages[$iconIndex].Length
    }
    foreach ($iconImage in $iconImages) { $iconWriter.Write([byte[]]$iconImage) }
}
finally { $iconWriter.Dispose(); $iconFile.Dispose() }
