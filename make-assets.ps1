<#
    Regenerates the shipped welcome-screen artwork in assets\assets\ from the
    full-resolution exports in design\.

    The Figma exports are 3x the design frame, which is four times more detail
    than the 560px welcome screen can show even at 300% display scaling, and
    every byte here lands in the installer. This scales them once, offline, so
    the build itself stays a straight copy.

    Run after re-exporting anything from Figma:
        powershell -ExecutionPolicy Bypass -File make-assets.ps1
#>

[CmdletBinding()]
param(
    # 0.4 keeps the cut-outs at 3x the 560px canvas - pixel-exact to 300% DPI.
    [double] $Scale = 0.4
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$from = Join-Path $root 'design\cutouts'
$to   = Join-Path $root 'assets\assets\collage'

if (-not (Test-Path $from)) { throw "Missing $from" }
New-Item -ItemType Directory -Force -Path $to | Out-Null
Get-ChildItem $to -Filter *.png -ErrorAction SilentlyContinue | Remove-Item -Force

$before = 0; $after = 0

foreach ($file in Get-ChildItem $from -Filter *.png) {
    $src = New-Object System.Drawing.Bitmap $file.FullName
    $w = [int][math]::Round($src.Width * $Scale)
    $h = [int][math]::Round($src.Height * $Scale)
    $dst = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

    $g = [System.Drawing.Graphics]::FromImage($dst)
    # SourceCopy writes the scaled alpha through instead of blending it against
    # the blank bitmap, and TileFlipXY stops the sampler reading transparent
    # black from beyond the edges - both show up as grey fringing otherwise.
    $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    $attr = New-Object System.Drawing.Imaging.ImageAttributes
    $attr.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
    $rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
    $g.DrawImage($src, $rect, 0, 0, $src.Width, $src.Height, [System.Drawing.GraphicsUnit]::Pixel, $attr)
    $g.Dispose(); $attr.Dispose()

    $out = Join-Path $to $file.Name
    $dst.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    $dst.Dispose(); $src.Dispose()

    $before += $file.Length
    $after  += (Get-Item $out).Length
}

Write-Host ("Wrote {0} cut-outs to assets\assets\collage\ ({1:N1} MB -> {2:N1} MB)" -f `
    (Get-ChildItem $to -Filter *.png).Count, ($before / 1MB), ($after / 1MB)) -ForegroundColor Green
