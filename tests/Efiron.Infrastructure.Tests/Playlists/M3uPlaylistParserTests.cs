using Efiron.Infrastructure.Playlists;
using Xunit;

namespace Efiron.Infrastructure.Tests.Playlists;

public sealed class M3uPlaylistParserTests
{
    private readonly M3uPlaylistParser _parser = new();

    [Fact]
    public void Parse_accepts_bom_crlf_attributes_commas_groups_and_catchup_metadata()
    {
        const string content = "\uFEFF#EXTM3U url-tvg=\"https://epg.example/guide.xml.gz\"\r\n" +
            "#EXTINF:-1 tvg-id=\"news.one\" tvg-name=\"News One\" tvg-logo=\"logos/news.png\" group-title=\"News\" catchup=\"default\" catchup-days=\"7\",News One, HD\r\n" +
            "https://media.example/news/index.m3u8\r\n" +
            "#EXTINF:-1 tvg-id='sport.one',Sport One\r\n" +
            "#EXTGRP:Sport\r\n" +
            "https://media.example/sport/index.m3u8\r\n";
        var sourceUri = new Uri("https://provider.example/account/playlist.m3u");

        var result = _parser.Parse(content, sourceUri);

        Assert.Equal(2, result.Channels.Count);
        Assert.Equal(
            "https://epg.example/guide.xml.gz",
            result.HeaderAttributes["url-tvg"]);

        var news = result.Channels[0];
        Assert.Equal("News One, HD", news.Name);
        Assert.Equal("news.one", news.ProgrammeGuideId);
        Assert.Equal("News", news.Category);
        Assert.Equal("default", news.Attributes["catchup"]);
        Assert.Equal("7", news.Attributes["catchup-days"]);
        Assert.Equal(
            new Uri("https://provider.example/account/logos/news.png"),
            news.LogoUri);

        var sport = result.Channels[1];
        Assert.Equal("Sport", sport.Category);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData("#EXTM3U\n#EXT-X-TARGETDURATION:6\n#EXTINF:6.0,\nsegment001.ts")]
    [InlineData("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=5000000\nvariant.m3u8")]
    public void Parse_rejects_hls_media_and_master_manifests(string content)
    {
        Assert.Throws<InvalidDataException>(() => _parser.Parse(
            content,
            new Uri("https://media.example/live/index.m3u8")));
    }

    [Fact]
    public void Parse_skips_malformed_entries_and_keeps_valid_channels()
    {
        const string content = "#EXTM3U\n" +
            "#EXTINF:-1 tvg-id=\"broken\",Broken\n" +
            "#EXTINF:-1 tvg-id=\"valid\",Valid\n" +
            "https://media.example/valid.m3u8\n" +
            "not an absolute uri\n";

        var result = _parser.Parse(content);

        var channel = Assert.Single(result.Channels);
        Assert.Equal("Valid", channel.Name);
        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains(result.Warnings, warning => warning.LineNumber == 2);
        Assert.Contains(result.Warnings, warning => warning.LineNumber == 5);
    }

    [Fact]
    public void Parse_resolves_relative_stream_uris_against_playlist_source()
    {
        const string content =
            "#EXTM3U\n#EXTINF:-1,Relative\nstreams/channel.m3u8\n";

        var result = _parser.Parse(
            content,
            new Uri("https://provider.example/user/playlist.m3u"));

        Assert.Equal(
            new Uri("https://provider.example/user/streams/channel.m3u8"),
            Assert.Single(result.Channels).StreamUri);
    }

    [Fact]
    public void Parse_keeps_channel_identity_stable_when_guide_id_is_stable()
    {
        const string first =
            "#EXTM3U\n#EXTINF:-1 tvg-id=\"stable.channel\",Channel\nhttps://old.example/live.m3u8\n";
        const string second =
            "#EXTM3U\n#EXTINF:-1 tvg-id=\"stable.channel\",Renamed Channel\nhttps://new.example/live.m3u8\n";

        var firstChannel = Assert.Single(_parser.Parse(first).Channels);
        var secondChannel = Assert.Single(_parser.Parse(second).Channels);

        Assert.Equal(firstChannel.StableId, secondChannel.StableId);
    }

    [Fact]
    public void Parse_assigns_unique_ids_to_duplicate_channel_identities()
    {
        const string content = "#EXTM3U\n" +
            "#EXTINF:-1 tvg-id=\"duplicate\",First\nhttps://one.example/live.m3u8\n" +
            "#EXTINF:-1 tvg-id=\"duplicate\",Second\nhttps://two.example/live.m3u8\n";

        var result = _parser.Parse(content);

        Assert.Equal(2, result.Channels.Count);
        Assert.NotEqual(
            result.Channels[0].StableId,
            result.Channels[1].StableId);
    }

    [Fact]
    public void Parse_preserves_player_directives_and_inline_url_options()
    {
        const string content = "#EXTM3U\n" +
            "#EXTINF:-1,Protected channel\n" +
            "#EXTVLCOPT:http-user-agent=Efiron Test\n" +
            "#KODIPROP:inputstream.adaptive.manifest_type=hls\n" +
            "https://media.example/live.m3u8|Referer=https://provider.example/\n";

        var channel = Assert.Single(_parser.Parse(content).Channels);

        Assert.Equal(
            "Efiron Test",
            channel.PlaybackDirectives["extvlcopt:http-user-agent"]);
        Assert.Equal(
            "hls",
            channel.PlaybackDirectives["kodiprop:inputstream.adaptive.manifest_type"]);
        Assert.Equal(
            "Referer=https://provider.example/",
            channel.PlaybackDirectives["url-options"]);
    }
}
