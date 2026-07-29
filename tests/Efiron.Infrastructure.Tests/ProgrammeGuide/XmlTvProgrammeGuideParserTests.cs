using System.IO.Compression;
using System.Text;
using Efiron.Infrastructure.ProgrammeGuide;
using Xunit;

namespace Efiron.Infrastructure.Tests.ProgrammeGuide;

public sealed class XmlTvProgrammeGuideParserTests
{
    private readonly XmlTvProgrammeGuideParser _parser = new();

    [Fact]
    public void Parse_reads_channels_programmes_offsets_and_provider_categories()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE tv SYSTEM "https://example.test/xmltv.dtd">
            <tv>
              <channel id="channel.one">
                <display-name lang="ru">Первый канал HD</display-name>
                <display-name>Channel One HD</display-name>
                <icon src="https://example.test/channel-one.png" />
              </channel>
              <programme channel="channel.one" start="20260726190000 +0300" stop="20260726200000 +0300">
                <title lang="ru">Новости</title>
                <sub-title>Вечерний выпуск</sub-title>
                <desc>[alias:news, Информация] Главные события дня.</desc>
                <category>[alias:news, Новости]</category>
              </programme>
            </tv>
            """;

        var result = _parser.Parse(Encoding.UTF8.GetBytes(xml));

        var channel = Assert.Single(result.Channels);
        Assert.Equal("channel.one", channel.Id);
        Assert.Equal(2, channel.DisplayNames.Count);
        Assert.Equal(new Uri("https://example.test/channel-one.png"), channel.IconUri);

        var programme = Assert.Single(result.Programmes);
        Assert.Equal("channel.one", programme.ChannelId);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 26, 19, 0, 0, TimeSpan.FromHours(3)),
            programme.Start);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 26, 20, 0, 0, TimeSpan.FromHours(3)),
            programme.Stop);
        Assert.Equal("Новости", programme.Title);
        Assert.Equal("Вечерний выпуск", programme.Subtitle);
        Assert.Equal("Главные события дня.", programme.Description);
        Assert.Equal(["Новости", "Информация"], programme.Categories);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Parse_accepts_gzip_xmltv_payload()
    {
        const string xml = """
            <tv>
              <channel id="gzip"><display-name>Gzip Channel</display-name></channel>
              <programme channel="gzip" start="20260726190000 Z">
                <title>Compressed programme</title>
              </programme>
            </tv>
            """;

        var compressed = Compress(xml);
        var result = _parser.Parse(compressed);

        Assert.Equal("gzip", Assert.Single(result.Channels).Id);
        Assert.Equal("Compressed programme", Assert.Single(result.Programmes).Title);
    }

    [Fact]
    public void Parse_uses_utc_when_timestamp_offset_is_missing()
    {
        const string xml = """
            <tv>
              <channel id="utc"><display-name>UTC Channel</display-name></channel>
              <programme channel="utc" start="20260726190000">
                <title>Programme</title>
              </programme>
            </tv>
            """;

        var programme = Assert.Single(
            _parser.Parse(Encoding.UTF8.GetBytes(xml)).Programmes);

        Assert.Equal(TimeSpan.Zero, programme.Start.Offset);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 26, 19, 0, 0, TimeSpan.Zero),
            programme.Start);
    }

    [Fact]
    public void Parse_keeps_valid_entries_and_reports_invalid_entries()
    {
        const string xml = """
            <tv>
              <channel id="valid"><display-name>Valid</display-name></channel>
              <channel id="valid"><display-name>Duplicate</display-name></channel>
              <programme channel="valid" start="not-a-date"><title>Broken</title></programme>
              <programme channel="valid" start="20260726200000 +0000" stop="20260726190000 +0000"><title>Working</title></programme>
            </tv>
            """;

        var result = _parser.Parse(Encoding.UTF8.GetBytes(xml));

        Assert.Single(result.Channels);
        Assert.Single(result.Programmes);
        Assert.Equal("Working", result.Programmes[0].Title);
        Assert.Equal(3, result.Warnings.Count);
        Assert.Null(result.Programmes[0].Stop);
    }

    [Fact]
    public void Parse_rejects_non_xmltv_root()
    {
        var content = Encoding.UTF8.GetBytes("<playlist />");

        Assert.Throws<InvalidDataException>(() => _parser.Parse(content));
    }

    private static byte[] Compress(string content)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(
                   output,
                   CompressionLevel.SmallestSize,
                   leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            gzip.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }
}
