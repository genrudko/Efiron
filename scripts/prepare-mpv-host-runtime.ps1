param(
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$releaseTag = "20260610"
$buildId = "20260610-git-304426c"
$archiveName = "mpv-x86_64-$buildId.7z"
$releaseApiUrl = "https://api.github.com/repos/shinchiro/mpv-winbuild-cmake/releases/tags/$releaseTag"

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

New-Item -ItemType Directory -Path $runtimeRoot,$cacheRoot -Force | Out-Null

$release = Invoke-RestMethod `
    -Uri $releaseApiUrl `
    -Headers @{ "User-Agent" = "Efiron-build" }
$asset = $release.assets |
    Where-Object name -eq $archiveName |
    Select-Object -First 1
if (-not $asset) {
    throw "Pinned mpv release contains no asset '$archiveName'."
}

$assetDigest = [string]$asset.digest
if (-not $assetDigest.StartsWith("sha256:", [StringComparison]::OrdinalIgnoreCase)) {
    throw "GitHub did not publish a SHA-256 digest for '$archiveName'."
}
$archiveSha256 = $assetDigest.Substring("sha256:".Length).ToLowerInvariant()

$manifestValid = $false
if ((Test-Path $exePath) -and (Test-Path $manifestPath)) {
    try {
        $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
        $manifestValid =
            $manifest.releaseTag -eq $releaseTag -and
            $manifest.buildId -eq $buildId -and
            $manifest.archiveName -eq $archiveName -and
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

$archiveValid = (Test-Path $archivePath) -and
    ((Get-Sha256 $archivePath) -eq $archiveSha256)
if (-not $archiveValid) {
    Remove-Item $archivePath -Force -ErrorAction SilentlyContinue
    Write-Host "Downloading pinned mpv host runtime $buildId..."
    Invoke-WebRequest `
        -Uri ([string]$asset.browser_download_url) `
        -OutFile $archivePath `
        -UseBasicParsing
    $actualArchiveSha = Get-Sha256 $archivePath
    if ($actualArchiveSha -ne $archiveSha256) {
        Remove-Item $archivePath -Force -ErrorAction SilentlyContinue
        throw "mpv host archive SHA-256 mismatch. Expected $archiveSha256, got $actualArchiveSha."
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
    archiveSha256 = $archiveSha256
    downloadUrl = [string]$asset.browser_download_url
    executableFileName = "mpv.exe"
    executableLength = (Get-Item $exePath).Length
    exeSha256 = $exeSha256
    version = $versionLine
    preparedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
} | ConvertTo-Json -Depth 3 |
    Set-Content $manifestPath -Encoding utf8

Remove-Item $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Prepared mpv.exe ($((Get-Item $exePath).Length) bytes, SHA-256 $exeSha256)."
