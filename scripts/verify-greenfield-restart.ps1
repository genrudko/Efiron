param(
    [Parameter(Mandatory = $true)]
    [string]$ApplicationOutput
)

$ErrorActionPreference = "Stop"

$executable = Join-Path $ApplicationOutput "Efiron.exe"
if (-not (Test-Path -LiteralPath $executable)) {
    throw "Efiron executable was not found at '$executable'."
}

$fixture = Join-Path $env:RUNNER_TEMP "efiron-restart-fixture"
$playlist = Join-Path $fixture "playlist.m3u"
$guide = Join-Path $fixture "guide.xml"
$wave = Join-Path $fixture "ci.wav"
$localAppData = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::LocalApplicationData)
$efironData = Join-Path $localAppData "Efiron"
$configuration = Join-Path $efironData "sources.json"
$preferencesPath = Join-Path $efironData "playback.json"
$diagnostics = Join-Path $efironData "diagnostics"
$controlEvidence = Join-Path $diagnostics "playback-controls.json"
$restartEvidence = Join-Path $diagnostics "playback-restart.json"
$firstProcess = $null
$secondProcess = $null
$serverProcess = $null

function Stop-TestProcess {
    param([System.Diagnostics.Process]$Process)

    if ($null -eq $Process) {
        return
    }

    $Process.Refresh()
    if (-not $Process.HasExited) {
        Stop-Process -Id $Process.Id -Force
        $Process.WaitForExit(10000) | Out-Null
    }
}

function Wait-ForFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds,
        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while (-not (Test-Path -LiteralPath $Path) -and
           [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 200
        $Process.Refresh()
        if ($Process.HasExited) {
            throw "Efiron exited while waiting for $Description with code $($Process.ExitCode)."
        }
    }

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Efiron did not produce $Description within $TimeoutSeconds seconds."
    }
}

try {
    Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $efironData -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $fixture -Force | Out-Null
    New-Item -ItemType Directory -Path $efironData -Force | Out-Null

    $sampleRate = 8000
    $channels = 1
    $bitsPerSample = 16
    $durationSeconds = 60
    $blockAlign = [int]($channels * $bitsPerSample / 8)
    $byteRate = $sampleRate * $blockAlign
    $dataLength = $byteRate * $durationSeconds
    $stream = [System.IO.File]::Create($wave)
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        $ascii = [System.Text.Encoding]::ASCII
        $writer.Write($ascii.GetBytes("RIFF"))
        $writer.Write([int](36 + $dataLength))
        $writer.Write($ascii.GetBytes("WAVE"))
        $writer.Write($ascii.GetBytes("fmt "))
        $writer.Write([int]16)
        $writer.Write([int16]1)
        $writer.Write([int16]$channels)
        $writer.Write([int]$sampleRate)
        $writer.Write([int]$byteRate)
        $writer.Write([int16]$blockAlign)
        $writer.Write([int16]$bitsPerSample)
        $writer.Write($ascii.GetBytes("data"))
        $writer.Write([int]$dataLength)

        $silence = [byte[]]::new(65536)
        $remaining = $dataLength
        while ($remaining -gt 0) {
            $count = [Math]::Min($silence.Length, $remaining)
            $writer.Write($silence, 0, $count)
            $remaining -= $count
        }
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }

    @'
#EXTM3U
#EXTINF:-1 tvg-id="ci.news" tvg-name="CI News" group-title="News",CI News
http://127.0.0.1:18766/ci.wav
#EXTINF:-1 tvg-name="CI Cinema" group-title="Cinema",CI Cinema
http://127.0.0.1:18766/ci.wav
'@ | Set-Content -LiteralPath $playlist -Encoding utf8

    $now = [DateTimeOffset]::UtcNow
    $currentStart = $now.AddMinutes(-20).ToString("yyyyMMddHHmmss +0000")
    $currentStop = $now.AddMinutes(40).ToString("yyyyMMddHHmmss +0000")
    $nextStop = $now.AddMinutes(100).ToString("yyyyMMddHHmmss +0000")
    @"
<tv>
  <channel id="ci.news"><display-name>CI News</display-name></channel>
  <channel id="ci.cinema"><display-name>CI Cinema</display-name></channel>
  <programme channel="ci.news" start="$currentStart" stop="$currentStop">
    <title>CI News Now</title>
  </programme>
  <programme channel="ci.news" start="$currentStop" stop="$nextStop">
    <title>CI News Next</title>
  </programme>
  <programme channel="ci.cinema" start="$currentStart" stop="$currentStop">
    <title>CI Movie Now</title>
  </programme>
  <programme channel="ci.cinema" start="$currentStop" stop="$nextStop">
    <title>CI Movie Next</title>
  </programme>
