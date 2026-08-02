param(
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

# Flyleaf's own reduced FFmpeg 8.0 build. The upstream commit records that
# these binaries include the Flyleaf HLS/thread-name patches and remove
# encoders/components which the player does not use.
$sourceRepository = "SuRGeoNix/Flyleaf"
$sourceCommit = "c27eec7244278cfb1f4141394f5f030693aca62c"
$ffmpegVersion = "8.0"
$requiredDlls = @(
    "avcodec-62.dll",
    "avdevice-62.dll",
    "avfilter-11.dll",
    "avformat-62.dll",
    "avutil-60.dll",
    "swresample-6.dll",
    "swscale-9.dll"
)

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repositoryRoot "artifacts/flyleaf-ffmpeg/win-x64"
}
else {
    [IO.Path]::GetFullPath($OutputDirectory)
}
$manifestPath = Join-Path $runtimeRoot "runtime-manifest.json"
$noticePath = Join-Path $runtimeRoot "NOTICE.txt"

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

function Test-PortableExecutable([string]$Path) {
    if (-not (Test-Path $Path) -or (Get-Item $Path).Length -lt 65536) {
        return $false
    }

    $stream = [IO.File]::OpenRead($Path)
    try {
        return $stream.ReadByte() -eq 0x4d -and $stream.ReadByte() -eq 0x5a
    }
    finally {
        $stream.Dispose()
    }
}

function Test-PreparedRuntime {
    if (-not (Test-Path $manifestPath) -or -not (Test-Path $noticePath)) {
        return $false
    }

    try {
        $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
        if ($manifest.sourceRepository -ne $sourceRepository -or
            $manifest.sourceCommit -ne $sourceCommit -or
            $manifest.ffmpegVersion -ne $ffmpegVersion) {
            return $false
        }

        $manifestFiles = @($manifest.files)
        if ($manifestFiles.Count -ne $requiredDlls.Count) {
            return $false
        }

        foreach ($name in $requiredDlls) {
            $path = Join-Path $runtimeRoot $name
            $entry = $manifestFiles | Where-Object name -eq $name | Select-Object -First 1
            if (-not $entry -or
                -not (Test-PortableExecutable $path) -or
                [int64]$entry.length -ne (Get-Item $path).Length -or
                [string]$entry.sha256 -ne (Get-Sha256 $path)) {
                return $false
            }
        }

        $unexpectedDlls = @(Get-ChildItem $runtimeRoot -File -Filter "*.dll" |
            Where-Object { $_.Name -notin $requiredDlls })
        return $unexpectedDlls.Count -eq 0
    }
    catch {
        return $false
    }
}

New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
if (Test-PreparedRuntime) {
    Write-Host "Pinned Flyleaf FFmpeg runtime already prepared: $runtimeRoot"
    exit 0
}

Get-ChildItem $runtimeRoot -File -ErrorAction SilentlyContinue |
    Remove-Item -Force

$tempRoot = Join-Path $runtimeRoot ".download"
Remove-Item $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

$downloadSpecs = @($requiredDlls | ForEach-Object {
    [pscustomobject]@{
        Name = $_
        Url = "https://raw.githubusercontent.com/$sourceRepository/$sourceCommit/FFmpeg/$_"
        TemporaryPath = Join-Path $tempRoot $_
    }
})
$downloadJobs = @()

try {
    foreach ($spec in $downloadSpecs) {
        Write-Host "Starting pinned Flyleaf FFmpeg download $($spec.Name)..."
        $downloadJobs += Start-Job -ScriptBlock {
            param([string]$Url, [string]$Path)
            $client = New-Object System.Net.WebClient
            try {
                $client.DownloadFile($Url, $Path)
            }
            finally {
                $client.Dispose()
            }
        } -ArgumentList $spec.Url,$spec.TemporaryPath
    }

    Wait-Job $downloadJobs | Out-Null
    $failedJobs = @($downloadJobs | Where-Object State -ne "Completed")
    if ($failedJobs.Count -gt 0) {
        $details = @($failedJobs | ForEach-Object {
            "$($_.State): $($_.ChildJobs[0].JobStateInfo.Reason)"
        }) -join "; "
        throw "One or more Flyleaf FFmpeg downloads failed: $details"
    }
    foreach ($job in $downloadJobs) {
        Receive-Job $job -ErrorAction Stop | Out-Null
    }

    $inventory = @()
    foreach ($spec in $downloadSpecs) {
        if (-not (Test-PortableExecutable $spec.TemporaryPath)) {
            throw "Downloaded Flyleaf FFmpeg component is not a valid PE DLL: $($spec.Name)"
        }

        $destination = Join-Path $runtimeRoot $spec.Name
        Move-Item $spec.TemporaryPath $destination -Force
        $item = Get-Item $destination
        $inventory += [ordered]@{
            name = $spec.Name
            length = $item.Length
            sha256 = Get-Sha256 $destination
            sourceUrl = $spec.Url
        }
    }

    [ordered]@{
        sourceRepository = $sourceRepository
        sourceCommit = $sourceCommit
        ffmpegVersion = $ffmpegVersion
        architecture = "win-x64"
        provenance = "Flyleaf reduced FFmpeg build with HLS/thread-name patches"
        files = $inventory
        preparedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    } | ConvertTo-Json -Depth 5 |
        Set-Content $manifestPath -Encoding utf8

    @"
Efiron packages the reduced FFmpeg 8.0 runtime maintained by Flyleaf for the
experimental Flyleaf DirectX playback backend.

Source repository: https://github.com/$sourceRepository
Pinned source commit: $sourceCommit
Upstream runtime directory: FFmpeg/
Architecture: Windows x64

The pinned upstream commit states that this FFmpeg build includes Flyleaf's
HLS and thread-name patches and removes encoders/components not used by
FlyleafLib. Individual file lengths and SHA-256 hashes are recorded in
runtime-manifest.json after retrieval from immutable commit-addressed URLs.

FlyleafLib is licensed LGPL-3.0-or-later. FFmpeg component licensing depends
on the enabled build configuration and included libraries; preserve this
notice and the manifest with every engineering candidate.
"@ | Set-Content $noticePath -Encoding utf8
}
finally {
    if ($downloadJobs.Count -gt 0) {
        $downloadJobs | Remove-Job -Force -ErrorAction SilentlyContinue
    }
    Remove-Item $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not (Test-PreparedRuntime)) {
    throw "Prepared Flyleaf FFmpeg runtime failed its final integrity check."
}

$totalBytes = (Get-ChildItem $runtimeRoot -File -Filter "*.dll" |
    Measure-Object Length -Sum).Sum
Write-Host "Prepared $($requiredDlls.Count) Flyleaf FFmpeg DLLs ($totalBytes bytes)."
