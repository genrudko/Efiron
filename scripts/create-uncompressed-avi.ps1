[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [ValidateRange(160, 1920)]
    [int]$Width = 336,

    [ValidateRange(90, 1080)]
    [int]$Height = 144,

    [ValidateRange(1, 60)]
    [int]$FramesPerSecond = 10,

    [ValidateRange(10, 3600)]
    [int]$FrameCount = 300
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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

$stride = (($Width * 3 + 3) -band -4)
$frameSize = $stride * $Height
$frameBytes = [byte[]]::new($frameSize)
$bandHeight = [Math]::Max(12, [int]($Height * 0.15))

$redRow = [byte[]]::new($stride)
$blueRow = [byte[]]::new($stride)
$greenRow = [byte[]]::new($stride)
for ($x = 0; $x -lt $Width; $x++) {
    $pixel = $x * 3

    # BGR byte order for BI_RGB.
    $redRow[$pixel] = 49
    $redRow[$pixel + 1] = 49
    $redRow[$pixel + 2] = 224

    $blueRow[$pixel] = 164
    $blueRow[$pixel + 1] = 101
    $blueRow[$pixel + 2] = 52

    $greenRow[$pixel] = 134
    $greenRow[$pixel + 1] = 184
    $greenRow[$pixel + 2] = 18
}

# Positive bitmap height means bottom-up scanline storage.
for ($storageRow = 0; $storageRow -lt $Height; $storageRow++) {
    $sourceY = $Height - 1 - $storageRow
    [byte[]]$sourceRow = $blueRow
    if ($sourceY -lt $bandHeight) {
        $sourceRow = $redRow
    }
    elseif ($sourceY -ge $Height - $bandHeight) {
        $sourceRow = $greenRow
    }

    [System.Buffer]::BlockCopy(
        $sourceRow,
        0,
        $frameBytes,
        $storageRow * $stride,
        $stride)
}

$file = [System.IO.File]::Create($OutputPath)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $riff = Start-Chunk -Writer $writer -FourCc "RIFF"
    Write-FourCc -Writer $writer -Value "AVI "

    $hdrl = Start-List -Writer $writer -Type "hdrl"
    $avih = Start-Chunk -Writer $writer -FourCc "avih"
    $writer.Write([uint32](1000000 / $FramesPerSecond))
    $writer.Write([uint32]($frameSize * $FramesPerSecond))
    $writer.Write([uint32]0)
    $writer.Write([uint32]0x10)
    $writer.Write([uint32]$FrameCount)
    $writer.Write([uint32]0)
    $writer.Write([uint32]1)
    $writer.Write([uint32]$frameSize)
    $writer.Write([uint32]$Width)
    $writer.Write([uint32]$Height)
    for ($index = 0; $index -lt 4; $index++) {
        $writer.Write([uint32]0)
    }
    End-Chunk -Writer $writer -SizePosition $avih

    $strl = Start-List -Writer $writer -Type "strl"
    $strh = Start-Chunk -Writer $writer -FourCc "strh"
    Write-FourCc -Writer $writer -Value "vids"
    Write-FourCc -Writer $writer -Value "DIB "
    $writer.Write([uint32]0)
    $writer.Write([uint16]0)
    $writer.Write([uint16]0)
    $writer.Write([uint32]0)
    $writer.Write([uint32]1)
    $writer.Write([uint32]$FramesPerSecond)
    $writer.Write([uint32]0)
    $writer.Write([uint32]$FrameCount)
    $writer.Write([uint32]$frameSize)
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
    $writer.Write([uint32]0)
    $writer.Write([uint32]$frameSize)
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
        $chunk = Start-Chunk -Writer $writer -FourCc "00db"
        $writer.Write($frameBytes)
        End-Chunk -Writer $writer -SizePosition $chunk
        $indexEntries.Add([pscustomobject]@{
            Offset = [uint32]($chunkStart - $moviFourCcPosition)
            Size = [uint32]$frameSize
        })
    }
    End-Chunk -Writer $writer -SizePosition $movi

    $idx1 = Start-Chunk -Writer $writer -FourCc "idx1"
    foreach ($entry in $indexEntries) {
        Write-FourCc -Writer $writer -Value "00db"
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
$minimumExpected = [long]($frameSize * $FrameCount)
if ($created.Length -lt $minimumExpected) {
    throw "Uncompressed AVI is unexpectedly small: $($created.Length) bytes."
}

[pscustomobject]@{
    Path = $created.FullName
    Width = $Width
    Height = $Height
    FramesPerSecond = $FramesPerSecond
    FrameCount = $FrameCount
    DurationSeconds = $FrameCount / $FramesPerSecond
    FrameBytes = $frameSize
    FileBytes = $created.Length
} | ConvertTo-Json -Compress
