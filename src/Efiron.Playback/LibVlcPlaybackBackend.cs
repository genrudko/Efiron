using Efiron.Application.Playback;
using Efiron.Domain.Playback;
using LibVLCSharp.Platforms.Windows;
using LibVLCSharp.Shared;

namespace Efiron.Playback;

public sealed class LibVlcPlaybackBackend : IPlaybackBackend
{
    private static readonly PlaybackBackendCapabilities BackendCapabilities = new(
        ContainerMetadata: false,
        CodecMetadata: false,
        FrameStatistics: false,
        InputBitrate: false,
        Buffering: false,
        HardwareDecodingStatus: false,
        RendererMetadata: false,
        AudioTracks: false,
        SubtitleTracks: false,
        MediaPosition: true);

    private readonly LibVlcPlaybackSession _session;

    public LibVlcPlaybackBackend(
        InitializedEventArgs initialization,
        LibVlcPlaybackProfile profile = LibVlcPlaybackProfile.Auto,
        bool enableDebugLogs = false)
    {
        _session = new LibVlcPlaybackSession(
            initialization,
            profile,
            enableDebugLogs);
    }

    public PlaybackBackendId Id => PlaybackBackendId.LibVlc;

    public string? Version => typeof(LibVLC).Assembly.GetName().Version?.ToString();

    public string SelectedProfile => _session.Profile.ToString();

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
            hardwareDecodingRequested: _session.Profile != LibVlcPlaybackProfile.Software);
        return diagnostics with
        {
            StartupLatency = _session.StartupLatency,
            TimeToFirstFrame = _session.StartupLatency,
            VideoRenderer = _session.Profile == LibVlcPlaybackProfile.D3D11Va
                ? "direct3d11 requested"
                : null,
        };
    }

    public void Dispose() => _session.Dispose();
}
