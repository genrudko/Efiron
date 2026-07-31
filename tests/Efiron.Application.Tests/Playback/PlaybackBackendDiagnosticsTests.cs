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
    [InlineData(PlaybackBackendId.Auto)]
    [InlineData(PlaybackBackendId.LibVlc)]
    [InlineData(PlaybackBackendId.WindowsMedia)]
    public void Backend_selection_has_stable_serializable_identity(
        PlaybackBackendId backendId)
    {
        Assert.False(string.IsNullOrWhiteSpace(backendId.ToString()));
    }
}
