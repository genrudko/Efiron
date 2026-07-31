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

    private static readonly TimeSpan MinimumCounterSampleWindow =
        TimeSpan.FromSeconds(1);

    private readonly object _diagnosticsSync = new();
    private readonly LibVlcPlaybackSession _session;
    private readonly ProfiledLibVlcPlaybackSession _profiledSession;
    private long? _previousDisplayedFrames;
    private DateTimeOffset? _previousFrameSampledAtUtc;
    private long? _previousReadBytes;
    private DateTimeOffset? _previousBitrateSampledAtUtc;

    public LibVlcPlaybackBackend(
        InitializedEventArgs initialization,
        LibVlcPlaybackProfile profile = LibVlcPlaybackProfile.Auto,
        bool enableDebugLogs = false)
    {
        _session = new LibVlcPlaybackSession(
            initialization,
            profile,
            enableDebugLogs);
        _profiledSession = new ProfiledLibVlcPlaybackSession(
            _session,
            profile);
    }

    public PlaybackBackendId Id => PlaybackBackendId.LibVlc;

    public string? Version => typeof(LibVLC).Assembly.GetName().Version?.ToString();

    public string SelectedProfile => _session.Profile.ToString();

    public PlaybackBackendCapabilities Capabilities => BackendCapabilities;

    public IPlaybackSession Session => _profiledSession;

    public MediaPlayer MediaPlayer => _session.MediaPlayer;

    public PlaybackBackendDiagnostics CaptureDiagnostics()
    {
        var sample = _session.CaptureDiagnosticSnapshot();
        var renderedFramesPerSecond = CalculateRenderedFramesPerSecond(sample);
        var inputBitrateBitsPerSecond = CalculateInputBitrateBitsPerSecond(sample);
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
            InputBitrateBitsPerSecond = inputBitrateBitsPerSecond,
            BufferedPercentage = sample.BufferedPercentage,
            RebufferCount = sample.RebufferCount,
            Discontinuities = sample.HasStatistics
                ? sample.Statistics.DemuxDiscontinuity
                : null,
            HardwareDecodingActive = sample.HardwareDecodingActive,
            Decoder = DescribeDecoderCompliance(sample),
            GraphicsDevice = sample.GraphicsDevice,
            VideoRenderer = sample.VideoRenderer,
            StartupLatency = sample.StartupLatency,
            TimeToFirstFrame = sample.StartupLatency,
        };
    }

    public void Dispose() => _profiledSession.Dispose();

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
            if (_previousDisplayedFrames is null ||
                _previousFrameSampledAtUtc is null ||
                displayedFrames < _previousDisplayedFrames)
            {
                _previousDisplayedFrames = displayedFrames;
                _previousFrameSampledAtUtc = sample.SampledAtUtc;
                return null;
            }

            var elapsed = sample.SampledAtUtc -
                _previousFrameSampledAtUtc.Value;
            if (elapsed < MinimumCounterSampleWindow)
            {
                return null;
            }

            var rate = PlaybackDiagnosticsMath.CalculateCounterRate(
                _previousDisplayedFrames.Value,
                _previousFrameSampledAtUtc.Value,
                displayedFrames,
                sample.SampledAtUtc);
            _previousDisplayedFrames = displayedFrames;
            _previousFrameSampledAtUtc = sample.SampledAtUtc;
            return rate;
        }
    }

    private double? CalculateInputBitrateBitsPerSecond(
        LibVlcDiagnosticSnapshot sample)
    {
        lock (_diagnosticsSync)
        {
            if (!sample.HasStatistics)
            {
                _previousReadBytes = null;
                _previousBitrateSampledAtUtc = null;
                return null;
            }

            var readBytes = Math.Max(
                (long)sample.Statistics.ReadBytes,
                sample.Statistics.DemuxReadBytes);
            if (_previousReadBytes is null ||
                _previousBitrateSampledAtUtc is null ||
                readBytes < _previousReadBytes)
            {
                _previousReadBytes = readBytes;
                _previousBitrateSampledAtUtc = sample.SampledAtUtc;
                return null;
            }

            var elapsed = sample.SampledAtUtc -
                _previousBitrateSampledAtUtc.Value;
            if (elapsed < MinimumCounterSampleWindow)
            {
                return null;
            }

            var bytes = readBytes - _previousReadBytes.Value;
            double? rate = elapsed.TotalSeconds <= 0
                ? null
                : bytes * 8d / elapsed.TotalSeconds;
            _previousReadBytes = readBytes;
            _previousBitrateSampledAtUtc = sample.SampledAtUtc;
            return rate;
        }
    }

    private string? DescribeDecoderCompliance(LibVlcDiagnosticSnapshot sample)
    {
        var mismatch = _session.Profile switch
        {
            LibVlcPlaybackProfile.Software when
                sample.HardwareDecodingActive == true =>
                "requested software, but hardware decoding remained active",
            LibVlcPlaybackProfile.Dxva2 when
                sample.Decoder?.Contains(
                    "D3D11VA",
                    StringComparison.OrdinalIgnoreCase) == true =>
                "requested DXVA2, but D3D11VA was selected",
            LibVlcPlaybackProfile.D3D11Va when
                sample.HardwareDecodingActive == false =>
                "requested D3D11VA, but hardware decoding is inactive",
            _ => null,
        };

        if (mismatch is null)
        {
            return sample.Decoder;
        }

        return string.IsNullOrWhiteSpace(sample.Decoder)
            ? $"Profile mismatch: {mismatch}."
            : $"{sample.Decoder}; profile mismatch: {mismatch}.";
    }
}
