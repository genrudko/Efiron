param(
    [Parameter(Mandatory = $true)][string]$SourceDirectory,
    [Parameter(Mandatory = $true)][string]$StageDirectory,
    [Parameter(Mandatory = $true)][string]$HeadSha,
    [string]$RunId = "local"
)

$ErrorActionPreference = "Stop"

function Get-Sha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $hash = $algorithm.ComputeHash($stream)
        return ([BitConverter]::ToString($hash) -replace "-", "").ToLowerInvariant()
    }
    finally {
        $algorithm.Dispose()
        $stream.Dispose()
    }
}

$source = [IO.Path]::GetFullPath($SourceDirectory)
$stage = [IO.Path]::GetFullPath($StageDirectory)
if (-not (Test-Path $source)) {
    throw "Candidate source directory does not exist: $source"
}

Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item $source $stage -Recurse

$sourceFiles = @(Get-ChildItem $source -Recurse -File)
$sourceBytes = ($sourceFiles | Measure-Object Length -Sum).Sum

# Repair K is a single-engine physical experiment. The rejected native mpv,
# mpv-host and LibVLC payloads are intentionally absent from the candidate;
# their source code stays in the branch only as historical/control evidence.
Get-ChildItem $stage -Recurse -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ieq "libvlc" } |
    Sort-Object FullName -Descending |
    Remove-Item -Recurse -Force
Remove-Item (Join-Path $stage "mpv-host") -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem $stage -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -ieq "mpv.exe" -or
        $_.Name -like "libmpv*.dll" -or
        $_.Name -ieq "libmpv-runtime-manifest.json" -or
        $_.Name -ieq "libvlc.dll" -or
        $_.Name -ieq "libvlccore.dll"
    } |
    Remove-Item -Force

$playbackOutputDirectory = Join-Path $stage "Efiron.Playback"
if ((Test-Path $playbackOutputDirectory) -and
    @(Get-ChildItem $playbackOutputDirectory -Force).Count -eq 0) {
    Remove-Item $playbackOutputDirectory -Force
}

# Keep only the two supported satellite-resource languages. A directory is
# classified as a locale only when every contained file is *.mui or
# *.resources.dll.
$allowedLocales = @("en-us", "ru-ru")
Get-ChildItem $stage -Directory -ErrorAction SilentlyContinue |
    ForEach-Object {
        $directory = $_
        $files = @(Get-ChildItem $directory.FullName -Recurse -File -ErrorAction SilentlyContinue)
        if ($files.Count -eq 0) {
            return
        }

        $localizedFiles = @($files | Where-Object {
            $_.Extension -ieq ".mui" -or
            $_.Name.EndsWith(
                ".resources.dll",
                [StringComparison]::OrdinalIgnoreCase)
        })
        if ($directory.Name.ToLowerInvariant() -notin $allowedLocales -and
            $localizedFiles.Count -eq $files.Count) {
            Remove-Item $directory.FullName -Recurse -Force
        }
    }

Get-ChildItem $stage -Recurse -File -Filter "*.pdb" -ErrorAction SilentlyContinue |
    Remove-Item -Force
Get-ChildItem $stage -Recurse -File -Filter "*.lib" -ErrorAction SilentlyContinue |
    Remove-Item -Force

$requiredEntries = @(
    "Efiron.exe",
    "FFmpeg/avcodec-62.dll",
    "FFmpeg/avdevice-62.dll",
    "FFmpeg/avfilter-11.dll",
    "FFmpeg/avformat-62.dll",
    "FFmpeg/avutil-60.dll",
    "FFmpeg/swresample-6.dll",
    "FFmpeg/swscale-9.dll",
    "FFmpeg/runtime-manifest.json",
    "FFmpeg/NOTICE.txt",
    "FlyleafLib.dll",
    "FlyleafLib.Controls.WinUI.dll",
    "Flyleaf.FFmpeg.Bindings.dll",
    "en-US",
    "ru-RU"
)
foreach ($entry in $requiredEntries) {
    if (-not (Test-Path (Join-Path $stage $entry))) {
        throw "Clean Flyleaf candidate is missing required entry: $entry"
    }
}

$remainingLocaleDirectories = @(Get-ChildItem $stage -Directory |
    Where-Object {
        $directory = $_
        $files = @(Get-ChildItem $directory.FullName -Recurse -File -ErrorAction SilentlyContinue)
        if ($files.Count -eq 0) { return $false }
        $localizedFiles = @($files | Where-Object {
            $_.Extension -ieq ".mui" -or
            $_.Name.EndsWith(
                ".resources.dll",
                [StringComparison]::OrdinalIgnoreCase)
        })
        return $localizedFiles.Count -eq $files.Count
    })
$unexpectedLocales = @($remainingLocaleDirectories |
    Where-Object { $_.Name.ToLowerInvariant() -notin $allowedLocales })
if ($unexpectedLocales.Count -gt 0) {
    throw "Unexpected packaged locale directories: $($unexpectedLocales.Name -join ', ')."
}

