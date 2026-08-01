using Efiron.Application.Playback;
using Efiron.Domain.Playback;

namespace Efiron.Playback;

public sealed class MpvPlaybackBackend : IPlaybackBackend
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

    private readonly MpvPlaybackSession _session;

    public MpvPlaybackBackend(
        MpvPlaybackProfile profile = MpvPlaybackProfile.Auto)
    {
        _session = new MpvPlaybackSession(profile);
    }

    public event EventHandler? DisplaySwapChainChanged
    {
        add => _session.DisplaySwapChainChanged += value;
        remove => _session.DisplaySwapChainChanged -= value;
    }

    public PlaybackBackendId Id => PlaybackBackendId.Mpv;

    public string? Version => _session.CaptureDiagnosticSnapshot().Version;

    public string SelectedProfile => _session.Profile.ToString();

    public PlaybackBackendCapabilities Capabilities => BackendCapabilities;

    public IPlaybackSession Session => _session;

    public nint DisplaySwapChain => _session.DisplaySwapChain;

    public void SetCompositionSize(int width, int height) =>
        _session.SetCompositionSize(width, height);

    public void SetFullscreenVideoFill(bool isFullscreen) =>
        _session.SetFullscreenVideoFill(isFullscreen);

    public PlaybackBackendDiagnostics CaptureDiagnostics()
    {
        var sample = _session.CaptureDiagnosticSnapshot();
        bool? hardwareDecodingRequested = _session.Profile switch
        {
            MpvPlaybackProfile.SmoothMotion => true,
            _ => null,
        };
        var hardwareDecoder = NormalizeHardwareDecoder(sample.HardwareDecoder);
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
            VideoRenderer = sample.VideoRenderer,
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
            PresentationMode = sample.PresentationMode,
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
}
