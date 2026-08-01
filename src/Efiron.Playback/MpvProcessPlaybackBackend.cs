using System.Text.Json;
using Efiron.Application.Playback;
using Efiron.Domain.Playback;

namespace Efiron.Playback;

public sealed class MpvProcessPlaybackBackend : IPlaybackBackend
{
    private static readonly PlaybackBackendCapabilities BackendCapabilities = new(
        ContainerMetadata: true,
        CodecMetadata: true,
        FrameStatistics: true,
        InputBitrate: false,
        Buffering: true,
        HardwareDecodingStatus: true,
        RendererMetadata: true,
        AudioTracks: false,
        SubtitleTracks: false,
        MediaPosition: true);

    private readonly RestartingMpvProcessPlaybackSession _session;

    public MpvProcessPlaybackBackend(
        nint hostWindowHandle,
        MpvPlaybackProfile profile = MpvPlaybackProfile.Auto)
    {
        _session = new RestartingMpvProcessPlaybackSession(
            hostWindowHandle,
            profile);
    }

    public PlaybackBackendId Id => PlaybackBackendId.MpvHost;

    public string? Version => _session.CaptureDiagnosticSnapshot().Version;

    public string SelectedProfile => _session.Profile.ToString();

    public PlaybackBackendCapabilities Capabilities => BackendCapabilities;

    public IPlaybackSession Session => _session;

    public int? HostProcessId => _session.HostProcessId;

    public PlaybackBackendDiagnostics CaptureDiagnostics()
    {
        var sample = _session.CaptureDiagnosticSnapshot();
        bool? hardwareDecodingRequested = _session.Profile switch
        {
            MpvPlaybackProfile.SmoothMotion => true,
            _ => null,
        };
        var hardwareDecoder = NormalizeHardwareDecoder(sample.HardwareDecoder);
        var videoRenderer = NormalizeVideoRenderer(sample.VideoRenderer);
        var diagnostics = PlaybackBackendDiagnostics.Unsupported(
            Id,
            sample.Version,
            SelectedProfile,
            Capabilities,
            sample.Snapshot,
            sample.SessionDuration,
            sample.MediaPosition,
            hardwareDecodingRequested);

        return diagnostics with
        {
            Container = sample.Container,
            VideoCodec = sample.VideoCodec,
            AudioCodec = sample.AudioCodec,
            VideoWidth = sample.VideoWidth,
            VideoHeight = sample.VideoHeight,
            DeclaredFramesPerSecond = sample.DeclaredFramesPerSecond,
            RenderedFramesPerSecond = sample.RenderedFramesPerSecond,
            DroppedFrames = sample.DroppedFrames,
            BufferDuration = sample.BufferDurationSeconds is null
                ? null
                : TimeSpan.FromSeconds(sample.BufferDurationSeconds.Value),
            BufferedPercentage = sample.BufferedPercentage,
            HardwareDecodingActive = hardwareDecoder is not null,
            Decoder = hardwareDecoder ?? sample.VideoCodec,
            VideoRenderer = videoRenderer,
            AudioVideoDrift = sample.AudioVideoDrift,
            StartupLatency = sample.StartupLatency,
            TimeToFirstFrame = sample.StartupLatency,
            DisplayFramesPerSecond = sample.DisplayFramesPerSecond,
            EstimatedDisplayFramesPerSecond =
                sample.EstimatedDisplayFramesPerSecond,
            VideoSpeedCorrection = sample.VideoSpeedCorrection,
            AudioSpeedCorrection = sample.AudioSpeedCorrection,
            VSyncRatio = sample.VSyncRatio,
            MistimedFrames = sample.MistimedFrames,
            DelayedFrames = sample.DelayedFrames,
            PixelFormat = sample.PixelFormat,
            PresentationMode = string.Join(
                "; ",
                new[]
                {
                    "process=out-of-process",
                    "d3d11-output=native-window",
                    string.IsNullOrWhiteSpace(sample.VideoSync)
                        ? null
                        : $"video-sync={sample.VideoSync}",
                    $"interpolation={sample.InterpolationActive?.ToString() ?? "unknown"}",
                    sample.HostProcessId is null
                        ? null
                        : $"pid={sample.HostProcessId}",
                    sample.HostWorkingSetBytes is null
                        ? null
                        : $"host-working-set={sample.HostWorkingSetBytes}",
                    sample.HostPrivateMemoryBytes is null
                        ? null
                        : $"host-private={sample.HostPrivateMemoryBytes}",
                    sample.HostHandleCount is null
                        ? null
                        : $"host-handles={sample.HostHandleCount}",
                }.Where(static value => value is not null)),
            InterpolationActive = sample.InterpolationActive,
        };
    }

    public void Dispose() => _session.Dispose();

    private static string? NormalizeHardwareDecoder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return value.Trim();
    }

    private static string? NormalizeVideoRenderer(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith('[', StringComparison.Ordinal))
        {
            return trimmed;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            foreach (var output in document.RootElement.EnumerateArray())
            {
                var enabled = !output.TryGetProperty("enabled", out var enabledElement) ||
                    enabledElement.ValueKind != JsonValueKind.False;
                if (enabled &&
                    output.TryGetProperty("name", out var nameElement) &&
                    nameElement.GetString() is { Length: > 0 } name)
                {
                    return name;
                }
            }
        }
        catch (JsonException)
        {
        }

        return trimmed;
    }
}
