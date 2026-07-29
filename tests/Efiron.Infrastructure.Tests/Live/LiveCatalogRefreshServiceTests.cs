using System.Text;
using Efiron.Application.Live;
using Efiron.Application.ProgrammeGuide;
using Efiron.Application.Sources;
using Efiron.Domain.Sources;
using Efiron.Infrastructure.Playlists;
using Efiron.Infrastructure.ProgrammeGuide;
using Xunit;

namespace Efiron.Infrastructure.Tests.Live;

public sealed class LiveCatalogRefreshServiceTests
{
    [Fact]
    public async Task RefreshAsync_builds_categories_matches_and_now_next()
    {
        const string playlist = """
            #EXTM3U
            #EXTINF:-1 tvg-id="guide.one" tvg-name="Первый HD" group-title="Новости",Первый
            streams/one.m3u8
            #EXTINF:-1 tvg-name="Второй HD" group-title="Кино",Второй
            https://media.example/two.m3u8
            """;
        const string guide = """
            <tv>
              <channel id="guide.one"><display-name>Первый канал</display-name></channel>
              <channel id="guide.two"><display-name>Второй HD</display-name></channel>
              <programme channel="guide.one" start="20260730180000 +0300" stop="20260730190000 +0300">
                <title>Новости</title>
              </programme>
              <programme channel="guide.one" start="20260730190000 +0300" stop="20260730200000 +0300">
                <title>Вечер</title>
              </programme>
              <programme channel="guide.two" start="20260730180000 +0300" stop="20260730200000 +0300">
                <title>Фильм</title>
              </programme>
            </tv>
            """;
        var configuration = new SourceConfiguration(
            SourceDefinition.Create(SourceKind.Playlist, "https://provider.example/list.m3u"),
            SourceDefinition.Create(SourceKind.ProgrammeGuide, "https://provider.example/guide.xml"));
        var loader = new StubSourceContentLoader(
            Encoding.UTF8.GetBytes(playlist),
            Encoding.UTF8.GetBytes(guide));
        var service = CreateService(loader);
        var now = new DateTimeOffset(
            2026,
            7,
            30,
            18,
            30,
            0,
            TimeSpan.FromHours(3));

        var result = await service.RefreshAsync(
            configuration,
            now,
            TestContext.Current.CancellationToken);

        Assert.Equal(["Новости", "Кино"], result.Categories);
        Assert.Equal(2, result.Channels.Count);
        Assert.Equal(1, result.ProgrammeGuideExactMatches);
        Assert.Equal(1, result.ProgrammeGuideNameMatches);

        var first = result.Channels[0];
        Assert.Equal(
            new Uri("https://provider.example/streams/one.m3u8"),
            first.Channel.StreamUri);
        Assert.Equal("guide.one", first.ProgrammeGuideChannelId);
        Assert.Equal("Новости", first.CurrentProgramme?.Title);
        Assert.Equal("Вечер", first.NextProgramme?.Title);

        var second = result.Channels[1];
        Assert.Equal("guide.two", second.ProgrammeGuideChannelId);
        Assert.Equal("Фильм", second.CurrentProgramme?.Title);
        Assert.Null(second.NextProgramme);
    }

    [Fact]
    public async Task RefreshAsync_keeps_channel_catalog_when_guide_is_not_configured()
    {
        const string playlist = """
            #EXTM3U
            #EXTINF:-1 group-title="Общие",Канал
            https://media.example/live.m3u8
            """;
        var configuration = new SourceConfiguration(
            SourceDefinition.Create(SourceKind.Playlist, "https://provider.example/list.m3u"),
            ProgrammeGuide: null);
        var loader = new StubSourceContentLoader(
            Encoding.UTF8.GetBytes(playlist),
            guideContent: null);
        var service = CreateService(loader);

        var result = await service.RefreshAsync(
            configuration,
            DateTimeOffset.UtcNow,
            TestContext.Current.CancellationToken);

        var channel = Assert.Single(result.Channels);
        Assert.Equal("Канал", channel.Channel.Name);
        Assert.Null(channel.ProgrammeGuideChannelId);
        Assert.Null(channel.CurrentProgramme);
        Assert.Equal(["Общие"], result.Categories);
    }

    private static LiveCatalogRefreshService CreateService(
        ISourceContentLoader loader) =>
        new(
            loader,
            new M3uPlaylistParser(),
            new XmlTvProgrammeGuideParser(),
            new ProgrammeGuideChannelMatcher());

    private sealed class StubSourceContentLoader(
        byte[] playlistContent,
        byte[]? guideContent)
        : ISourceContentLoader
    {
        public ValueTask<LoadedSourceContent> LoadAsync(
            SourceDefinition source,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = source.Kind switch
            {
                SourceKind.Playlist => playlistContent,
                SourceKind.ProgrammeGuide when guideContent is not null => guideContent,
                _ => throw new InvalidOperationException("Unexpected source request."),
            };
            var effectiveUri = source.Kind == SourceKind.Playlist
                ? new Uri("https://provider.example/list.m3u")
                : new Uri("https://provider.example/guide.xml");

            return ValueTask.FromResult(new LoadedSourceContent(
                source,
                content,
                effectiveUri,
                "application/octet-stream",
                DateTimeOffset.UtcNow));
        }
    }
}
