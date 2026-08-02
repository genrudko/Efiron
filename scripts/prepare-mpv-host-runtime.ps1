param(
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$releaseTag = "20260610"
$buildId = "20260610-git-304426c"
$archiveName = "mpv-x86_64-$buildId.7z"
$archiveUrl = "https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/$releaseTag/$archiveName"
$archiveLength = 32691385L
$archiveSha256 = "facac536baa73c7b925771af5e39a3c9cb16b8d75b59a6e9800de89799dffca7"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repositoryRoot "artifacts/mpv-host/win-x64"
}
else {
    [IO.Path]::GetFullPath($OutputDirectory)
}
$cacheRoot = Join-Path $repositoryRoot "artifacts/mpv-host/cache"
$archivePath = Join-Path $cacheRoot $archiveName
$exePath = Join-Path $runtimeRoot "mpv.exe"
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

function Resolve-SevenZip {
    foreach ($commandName in @("7z", "7z.exe")) {
        $command = Get-Command $commandName -ErrorAction SilentlyContinue
        if ($command) {
            return $command.Source
        }
    }

    $standardPath = Join-Path $env:ProgramFiles "7-Zip/7z.exe"
    if (Test-Path $standardPath) {
        return $standardPath
    }

    throw "7-Zip is required to prepare the pinned mpv host runtime."
}

function Test-PinnedArchive([string]$Path) {
    return (Test-Path $Path) -and
        (Get-Item $Path).Length -eq $archiveLength -and
        (Get-Sha256 $Path) -eq $archiveSha256
}

New-Item -ItemType Directory -Path $runtimeRoot,$cacheRoot -Force | Out-Null

$manifestValid = $false
if ((Test-Path $exePath) -and (Test-Path $manifestPath)) {
    try {
        $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
        $manifestValid =
            $manifest.releaseTag -eq $releaseTag -and
            $manifest.buildId -eq $buildId -and
            $manifest.archiveName -eq $archiveName -and
            [int64]$manifest.archiveLength -eq $archiveLength -and
            $manifest.archiveSha256 -eq $archiveSha256 -and
            $manifest.exeSha256 -eq (Get-Sha256 $exePath) -and
            (Get-Item $exePath).Length -gt 20MB
    }
    catch {
        $manifestValid = $false
    }
}

if ($manifestValid) {
    Write-Host "Pinned mpv host runtime already prepared: $exePath"
    exit 0
}

if (-not (Test-PinnedArchive $archivePath)) {
    Remove-Item $archivePath -Force -ErrorAction SilentlyContinue
    Write-Host "Downloading pinned mpv host runtime $buildId..."
    Invoke-WebRequest `
        -Uri $archiveUrl `
        -OutFile $archivePath `
        -UseBasicParsing
    if (-not (Test-PinnedArchive $archivePath)) {
        $actualLength = if (Test-Path $archivePath) {
            (Get-Item $archivePath).Length
        }
        else {
            0
        }
        $actualSha = if (Test-Path $archivePath) {
            Get-Sha256 $archivePath
        }
        else {
            "missing"
        }
        Remove-Item $archivePath -Force -ErrorAction SilentlyContinue
        throw (
            "mpv host archive verification failed. " +
            "Expected length/SHA $archiveLength/$archiveSha256, " +
            "got $actualLength/$actualSha.")
    }
}

$extractRoot = Join-Path $cacheRoot "extract-$buildId"
Remove-Item $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null
$sevenZip = Resolve-SevenZip
& $sevenZip x $archivePath "-o$extractRoot" -y | Out-Host

$sourceExe = Get-ChildItem $extractRoot -Recurse -File -Filter "mpv.exe" |
    Select-Object -First 1
if (-not $sourceExe) {
    throw "Pinned mpv archive contains no mpv.exe."
}
if ($sourceExe.Length -le 20MB) {
    throw "Pinned mpv.exe is unexpectedly small: $($sourceExe.Length) bytes."
}

Copy-Item $sourceExe.FullName $exePath -Force
Get-ChildItem $extractRoot -Recurse -File |
    Where-Object { $_.Name -match '^(LICENSE|COPYING|Copyright)' } |
    ForEach-Object {
        Copy-Item $_.FullName (Join-Path $runtimeRoot $_.Name) -Force
    }

$exeSha256 = Get-Sha256 $exePath
$versionLine = (& $exePath --no-config --version | Select-Object -First 1).Trim()
@{
    source = "shinchiro/mpv-winbuild-cmake"
    releaseTag = $releaseTag
    buildId = $buildId
    archiveName = $archiveName
    archiveLength = $archiveLength
    archiveSha256 = $archiveSha256
    downloadUrl = $archiveUrl
    executableFileName = "mpv.exe"
    executableLength = (Get-Item $exePath).Length
    exeSha256 = $exeSha256
    version = $versionLine
    preparedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
} | ConvertTo-Json -Depth 3 |
    Set-Content $manifestPath -Encoding utf8

@"
Efiron packages an unmodified pinned mpv Windows build for the experimental
out-of-process playback host.

Source project: shinchiro/mpv-winbuild-cmake
Release tag: $releaseTag
Build: $buildId
Archive: $archiveName
Archive SHA-256: $archiveSha256
Executable SHA-256: $exeSha256
Upstream licensing files, when present in the archive, are retained beside
this notice in the prepared runtime directory.
"@ | Set-Content $noticePath -Encoding utf8

Remove-Item $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Prepared mpv.exe ($((Get-Item $exePath).Length) bytes, SHA-256 $exeSha256)."
