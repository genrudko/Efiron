[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [ValidateRange(320, 4096)]
    [int]$Width = 1680,

    [ValidateRange(180, 2160)]
    [int]$Height = 720,

    [ValidateRange(1, 60)]
    [int]$FramesPerSecond = 10,

    [ValidateRange(10, 3600)]
    [int]$FrameCount = 300
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

function Write-FourCc {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.BinaryWriter]$Writer,

        [Parameter(Mandatory = $true)]
        [ValidateLength(4, 4)]
        [string]$Value
    )

    $Writer.Write([System.Text.Encoding]::ASCII.GetBytes($Value))
}

function Start-Chunk {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.BinaryWriter]$Writer,

        [Parameter(Mandatory = $true)]
        [ValidateLength(4, 4)]
        [string]$FourCc
    )

    Write-FourCc -Writer $Writer -Value $FourCc
    $sizePosition = $Writer.BaseStream.Position
    $Writer.Write([uint32]0)
    return $sizePosition
}

function End-Chunk {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.BinaryWriter]$Writer,

        [Parameter(Mandatory = $true)]
        [long]$SizePosition
    )

    $endPosition = $Writer.BaseStream.Position
    $size = [uint32]($endPosition - $SizePosition - 4)
    $Writer.BaseStream.Position = $SizePosition
    $Writer.Write($size)
    $Writer.BaseStream.Position = $endPosition
    if (($size % 2) -ne 0) {
        $Writer.Write([byte]0)
    }
}

function Start-List {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.BinaryWriter]$Writer,

        [Parameter(Mandatory = $true)]
        [ValidateLength(4, 4)]
        [string]$Type
    )

    $sizePosition = Start-Chunk -Writer $Writer -FourCc "LIST"
    Write-FourCc -Writer $Writer -Value $Type
    return $sizePosition
}

$directory = Split-Path -Parent $OutputPath
if ([string]::IsNullOrWhiteSpace($directory)) {
    $directory = (Get-Location).Path
    $OutputPath = Join-Path $directory $OutputPath
}
New-Item -ItemType Directory -Path $directory -Force | Out-Null

