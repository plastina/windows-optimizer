<#
.SYNOPSIS
    Renders tools/app-icon/greyhound.xaml into the app icon (a multi-size .ico) and a preview PNG.

.DESCRIPTION
    Needs Windows (it uses WPF to rasterize) and runs in a single-threaded apartment, so start it with:
        powershell -STA -File tools\Build-Icon.ps1
    The .ico holds PNG-compressed images at 16 to 256 px, which every Windows since Vista reads.
    Run it again after editing the drawing and commit the new app.ico.
#>
[CmdletBinding()]
param(
    [string]$Source,
    [string]$Output,
    [string]$PreviewPng
)

$ErrorActionPreference = 'Stop'
# Not parameter defaults: Windows PowerShell 5.1 can leave $PSScriptRoot empty there.
$toolsDir = Split-Path $MyInvocation.MyCommand.Path -Parent
if (-not $Source) { $Source = Join-Path $toolsDir 'app-icon\greyhound.xaml' }
if (-not $Output) { $Output = Join-Path (Split-Path $toolsDir -Parent) 'src\WindowsOptimizer.App\Assets\app.ico' }
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$drawing = [Windows.Markup.XamlReader]::Parse([IO.File]::ReadAllText($Source))["Icon"]
$designSize = 256.0

function Render-Png([int]$size) {
    $visual = New-Object Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $dc.PushTransform((New-Object Windows.Media.ScaleTransform(($size / $designSize), ($size / $designSize))))
    $dc.DrawDrawing($drawing)
    $dc.Pop()
    $dc.Close()

    $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object IO.MemoryStream
    $encoder.Save($stream)
    , $stream.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($size in $sizes) { , (Render-Png $size) }

New-Item -ItemType Directory -Path (Split-Path $Output -Parent) -Force | Out-Null
$file = [IO.File]::Create($Output)
$writer = New-Object IO.BinaryWriter($file)
try {
    # ICONDIR, then one 16-byte ICONDIRENTRY per image, then the PNG data itself.
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }   # 0 means 256
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($image in $images) { $writer.Write($image) }
}
finally { $writer.Dispose() }
Write-Host "Wrote $Output ($($sizes -join ', ') px)"

if ($PreviewPng) {
    [IO.File]::WriteAllBytes($PreviewPng, (Render-Png 512))
    Write-Host "Wrote $PreviewPng"
}
