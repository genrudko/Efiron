[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [Parameter(Mandatory = $true)]
    [string]$FixtureDirectory,

    [Parameter(Mandatory = $true)]
    [string]$DiagnosticsDirectory,

    [Parameter(Mandatory = $true)]
    [string]$ArtifactDirectory,

    [Parameter(Mandatory = $true)]
    [string]$HeadSha,

    [ValidateRange(1024, 65535)]
    [int]$Port = 18770
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$exe = Join-Path $OutputDirectory "Efiron.exe"
if (-not (Test-Path $exe)) {
    throw "Efiron.exe not found: $exe"
}

New-Item -ItemType Directory -Path $ArtifactDirectory -Force | Out-Null
$evidencePath = Join-Path $DiagnosticsDirectory "fullscreen-runtime.json"
$previewPath = Join-Path $DiagnosticsDirectory "fullscreen-preview.png"
$errorPath = Join-Path $DiagnosticsDirectory "fullscreen-preview-error.log"
$crashPath = Join-Path $DiagnosticsDirectory "startup-crash.log"
$desktopPath = Join-Path $DiagnosticsDirectory "fullscreen-desktop.png"
$desktopEvidencePath = Join-Path `
    $DiagnosticsDirectory `
    "fullscreen-desktop-runtime.json"
$process = $null
$server = $null

try {
    $python = (Get-Command python -ErrorAction Stop).Source
    $server = Start-Process $python -ArgumentList @(
        "-m",
        "http.server",
        $Port.ToString(),
        "--bind",
        "127.0.0.1",
        "--directory",
        $FixtureDirectory) -WindowStyle Hidden -PassThru
    Start-Sleep -Seconds 2
    if ($server.HasExited) {
        throw "Fullscreen media server exited."
    }

    $process = Start-Process `
        $exe `
        -WorkingDirectory $OutputDirectory `
        -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(100)
    while ([DateTime]::UtcNow -lt $deadline -and
           (-not (Test-Path $evidencePath) -or
            -not (Test-Path $previewPath))) {
        Start-Sleep -Milliseconds 300
        $process.Refresh()
        if ($process.HasExited) {
            $crash = if (Test-Path $crashPath) {
                Get-Content $crashPath -Raw
            }
            else {
                "no crash log"
            }
            throw "Efiron exited before fullscreen evidence. $crash"
        }
    }

    if (Test-Path $errorPath) {
        throw "Fullscreen capture failed: $(Get-Content $errorPath -Raw)"
    }
    if (-not (Test-Path $evidencePath) -or
        -not (Test-Path $previewPath)) {
        throw "Fullscreen evidence is incomplete."
    }

    $evidence = Get-Content $evidencePath -Raw | ConvertFrom-Json
    if ($evidence.PresenterKind -ne "Overlapped") {
        throw "Unexpected native-popup presenter: $($evidence.PresenterKind)"
    }
    if ([double]$evidence.TitleBarRowHeight -ne 0 -or
        [double]$evidence.NavigationColumnWidth -ne 0) {
        throw "Shell chrome remains visible in fullscreen."
    }
    if (-not [bool]$evidence.Surface.IsFullscreen -or
        [double]$evidence.Surface.LiveRootRowSpacing -ne 0 -or
        [double]$evidence.Surface.PlayerWorkspaceRowSpacing -ne 0 -or
        [double]$evidence.Surface.PlayerBorderThickness -ne 0) {
        throw "Fullscreen surface geometry is invalid."
    }
    if ($evidence.Surface.PlaybackState -ne "Playing" -or
        [string]::IsNullOrWhiteSpace(
            [string]$evidence.Surface.PlaybackSource)) {
        throw "Fullscreen fixture was not playing when evidence was recorded."
    }
    if ([string]::IsNullOrWhiteSpace(
            [string]$evidence.Surface.VideoCropGeometry)) {
        throw "Fullscreen video fill crop geometry was not applied."
    }
    if ($evidence.WindowBackground -ne "#FF000000" -or
        $evidence.Surface.LiveRootBackground -ne "#FF000000") {
        throw "Fullscreen root surfaces are not black."
    }
    if ([double]$evidence.TopWhitePixelRatio -gt 0.01 -or
        [double]$evidence.BottomWhitePixelRatio -gt 0.01) {
        throw (
            "White fullscreen edge detected inside XAML surface: " +
            "top=$($evidence.TopWhitePixelRatio), " +
            "bottom=$($evidence.BottomWhitePixelRatio)")
    }

    $cycles = @($evidence.WindowedCycles)
    if ($cycles.Count -ne 3) {
        throw "Expected three fullscreen/windowed cycles, got $($cycles.Count)."
    }
    foreach ($cycle in $cycles) {
        if ($cycle.PresenterKind -ne "Overlapped" -or
            -not [bool]$cycle.NormalStyleRestored -or
            -not [bool]$cycle.NormalExStyleRestored -or
            -not [bool]$cycle.CaptionButtonStylesPresent -or
            -not [bool]$cycle.WindowRegionCleared -or
            -not [bool]$cycle.DwmWindowStyleRenderingRestored -or
            -not [bool]$cycle.ExtendsContentIntoTitleBar -or
            $cycle.TitleBarDragRegionVisibility -ne "Visible" -or
            [double]$cycle.TitleBarRowHeight -le 0) {
            throw (
                "Window chrome cycle $($cycle.Cycle) failed: " +
                ($cycle | ConvertTo-Json -Compress))
        }
    }

    Start-Sleep -Seconds 2
    Add-Type -AssemblyName System.Drawing
    Add-Type -AssemblyName System.Windows.Forms
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = [System.Drawing.Bitmap]::new(
        $bounds.Width,
        $bounds.Height,
        [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen(
            $bounds.Location,
            [System.Drawing.Point]::Empty,
            $bounds.Size)
        $bitmap.Save(
            $desktopPath,
            [System.Drawing.Imaging.ImageFormat]::Png)

        function Measure-NonBlackRatio {
            param(
                [Parameter(Mandatory = $true)]
                [System.Drawing.Bitmap]$Image,

                [Parameter(Mandatory = $true)]
                [int]$YStart,

                [Parameter(Mandatory = $true)]
                [int]$YEnd
            )

            $xStart = [int]($Image.Width * 0.20)
            $xEnd = [int]($Image.Width * 0.80)
            $nonBlack = 0L
            $samples = 0L
            for ($y = $YStart; $y -lt $YEnd; $y += 2) {
                for ($x = $xStart; $x -lt $xEnd; $x += 2) {
                    $pixel = $Image.GetPixel($x, $y)
                    if ($pixel.R -gt 30 -or
                        $pixel.G -gt 30 -or
                        $pixel.B -gt 30) {
                        $nonBlack++
                    }
                    $samples++
                }
            }

            if ($samples -eq 0) {
                return 0d
            }
            return [double]$nonBlack / [double]$samples
        }

        function Measure-WhiteRatio {
            param(
                [Parameter(Mandatory = $true)]
                [System.Drawing.Bitmap]$Image,

                [Parameter(Mandatory = $true)]
                [int]$YStart,

                [Parameter(Mandatory = $true)]
                [int]$YEnd
            )

            $white = 0L
            $samples = 0L
            $boundedStart = [Math]::Max(0, $YStart)
            $boundedEnd = [Math]::Min($Image.Height, $YEnd)
            for ($y = $boundedStart; $y -lt $boundedEnd; $y++) {
                for ($x = 0; $x -lt $Image.Width; $x++) {
                    $pixel = $Image.GetPixel($x, $y)
                    if ($pixel.R -ge 235 -and
                        $pixel.G -ge 235 -and
                        $pixel.B -ge 235) {
                        $white++
                    }
                    $samples++
                }
            }

            if ($samples -eq 0) {
                return 0d
            }
            return [double]$white / [double]$samples
        }

        $topContentRatio = Measure-NonBlackRatio `
            -Image $bitmap `
            -YStart ([int]($bitmap.Height * 0.04)) `
            -YEnd ([int]($bitmap.Height * 0.16))
        $lowerContentRatio = Measure-NonBlackRatio `
            -Image $bitmap `
            -YStart ([int]($bitmap.Height * 0.62)) `
            -YEnd ([int]($bitmap.Height * 0.74))
        if ($topContentRatio -lt 0.70 -or
            $lowerContentRatio -lt 0.70) {
            throw (
                "Decoded video did not fill fullscreen: " +
                "top=$topContentRatio lower=$lowerContentRatio")
        }

        $topDesktopEdgeWhiteRatio = Measure-WhiteRatio `
            -Image $bitmap `
            -YStart 0 `
            -YEnd 1
        $topDesktopInteriorWhiteRatio = Measure-WhiteRatio `
            -Image $bitmap `
            -YStart 2 `
            -YEnd 7
        $bottomDesktopEdgeWhiteRatio = Measure-WhiteRatio `
            -Image $bitmap `
            -YStart ($bitmap.Height - 1) `
            -YEnd $bitmap.Height
        $bottomDesktopInteriorWhiteRatio = Measure-WhiteRatio `
            -Image $bitmap `
            -YStart ($bitmap.Height - 7) `
            -YEnd ($bitmap.Height - 2)

        $topEdgeDelta = $topDesktopEdgeWhiteRatio -
            $topDesktopInteriorWhiteRatio
        $bottomEdgeDelta = $bottomDesktopEdgeWhiteRatio -
            $bottomDesktopInteriorWhiteRatio
        if (($topDesktopEdgeWhiteRatio -gt 0.85 -and
             $topEdgeDelta -gt 0.45) -or
            ($bottomDesktopEdgeWhiteRatio -gt 0.85 -and
             $bottomEdgeDelta -gt 0.45)) {
            throw (
                "Physical desktop edge line detected: " +
                "top=$topDesktopEdgeWhiteRatio " +
                "topInterior=$topDesktopInteriorWhiteRatio " +
                "bottom=$bottomDesktopEdgeWhiteRatio " +
                "bottomInterior=$bottomDesktopInteriorWhiteRatio")
        }

        [ordered]@{
            HeadSha = $HeadSha
            Width = $bitmap.Width
            Height = $bitmap.Height
            TopContentRatio = $topContentRatio
            LowerContentRatio = $lowerContentRatio
            TopDesktopEdgeWhiteRatio = $topDesktopEdgeWhiteRatio
            TopDesktopInteriorWhiteRatio = $topDesktopInteriorWhiteRatio
            BottomDesktopEdgeWhiteRatio = $bottomDesktopEdgeWhiteRatio
            BottomDesktopInteriorWhiteRatio = $bottomDesktopInteriorWhiteRatio
            PlaybackState = [string]$evidence.Surface.PlaybackState
            CropGeometry = [string]$evidence.Surface.VideoCropGeometry
            WindowedCycleCount = $cycles.Count
        } | ConvertTo-Json |
            Set-Content $desktopEvidencePath -Encoding utf8
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }

    $preview = [System.Drawing.Image]::FromFile($previewPath)
    try {
        if ($preview.Width -lt 800 -or
            $preview.Height -lt 500) {
            throw (
                "Fullscreen preview dimensions are too small: " +
                "$($preview.Width)x$($preview.Height).")
        }
    }
    finally {
        $preview.Dispose()
    }

    $artifactFiles = @(
        $evidencePath,
        $previewPath,
        $desktopPath,
        $desktopEvidencePath)
    Copy-Item `
        -Path $artifactFiles `
        -Destination $ArtifactDirectory `
        -Force

    Get-Content $evidencePath -Raw
}
finally {
    Get-ChildItem `
        $DiagnosticsDirectory `
        -Filter "fullscreen-*" `
        -File `
        -ErrorAction SilentlyContinue |
        Copy-Item `
            -Destination $ArtifactDirectory `
            -Force `
            -ErrorAction SilentlyContinue

    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
    }
    if ($server -and -not $server.HasExited) {
        Stop-Process -Id $server.Id -Force
    }
}
