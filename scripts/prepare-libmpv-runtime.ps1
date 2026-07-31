param(
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$releaseTag = "20260610"
$buildId = "20260610-git-304426c"
$archiveName = "mpv-dev-x86_64-$buildId.7z"
$archiveSha256 = "8cbb25ea784f01afbb3f904217cab1317430a8bcfd5680fd827a866367f71cc9"
$downloadUrl = "https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/$releaseTag/$archiveName"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repositoryRoot "artifacts/libmpv/win-x64"
}
else {
    [IO.Path]::GetFullPath($OutputDirectory)
}
$cacheRoot = Join-Path $repositoryRoot "artifacts/libmpv/cache"
$archivePath = Join-Path $cacheRoot $archiveName
$dllPath = Join-Path $runtimeRoot "libmpv-2.dll"
$manifestPath = Join-Path $runtimeRoot "runtime-manifest.json"

function Get-Sha256([string]$Path) {
    (Get-FileHash -Path $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Resolve-SevenZip {
    $commands = @("7z", "7z.exe")
    foreach ($commandName in $commands) {
        $command = Get-Command $commandName -ErrorAction SilentlyContinue
        if ($command) {
            return $command.Source
        }
    }

    $standardPath = Join-Path $env:ProgramFiles "7-Zip/7z.exe"
    if (Test-Path $standardPath) {
        return $standardPath
    }

    throw "7-Zip is required to prepare the pinned libmpv runtime."
}

New-Item -ItemType Directory -Path $runtimeRoot,$cacheRoot -Force | Out-Null

$manifestValid = $false
if ((Test-Path $dllPath) -and (Test-Path $manifestPath)) {
    try {
        $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
        $manifestValid =
            $manifest.archiveSha256 -eq $archiveSha256 -and
            $manifest.buildId -eq $buildId -and
            $manifest.dllSha256 -eq (Get-Sha256 $dllPath) -and
            (Get-Item $dllPath).Length -gt 20MB
    }
    catch {
        $manifestValid = $false
    }
}

if ($manifestValid) {
    Write-Host "Pinned libmpv runtime already prepared: $dllPath"
    exit 0
}

$archiveValid = (Test-Path $archivePath) -and
    ((Get-Sha256 $archivePath) -eq $archiveSha256)
if (-not $archiveValid) {
    Remove-Item $archivePath -Force -ErrorAction SilentlyContinue
    Write-Host "Downloading pinned libmpv runtime $buildId..."
    Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath -UseBasicParsing
    $actualArchiveSha = Get-Sha256 $archivePath
    if ($actualArchiveSha -ne $archiveSha256) {
        Remove-Item $archivePath -Force -ErrorAction SilentlyContinue
        throw "libmpv archive SHA-256 mismatch. Expected $archiveSha256, got $actualArchiveSha."
    }
}

$extractRoot = Join-Path $cacheRoot "extract-$buildId"
Remove-Item $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null
$sevenZip = Resolve-SevenZip
& $sevenZip x $archivePath "-o$extractRoot" -y | Out-Host

$sourceDll = Get-ChildItem $extractRoot -Recurse -File -Filter "libmpv-2.dll" |
    Select-Object -First 1
if (-not $sourceDll) {
    throw "Pinned mpv development archive contains no libmpv-2.dll."
}
if ($sourceDll.Length -le 20MB) {
    throw "Pinned libmpv-2.dll is unexpectedly small: $($sourceDll.Length) bytes."
}

Copy-Item $sourceDll.FullName $dllPath -Force
Get-ChildItem $extractRoot -Recurse -File |
    Where-Object { $_.Name -match '^(LICENSE|COPYING|Copyright)' } |
    ForEach-Object {
        Copy-Item $_.FullName (Join-Path $runtimeRoot $_.Name) -Force
    }

$dllSha256 = Get-Sha256 $dllPath
@{
    source = "shinchiro/mpv-winbuild-cmake"
    releaseTag = $releaseTag
    buildId = $buildId
    archiveName = $archiveName
    archiveSha256 = $archiveSha256
    downloadUrl = $downloadUrl
    dllFileName = "libmpv-2.dll"
    dllLength = (Get-Item $dllPath).Length
    dllSha256 = $dllSha256
    preparedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
} | ConvertTo-Json -Depth 3 |
    Set-Content $manifestPath -Encoding utf8

Remove-Item $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Prepared libmpv-2.dll ($((Get-Item $dllPath).Length) bytes, SHA-256 $dllSha256)."