$ffmpegDirectory = Join-Path $stage "FFmpeg"
$ffmpegManifestPath = Join-Path $ffmpegDirectory "runtime-manifest.json"
$ffmpegManifest = Get-Content $ffmpegManifestPath -Raw | ConvertFrom-Json
if ($ffmpegManifest.sourceRepository -ne "SuRGeoNix/Flyleaf" -or
    $ffmpegManifest.sourceCommit -ne "c27eec7244278cfb1f4141394f5f030693aca62c" -or
    $ffmpegManifest.ffmpegVersion -ne "8.0") {
    throw "Packaged Flyleaf FFmpeg manifest is not pinned to the accepted upstream runtime."
}
$ffmpegDlls = @(Get-ChildItem $ffmpegDirectory -File -Filter "*.dll" | Sort-Object Name)
if ($ffmpegDlls.Count -ne 7) {
    throw "Expected exactly seven packaged Flyleaf FFmpeg DLLs, got $($ffmpegDlls.Count)."
}
foreach ($entry in @($ffmpegManifest.files)) {
    $path = Join-Path $ffmpegDirectory ([string]$entry.name)
    if (-not (Test-Path $path) -or
        (Get-Item $path).Length -ne [int64]$entry.length -or
        (Get-Sha256 $path) -ne [string]$entry.sha256) {
        throw "Flyleaf FFmpeg manifest mismatch for $($entry.name)."
    }
}

$efironExeCount = @(Get-ChildItem $stage -Recurse -File -Filter "Efiron.exe").Count
$mpvExeCount = @(Get-ChildItem $stage -Recurse -File -Filter "mpv.exe").Count
$libMpvCount = @(Get-ChildItem $stage -Recurse -File -Filter "libmpv*.dll").Count
$libVlcCount = @(Get-ChildItem $stage -Recurse -File -Filter "libvlc.dll").Count
$libVlcCoreCount = @(Get-ChildItem $stage -Recurse -File -Filter "libvlccore.dll").Count
$pdbCount = @(Get-ChildItem $stage -Recurse -File -Filter "*.pdb").Count
$importLibraryCount = @(Get-ChildItem $stage -Recurse -File -Filter "*.lib").Count
if ($efironExeCount -ne 1) {
    throw "Expected exactly one Efiron.exe, got $efironExeCount."
}
if ($mpvExeCount -ne 0 -or $libMpvCount -ne 0 -or
    $libVlcCount -ne 0 -or $libVlcCoreCount -ne 0) {
    throw "Rejected native engines remain in candidate: mpv=$mpvExeCount libmpv=$libMpvCount libvlc=$libVlcCount core=$libVlcCoreCount."
}
if ($pdbCount -ne 0 -or $importLibraryCount -ne 0) {
    throw "Candidate contains build-only files: PDB=$pdbCount LIB=$importLibraryCount."
}

$packageFiles = @(Get-ChildItem $stage -Recurse -File)
$packageBytes = ($packageFiles | Measure-Object Length -Sum).Sum
if ($packageFiles.Count -ge 600) {
    throw "Candidate cleanup regressed: expected fewer than 600 files, got $($packageFiles.Count)."
}
if ($packageBytes -ge 550MB) {
    throw "Candidate cleanup regressed: expected less than 550 MiB unpacked, got $packageBytes bytes."
}

[ordered]@{
    product = "Efiron"
    platform = "Windows 11 x64"
    architecture = "greenfield"
    repair = "Repair K single-engine Flyleaf FFmpeg DirectX cadence experiment"
    status = "physical playback validation candidate"
    headSha = $HeadSha
    runId = $RunId
    playbackExperiment = [ordered]@{
        backend = "Flyleaf"
        presentation = "in-process D3D11 DirectComposition swap chain with VSync"
        flyleafVersion = "3.10.4"
        ffmpegVersion = [string]$ffmpegManifest.ffmpegVersion
        ffmpegSourceCommit = [string]$ffmpegManifest.sourceCommit
        ffmpegDllCount = $ffmpegDlls.Count
        exposedEngineCount = 1
    }
    removedNativeControls = @("Mpv", "MpvHost", "LibVlc")
    packageChecks = [ordered]@{
        sourceFileCount = $sourceFiles.Count
        sourceBytes = $sourceBytes
        packagedFileCount = $packageFiles.Count
        packagedBytes = $packageBytes
        efironExeCount = $efironExeCount
        mpvExeCount = $mpvExeCount
        libMpvRuntimeCount = $libMpvCount
        libVlcRuntimeCount = $libVlcCount
        flyleafFfmpegDllCount = $ffmpegDlls.Count
        pdbCount = $pdbCount
        importLibraryCount = $importLibraryCount
        locales = @("en-US", "ru-RU")
    }
    builtAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
} | ConvertTo-Json -Depth 6 |
    Set-Content (Join-Path $stage "repair-k-candidate-manifest.json") -Encoding utf8

Write-Host "Candidate sanitation: $($sourceFiles.Count) -> $($packageFiles.Count) files; $sourceBytes -> $packageBytes bytes."
