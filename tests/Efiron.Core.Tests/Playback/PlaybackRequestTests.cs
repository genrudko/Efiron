using Efiron.Core.Playback;

namespace Efiron.Core.Tests.Playback;

public sealed class PlaybackRequestTests
{
    [Theory]
    [InlineData("https://example.test/live/index.m3u8")]
    [InlineData("http://example.test/channel.ts")]
    [InlineData("rtsp://example.test/live")]
    [InlineData("udp://@239.0.0.1:1234")]
    public void Constructor_accepts_supported_absolute_sources(string value)
    {
        var source = new Uri(value, UriKind.Absolute);

        var request = new PlaybackRequest(source);

        Assert.Equal(source, request.Source);
    }

    [Fact]
    public void Constructor_rejects_relative_source()
    {
        var source = new Uri("live/index.m3u8", UriKind.Relative);

        Assert.Throws<ArgumentException>(() => new PlaybackRequest(source));
    }

    [Fact]
    public void Constructor_rejects_unsupported_scheme()
    {
        var source = new Uri("file:///C:/video.ts", UriKind.Absolute);

        Assert.Throws<NotSupportedException>(() => new PlaybackRequest(source));
    }
}