$bitmap = [System.Drawing.Bitmap]::new($Width, $Height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$jpegStream = [System.IO.MemoryStream]::new()
try {
    $graphics.Clear([System.Drawing.Color]::FromArgb(52, 101, 164))
    $red = [System.Drawing.SolidBrush]::new(
        [System.Drawing.Color]::FromArgb(224, 49, 49))
    $green = [System.Drawing.SolidBrush]::new(
        [System.Drawing.Color]::FromArgb(18, 184, 134))
    $white = [System.Drawing.SolidBrush]::new(
        [System.Drawing.Color]::White)
    try {
        $bandHeight = [Math]::Max(40, [int]($Height * 0.15))
        $graphics.FillRectangle($red, 0, 0, $Width, $bandHeight)
        $graphics.FillRectangle(
            $green,
            0,
            $Height - $bandHeight,
            $Width,
            $bandHeight)
        $fontSize = [Math]::Max(18, [single]($Height * 0.067))
        $font = [System.Drawing.Font]::new(
            "Arial",
            $fontSize,
            [System.Drawing.FontStyle]::Bold)
        try {
            $text = "EFIRON FULLSCREEN VIDEO"
            $textSize = $graphics.MeasureString($text, $font)
            $graphics.DrawString(
                $text,
                $font,
                $white,
                [single](($Width - $textSize.Width) / 2),
                [single](($Height - $textSize.Height) / 2))
        }
        finally {
            $font.Dispose()
        }
    }
    finally {
        $red.Dispose()
        $green.Dispose()
        $white.Dispose()
    }

    $bitmap.Save($jpegStream, [System.Drawing.Imaging.ImageFormat]::Jpeg)
}
finally {
    $graphics.Dispose()
    $bitmap.Dispose()
}

$jpeg = $jpegStream.ToArray()
$jpegStream.Dispose()
if ($jpeg.Length -lt 10000) {
    throw "MJPEG frame is unexpectedly small: $($jpeg.Length) bytes."
}

$file = [System.IO.File]::Create($OutputPath)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $riff = Start-Chunk -Writer $writer -FourCc "RIFF"
    Write-FourCc -Writer $writer -Value "AVI "

    $hdrl = Start-List -Writer $writer -Type "hdrl"
    $avih = Start-Chunk -Writer $writer -FourCc "avih"
    $writer.Write([uint32](1000000 / $FramesPerSecond))
    $writer.Write([uint32]($jpeg.Length * $FramesPerSecond))
    $writer.Write([uint32]0)
    $writer.Write([uint32]0x10)
    $writer.Write([uint32]$FrameCount)
    $writer.Write([uint32]0)
    $writer.Write([uint32]1)
    $writer.Write([uint32]$jpeg.Length)
    $writer.Write([uint32]$Width)
    $writer.Write([uint32]$Height)
    for ($index = 0; $index -lt 4; $index++) {
        $writer.Write([uint32]0)
    }
    End-Chunk -Writer $writer -SizePosition $avih

    $strl = Start-List -Writer $writer -Type "strl"
    $strh = Start-Chunk -Writer $writer -FourCc "strh"
    Write-FourCc -Writer $writer -Value "vids"
    Write-FourCc -Writer $writer -Value "MJPG"
    $writer.Write([uint32]0)
    $writer.Write([uint16]0)
    $writer.Write([uint16]0)
    $writer.Write([uint32]0)
    $writer.Write([uint32]1)
    $writer.Write([uint32]$FramesPerSecond)
    $writer.Write([uint32]0)
    $writer.Write([uint32]$FrameCount)
    $writer.Write([uint32]$jpeg.Length)
    $writer.Write([uint32]::MaxValue)
    $writer.Write([uint32]0)
    $writer.Write([int16]0)
    $writer.Write([int16]0)
    $writer.Write([int16]$Width)
    $writer.Write([int16]$Height)
    End-Chunk -Writer $writer -SizePosition $strh

    $strf = Start-Chunk -Writer $writer -FourCc "strf"
    $writer.Write([uint32]40)
    $writer.Write([int32]$Width)
    $writer.Write([int32]$Height)
    $writer.Write([uint16]1)
    $writer.Write([uint16]24)
    $writer.Write([uint32][BitConverter]::ToUInt32(
        [System.Text.Encoding]::ASCII.GetBytes("MJPG"),
        0))
    $writer.Write([uint32]$jpeg.Length)
    $writer.Write([int32]0)
    $writer.Write([int32]0)
    $writer.Write([uint32]0)
    $writer.Write([uint32]0)
    End-Chunk -Writer $writer -SizePosition $strf
    End-Chunk -Writer $writer -SizePosition $strl
    End-Chunk -Writer $writer -SizePosition $hdrl

    $movi = Start-List -Writer $writer -Type "movi"
    $moviFourCcPosition = $movi + 4
    $indexEntries = [System.Collections.Generic.List[object]]::new()
    for ($frame = 0; $frame -lt $FrameCount; $frame++) {
        $chunkStart = $writer.BaseStream.Position
        $chunk = Start-Chunk -Writer $writer -FourCc "00dc"
        $writer.Write($jpeg)
        End-Chunk -Writer $writer -SizePosition $chunk
        $indexEntries.Add([pscustomobject]@{
            Offset = [uint32]($chunkStart - $moviFourCcPosition)
            Size = [uint32]$jpeg.Length
        })
    }
    End-Chunk -Writer $writer -SizePosition $movi

    $idx1 = Start-Chunk -Writer $writer -FourCc "idx1"
    foreach ($entry in $indexEntries) {
        Write-FourCc -Writer $writer -Value "00dc"
        $writer.Write([uint32]0x10)
        $writer.Write([uint32]$entry.Offset)
        $writer.Write([uint32]$entry.Size)
    }
    End-Chunk -Writer $writer -SizePosition $idx1
    End-Chunk -Writer $writer -SizePosition $riff
}
finally {
    $writer.Dispose()
    $file.Dispose()
}

$created = Get-Item $OutputPath
if ($created.Length -lt 1000000) {
    throw "MJPEG AVI is unexpectedly small: $($created.Length) bytes."
}

[pscustomobject]@{
    Path = $created.FullName
    Width = $Width
    Height = $Height
    FramesPerSecond = $FramesPerSecond
    FrameCount = $FrameCount
    DurationSeconds = $FrameCount / $FramesPerSecond
    JpegFrameBytes = $jpeg.Length
    FileBytes = $created.Length
} | ConvertTo-Json -Compress