</tv>
"@ | Set-Content -LiteralPath $guide -Encoding utf8

    @{
        playlist = @{
            location = $playlist
            isEnabled = $true
        }
        programmeGuide = @{
            location = $guide
            isEnabled = $true
        }
    } |
        ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath $configuration -Encoding utf8

    $python = (Get-Command python -ErrorAction Stop).Source
    $serverProcess = Start-Process `
        -FilePath $python `
        -ArgumentList @(
            "-m",
            "http.server",
            "18766",
            "--bind",
            "127.0.0.1",
            "--directory",
            $fixture) `
        -WindowStyle Hidden `
        -PassThru
    Start-Sleep -Seconds 2
    $serverProcess.Refresh()
    if ($serverProcess.HasExited) {
        throw "The local media server exited with code $($serverProcess.ExitCode)."
    }

    $env:EFIRON_CI_PLAYBACK_SEQUENCE = "1"
    Remove-Item -LiteralPath $controlEvidence -Force -ErrorAction SilentlyContinue
    $firstProcess = Start-Process `
        -FilePath $executable `
        -WorkingDirectory $ApplicationOutput `
        -PassThru

    Wait-ForFile `
        -Path $controlEvidence `
        -Process $firstProcess `
        -TimeoutSeconds 55 `
        -Description "playback control evidence"

    $controls = Get-Content -LiteralPath $controlEvidence -Raw | ConvertFrom-Json
    if (-not [string]::IsNullOrWhiteSpace([string]$controls.Error)) {
        throw "The first playback sequence failed: $($controls.Error)"
    }
    foreach ($property in @(
        "Paused",
        "Resumed",
        "VolumeSetTo37",
        "Muted",
        "Unmuted",
        "Stopped",
        "SwitchedToSecondChannel")) {
        if (-not [bool]$controls.$property) {
            throw "The first playback sequence did not prove '$property'."
        }
    }
    if ([string]::IsNullOrWhiteSpace([string]$controls.SecondChannelStableId)) {
        throw "The first playback sequence did not identify the second channel."
    }

    $preferenceDeadline = [DateTime]::UtcNow.AddSeconds(12)
    $stored = $null
    while ([DateTime]::UtcNow -lt $preferenceDeadline) {
        if (Test-Path -LiteralPath $preferencesPath) {
            try {
                $candidate = Get-Content -LiteralPath $preferencesPath -Raw | ConvertFrom-Json
                if ($candidate.selectedChannelStableId -eq $controls.SecondChannelStableId -and
                    [int]$candidate.volume -eq 37 -and
                    -not [bool]$candidate.isMuted) {
                    $stored = $candidate
                    break
                }
            }
            catch {
            }
        }

        Start-Sleep -Milliseconds 200
        $firstProcess.Refresh()
        if ($firstProcess.HasExited) {
            throw "Efiron exited before playback preferences were persisted with code $($firstProcess.ExitCode)."
        }
    }

    if ($null -eq $stored) {
        throw "The first launch did not persist the final channel, volume and mute state."
    }

    Stop-TestProcess -Process $firstProcess
    $firstProcess = $null

    Remove-Item -LiteralPath $restartEvidence -Force -ErrorAction SilentlyContinue
    $env:EFIRON_CI_PLAYBACK_SEQUENCE = $null
    $env:EFIRON_CI_RESTART_VERIFICATION = "1"
    $env:GITHUB_ACTIONS = "false"

    $secondProcess = Start-Process `
        -FilePath $executable `
        -WorkingDirectory $ApplicationOutput `
        -PassThru

    Wait-ForFile `
        -Path $restartEvidence `
        -Process $secondProcess `
        -TimeoutSeconds 40 `
        -Description "restart-state evidence"

    $restart = Get-Content -LiteralPath $restartEvidence -Raw | ConvertFrom-Json
    if ($restart.State -ne "Playing") {
        throw "The second launch did not reach Playing; state was '$($restart.State)'."
    }
    if ($restart.ChannelStableId -ne $controls.SecondChannelStableId -or
        $restart.StoredChannelStableId -ne $controls.SecondChannelStableId) {
        throw "The second launch did not restore the selected channel."
    }
    if ([int]$restart.Volume -ne 37 -or [int]$restart.StoredVolume -ne 37) {
        throw "The second launch did not restore volume 37."
    }
    if ([bool]$restart.IsMuted -or [bool]$restart.StoredIsMuted) {
        throw "The second launch did not restore the unmuted state."
    }

    Start-Sleep -Seconds 3
    $secondProcess.Refresh()
    if ($secondProcess.HasExited) {
        throw "Efiron exited after restart-state restoration with code $($secondProcess.ExitCode)."
    }

    @(
        "First launch persisted playback preferences."
        "Second launch restored the selected channel, volume and mute state."
        "Restored channel: $($restart.ChannelStableId)"
        "Restored volume: $($restart.Volume)"
        "Restored muted: $($restart.IsMuted)"
        "Preference file: $preferencesPath"
        "Restart evidence: $restartEvidence"
    ) | Set-Content -LiteralPath rewrite-restart.log -Encoding utf8
}
finally {
    Stop-TestProcess -Process $firstProcess
    Stop-TestProcess -Process $secondProcess
    Stop-TestProcess -Process $serverProcess
}
