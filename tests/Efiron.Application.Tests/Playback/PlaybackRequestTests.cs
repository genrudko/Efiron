using Efiron.Domain.Playback;
using Xunit;

namespace Efiron.Application.Tests.Playback;

public sealed class PlaybackRequestTests
{
    [Theory]
    [InlineData("http://media.example/live")]
    [InlineData("https://media.example/live")]
    [InlineData("rtsp://media.example/live")]
    [InlineData("rtmp://media.example/live")]
    [InlineData("rtp://239.0.0.1:1234")]
    [InlineData("udp://@239.0.0.1:1234")]
    public void Constructor_accepts_supported_absolute_sources(string source)
    {
        var expected = new Uri(source);
        var request = new PlaybackRequest(expected);

        Assert.Equal(expected, request.Source);
    }

    [Fact]
    public void Constructor_rejects_relative_source()
    {
        var source = new Uri("streams/live.m3u8", UriKind.Relative);

        Assert.Throws<ArgumentException>(() => new PlaybackRequest(source));
    }

    [Fact]
    public void Constructor_rejects_unsupported_scheme()
    {
        var source = new Uri("file:///C:/video.ts");

        Assert.Throws<NotSupportedException>(() => new PlaybackRequest(source));
    }

    [Fact]
    public void Constructor_normalizes_identity_and_copies_directives()
    {
        var directives = new Dictionary<string, string>
        {
            ["extvlcopt:http-user-agent"] = "Efiron Test",
        };
        var request = new PlaybackRequest(
            new Uri("https://media.example/live"),
            "  stable-id  ",
            "  Channel name  ",
            directives);

        directives["extvlcopt:http-user-agent"] = "Changed";

        Assert.Equal("stable-id", request.ChannelStableId);
        Assert.Equal("Channel name", request.DisplayName);
        Assert.Equal(
            "Efiron Test",
            request.Directives["EXTVLCOPT:HTTP-USER-AGENT"]);
    }
}
