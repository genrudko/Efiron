using Efiron.Core.Epg;
using Efiron.Core.Playlists;
using Xunit;

namespace Efiron.Core.Tests.Epg;

public sealed class EpgChannelMatcherTests
{
    [Fact]
    public void Match_prioritizes_exact_tvg_id_over_name_candidates()
    {
        var playlist = new[]
        {
            Channel("stable-1", "Wrong Name", tvgId: "xml.exact", tvgName: "Other Channel"),
        };
        var xml = new[]
        {
            new XmlTvChannel("xml.exact", ["Exact Channel"], null),
            new XmlTvChannel("xml.other", ["Other Channel"], null),
        };

        var result = new EpgChannelMatcher().Match(playlist, xml);

        Assert.Equal("xml.exact", result.PlaylistChannelMatches["stable-1"]);
        Assert.Equal(1, result.ExactIdMatches);
        Assert.Equal(0, result.UniqueNameMatches);
    }

    [Fact]
    public void Match_uses_unique_normalized_display_name_as_fallback()
    {
        var playlist = new[]
        {
            Channel("stable-1", "Первый канал HD+", tvgId: null, tvgName: null),
        };
        var xml = new[]
        {
            new XmlTvChannel("channel.one", ["Первый канал HD"], null),
        };

        var result = new EpgChannelMatcher().Match(playlist, xml);

        Assert.Equal("channel.one", result.PlaylistChannelMatches["stable-1"]);
        Assert.Equal(0, result.ExactIdMatches);
        Assert.Equal(1, result.UniqueNameMatches);
    }

    [Fact]
    public void Match_does_not_guess_when_normalized_name_is_ambiguous()
    {
        var playlist = new[]
        {
            Channel("stable-1", "Россия 1", tvgId: null, tvgName: null),
        };
        var xml = new[]
        {
            new XmlTvChannel("russia.one.moscow", ["Россия 1"], null),
            new XmlTvChannel("russia.one.regional", ["Россия-1"], null),
        };

        var result = new EpgChannelMatcher().Match(playlist, xml);

        Assert.Empty(result.PlaylistChannelMatches);
        Assert.Equal(0, result.ExactIdMatches);
        Assert.Equal(0, result.UniqueNameMatches);
    }

    private static PlaylistChannel Channel(
        string stableId,
        string name,
        string? tvgId,
        string? tvgName) =>
        new(
            stableId,
            name,
            new Uri("https://example.test/live.m3u8"),
            tvgId,
            tvgName,
            null,
            null,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            1);
}
