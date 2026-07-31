using Efiron.Application.Playback;
using Efiron.Domain.Playback;
using LibVLCSharp.Platforms.Windows;
using LibVLCSharp.Shared;

namespace Efiron.Playback;

public sealed class LibVlcPlaybackBackend : IPlaybackBackend
{
    private static readonly PlaybackBackendCapabilities BackendCapabilities = new(
        ContainerMetadata: false,
        CodecMetadata: true,
        FrameStatistics: true,
        InputBitrate: true,
        Buffering: true,
        HardwareDecodingStatus: true,
        RendererMetadata: true,
        AudioTracks: false,
        SubtitleTracks: false,
        MediaPosition: true);

    private readonly object _diagnosticsSync = new();
    private readonly LibVlcPlaybackSession _session;
    private long? _previousDisplayedFrames;
    private DateTimeOffset? _previousFrameSampledAtUtc;

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
        var sample = _session.CaptureDiagnosticSnapshot();
        var renderedFramesPerSecond = CalculateRenderedFramesPerSecond(sample);
        bool? hardwareDecodingRequested = _session.Profile switch
        {
            LibVlcPlaybackProfile.D3D11Va or LibVlcPlaybackProfile.Dxva2 => true,
            LibVlcPlaybackProfile.Software => false,
            _ => null,
        };
        var diagnostics = PlaybackBackendDiagnostics.Unsupported(
            Id,
            Version,
            SelectedProfile,
            Capabilities,
            _session.Snapshot,
            sample.SessionDuration,
            sample.MediaPosition,
            hardwareDecodingRequested);

        return diagnostics with
        {
            VideoCodec = sample.VideoCodec,
            AudioCodec = sample.AudioCodec,
            VideoWidth = sample.VideoWidth,
            VideoHeight = sample.VideoHeight,
            DeclaredFramesPerSecond = sample.DeclaredFramesPerSecond,
            RenderedFramesPerSecond = renderedFramesPerSecond,
            DisplayedFrames = sample.HasStatistics
                ? sample.Statistics.DisplayedPictures
                : null,
            DroppedFrames = sample.HasStatistics
                ? sample.Statistics.LostPictures
                : null,
            InputBitrateBitsPerSecond = sample.HasStatistics
                ? PlaybackDiagnosticsMath.BytesPerMicrosecondToBitsPerSecond(
                    sample.Statistics.InputBitrate)
                : null,
            BufferedPercentage = sample.BufferedPercentage,
            RebufferCount = sample.RebufferCount,
            Discontinuities = sample.HasStatistics
                ? sample.Statistics.DemuxDiscontinuity
                : null,
            HardwareDecodingActive = sample.HardwareDecodingActive,
            Decoder = sample.Decoder,
            GraphicsDevice = sample.GraphicsDevice,
            VideoRenderer = sample.VideoRenderer,
            StartupLatency = sample.StartupLatency,
            TimeToFirstFrame = sample.StartupLatency,
        };
    }

    public void Dispose() => _session.Dispose();

    private double? CalculateRenderedFramesPerSecond(
        LibVlcDiagnosticSnapshot sample)
    {
        lock (_diagnosticsSync)
        {
            if (!sample.HasStatistics)
            {
                _previousDisplayedFrames = null;
                _previousFrameSampledAtUtc = null;
                return null;
            }

            var displayedFrames = (long)sample.Statistics.DisplayedPictures;
            double? rate = null;
            if (_previousDisplayedFrames is { } previousFrames &&
                _previousFrameSampledAtUtc is { } previousSampledAtUtc)
            {
                rate = PlaybackDiagnosticsMath.CalculateCounterRate(
                    previousFrames,
                    previousSampledAtUtc,
                    displayedFrames,
                    sample.SampledAtUtc);
            }

            _previousDisplayedFrames = displayedFrames;
            _previousFrameSampledAtUtc = sample.SampledAtUtc;
            return rate;
        }
    }
}
