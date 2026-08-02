param(
    [Parameter(Mandatory = $true)][string]$SourceDirectory,
    [Parameter(Mandatory = $true)][string]$StageDirectory,
    [Parameter(Mandatory = $true)][string]$HeadSha,
    [string]$RunId = "local"
)

$ErrorActionPreference = "Stop"

$source = [IO.Path]::GetFullPath($SourceDirectory)
$stage = [IO.Path]::GetFullPath($StageDirectory)
if (-not (Test-Path $source)) {
    throw "Candidate source directory does not exist: $source"
}

Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item $source $stage -Recurse

$sourceFiles = @(Get-ChildItem $source -Recurse -File)
$sourceBytes = ($sourceFiles | Measure-Object Length -Sum).Sum

# Project-reference propagation duplicates the entire LibVLC tree below
# Efiron.Playback. Runtime lookup uses the root libvlc tree.
$duplicateLibVlc = Join-Path $stage "Efiron.Playback/libvlc"
if (Test-Path $duplicateLibVlc) {
    Remove-Item $duplicateLibVlc -Recurse -Force
}
$playbackOutputDirectory = Join-Path $stage "Efiron.Playback"
if ((Test-Path $playbackOutputDirectory) -and
    @(Get-ChildItem $playbackOutputDirectory -Force).Count -eq 0) {
    Remove-Item $playbackOutputDirectory -Force
}

# Keep only the two supported satellite-resource languages. A folder is
# classified as a locale only when every contained file is *.mui or
# *.resources.dll, so native folders such as mpv-host cannot be misclassified.
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
    "mpv-host/mpv.exe",
    "mpv-host/runtime-manifest.json",
    "mpv-host/NOTICE.txt",
    "libmpv-2.dll",
    "libvlc/win-x64/libvlc.dll",
    "libvlc/win-x64/libvlccore.dll",
    "libvlc/win-x64/plugins",
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
        throw "Clean candidate is missing required entry: $entry"
    }
}

if (Test-Path $duplicateLibVlc) {
    throw "Clean candidate still contains nested duplicate Efiron.Playback/libvlc."
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

$mpvManifestPath = Join-Path $stage "mpv-host/runtime-manifest.json"
$mpvManifest = Get-Content $mpvManifestPath -Raw | ConvertFrom-Json
$mpvExe = Join-Path $stage "mpv-host/mpv.exe"
$mpvHash = (Get-FileHash $mpvExe -Algorithm SHA256).Hash.ToLowerInvariant()
$mpvLength = (Get-Item $mpvExe).Length
if ($mpvHash -ne [string]$mpvManifest.exeSha256 -or
    $mpvLength -ne [int64]$mpvManifest.executableLength) {
    throw "Packaged mpv.exe does not match its pinned runtime manifest."
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
        (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne [string]$entry.sha256) {
        throw "Flyleaf FFmpeg manifest mismatch for $($entry.name)."
    }
}

$efironExeCount = @(Get-ChildItem $stage -Recurse -File -Filter "Efiron.exe").Count
$mpvExeCount = @(Get-ChildItem $stage -Recurse -File -Filter "mpv.exe").Count
$libVlcDllCount = @(Get-ChildItem $stage -Recurse -File -Filter "libvlc.dll").Count
$pdbCount = @(Get-ChildItem $stage -Recurse -File -Filter "*.pdb").Count
$importLibraryCount = @(Get-ChildItem $stage -Recurse -File -Filter "*.lib").Count
$pluginCount = @(Get-ChildItem (Join-Path $stage "libvlc/win-x64/plugins") -Recurse -File).Count
if ($efironExeCount -ne 1 -or $mpvExeCount -ne 1 -or $libVlcDllCount -ne 1) {
    throw "Candidate runtime multiplicity is invalid: Efiron=$efironExeCount mpv=$mpvExeCount LibVLC=$libVlcDllCount."
}
if ($pdbCount -ne 0 -or $importLibraryCount -ne 0) {
    throw "Candidate contains build-only files: PDB=$pdbCount LIB=$importLibraryCount."
}
if ($pluginCount -le 0) {
    throw "Candidate contains no LibVLC fallback plugins."
}

$packageFiles = @(Get-ChildItem $stage -Recurse -File)
$packageBytes = ($packageFiles | Measure-Object Length -Sum).Sum
if ($packageFiles.Count -ge 950) {
    throw "Candidate cleanup regressed: expected fewer than 950 files, got $($packageFiles.Count)."
}
if ($packageBytes -ge 700MB) {
    throw "Candidate cleanup regressed: expected less than 700 MiB unpacked, got $packageBytes bytes."
}

[ordered]@{
    product = "Efiron"
    platform = "Windows 11 x64"
    architecture = "greenfield"
    repair = "Repair K Flyleaf FFmpeg DirectX cadence experiment"
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
    }
    retainedControls = @("Mpv", "MpvHost", "LibVlc")
    packageChecks = [ordered]@{
        sourceFileCount = $sourceFiles.Count
        sourceBytes = $sourceBytes
        packagedFileCount = $packageFiles.Count
        packagedBytes = $packageBytes
        efironExeCount = $efironExeCount
        mpvExeCount = $mpvExeCount
        libVlcRuntimeCount = $libVlcDllCount
        flyleafFfmpegDllCount = $ffmpegDlls.Count
        duplicatePlaybackLibVlcPresent = $false
        pdbCount = $pdbCount
        importLibraryCount = $importLibraryCount
        libVlcPluginCount = $pluginCount
        locales = @("en-US", "ru-RU")
    }
    builtAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
} | ConvertTo-Json -Depth 6 |
    Set-Content (Join-Path $stage "repair-k-candidate-manifest.json") -Encoding utf8

Write-Host "Candidate sanitation: $($sourceFiles.Count) -> $($packageFiles.Count) files; $sourceBytes -> $packageBytes bytes."
