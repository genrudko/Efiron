using Efiron.Domain.Playback;
using Xunit;

namespace Efiron.Application.Tests.Playback;

public sealed class PlaybackBackendDiagnosticsTests
{
    [Fact]
    public void Unsupported_metrics_remain_null_instead_of_fake_zeroes()
    {
        var capabilities = new PlaybackBackendCapabilities(
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
        var snapshot = PlaybackSnapshot.Idle with
        {
            State = PlaybackState.Playing,
            Source = new Uri("https://media.example/live.m3u8"),
            ChannelStableId = "channel-1",
            DisplayName = "4K test",
        };

        var diagnostics = PlaybackBackendDiagnostics.Unsupported(
            PlaybackBackendId.LibVlc,
            "3.0.23",
            "D3D11Va",
            capabilities,
            snapshot,
            sessionDuration: TimeSpan.FromSeconds(12),
            mediaPosition: TimeSpan.FromSeconds(4),
            hardwareDecodingRequested: true);

        Assert.Equal("https", diagnostics.StreamUrlScheme);
        Assert.True(diagnostics.HardwareDecodingRequested);
        Assert.Equal(TimeSpan.FromSeconds(12), diagnostics.SessionDuration);
        Assert.Equal(TimeSpan.FromSeconds(4), diagnostics.MediaPosition);
        Assert.Null(diagnostics.RenderedFramesPerSecond);
        Assert.Null(diagnostics.DisplayedFrames);
        Assert.Null(diagnostics.DroppedFrames);
        Assert.Null(diagnostics.InputBitrateBitsPerSecond);
        Assert.Null(diagnostics.HardwareDecodingActive);
        Assert.Null(diagnostics.Decoder);
        Assert.False(diagnostics.Capabilities.FrameStatistics);
        Assert.False(diagnostics.Capabilities.HardwareDecodingStatus);
    }

    [Theory]
    [InlineData(0d, 0d)]
    [InlineData(2.5d, 20_000_000d)]
    public void LibVlc_byte_rate_is_converted_to_bits_per_second(
        double bytesPerMicrosecond,
        double expectedBitsPerSecond)
    {
        Assert.Equal(
            expectedBitsPerSecond,
            PlaybackDiagnosticsMath.BytesPerMicrosecondToBitsPerSecond(
                bytesPerMicrosecond));
    }

    [Fact]
    public void Counter_rate_uses_elapsed_sample_time()
    {
        var start = new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

        var rate = PlaybackDiagnosticsMath.CalculateCounterRate(
            previousValue: 100,
            previousSampledAtUtc: start,
            currentValue: 150,
            currentSampledAtUtc: start.AddSeconds(2));

        Assert.Equal(25d, rate);
    }

    [Fact]
    public void Counter_reset_does_not_create_negative_rendered_fps()
    {
        var start = new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

        var rate = PlaybackDiagnosticsMath.CalculateCounterRate(
            previousValue: 500,
            previousSampledAtUtc: start,
            currentValue: 2,
            currentSampledAtUtc: start.AddSeconds(2));

        Assert.Null(rate);
    }

    [Theory]
    [InlineData(PlaybackBackendId.Auto)]
    [InlineData(PlaybackBackendId.LibVlc)]
    [InlineData(PlaybackBackendId.WindowsMedia)]
    public void Backend_selection_has_stable_serializable_identity(
        PlaybackBackendId backendId)
    {
        Assert.False(string.IsNullOrWhiteSpace(backendId.ToString()));
    }
}
