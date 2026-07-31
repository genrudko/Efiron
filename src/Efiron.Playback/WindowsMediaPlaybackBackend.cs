using Efiron.Application.Playback;
using Efiron.Domain.Playback;
using Windows.Media.Playback;

namespace Efiron.Playback;

public sealed class WindowsMediaPlaybackBackend : IPlaybackBackend
{
    private static readonly PlaybackBackendCapabilities BackendCapabilities = new(
        ContainerMetadata: false,
        CodecMetadata: false,
        FrameStatistics: false,
        InputBitrate: false,
        Buffering: true,
        HardwareDecodingStatus: false,
        RendererMetadata: false,
        AudioTracks: false,
        SubtitleTracks: false,
        MediaPosition: true);

    private readonly WindowsMediaPlaybackSession _session = new();

    public PlaybackBackendId Id => PlaybackBackendId.WindowsMedia;

    public string? Version => Environment.OSVersion.Version.ToString();

    public string SelectedProfile => "WindowsMedia";

    public PlaybackBackendCapabilities Capabilities => BackendCapabilities;

    public IPlaybackSession Session => _session;

    public MediaPlayer MediaPlayer => _session.MediaPlayer;

    public PlaybackBackendDiagnostics CaptureDiagnostics()
    {
        var diagnostics = PlaybackBackendDiagnostics.Unsupported(
            Id,
            Version,
            SelectedProfile,
            Capabilities,
            _session.Snapshot,
            _session.SessionDuration,
            _session.MediaPosition,
            hardwareDecodingRequested: null);
        return diagnostics with
        {
            VideoWidth = _session.NaturalVideoWidth,
            VideoHeight = _session.NaturalVideoHeight,
            BufferedPercentage = _session.BufferedPercentage,
            BufferUnderruns = _session.BufferUnderruns,
            RebufferCount = _session.RebufferCount,
            StartupLatency = _session.StartupLatency,
            TimeToFirstFrame = _session.StartupLatency,
            VideoRenderer = "Windows Media / Media Foundation",
        };
    }

    public void Dispose() => _session.Dispose();
}
