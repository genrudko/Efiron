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
    private const string Playlist = """
        #EXTM3U
        #EXTINF:-1 tvg-id="guide.one" tvg-name="Первый HD" group-title="Новости",Первый
        streams/one.m3u8
        #EXTINF:-1 tvg-name="Второй HD" group-title="Кино",Второй
        https://media.example/two.m3u8
        """;

    private const string Guide = """
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

    private static readonly DateTimeOffset Now = new(
        2026,
        7,
        30,
        18,
        30,
        0,
        TimeSpan.FromHours(3));

    [Fact]
    public async Task RefreshAsync_builds_categories_matches_now_next_and_schedule()
    {
        var configuration = CreateRemoteConfiguration();
        var loader = CreateLoader();
        var service = CreateService(loader);

        var result = await service.RefreshAsync(
            configuration,
            Now,
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
        Assert.Equal(["Новости", "Вечер"], first.Schedule.Select(static programme => programme.Title));

        var second = result.Channels[1];
        Assert.Equal("guide.two", second.ProgrammeGuideChannelId);
        Assert.Equal("Фильм", second.CurrentProgramme?.Title);
        Assert.Null(second.NextProgramme);
        Assert.Equal("Фильм", Assert.Single(second.Schedule).Title);
    }

    [Fact]
    public async Task RefreshPlaylistAsync_exposes_remote_playlist_before_epg()
    {
        var loader = CreateLoader();
        var service = CreateService(loader);

        var result = await service.RefreshPlaylistAsync(
            CreateRemoteConfiguration(),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Channels.Count);
        Assert.Equal(["Новости", "Кино"], result.Categories);
        Assert.Equal(0, result.MatchedChannelCount);
        Assert.Equal(0, result.RetainedProgrammeCount);
        Assert.Equal(1, loader.PlaylistLoads);
        Assert.Equal(0, loader.GuideLoads);
    }

    [Fact]
    public async Task RefreshPlaylistAsync_keeps_complete_readiness_for_local_sources()
    {
        var loader = CreateLoader();
        var service = CreateService(loader);
        var configuration = new SourceConfiguration(
            SourceDefinition.Create(SourceKind.Playlist, "C:\\fixture\\playlist.m3u"),
            SourceDefinition.Create(SourceKind.ProgrammeGuide, "C:\\fixture\\guide.xml"));

        var result = await service.RefreshPlaylistAsync(
            configuration,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Channels.Count);
        Assert.Equal(2, result.MatchedChannelCount);
        Assert.Equal(0, result.RetainedProgrammeCount);
        Assert.Equal(1, loader.PlaylistLoads);
        Assert.Equal(1, loader.GuideLoads);
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
        Assert.Empty(channel.Schedule);
        Assert.Equal(["Общие"], result.Categories);
    }

    private static SourceConfiguration CreateRemoteConfiguration() =>
        new(
            SourceDefinition.Create(SourceKind.Playlist, "https://provider.example/list.m3u"),
            SourceDefinition.Create(SourceKind.ProgrammeGuide, "https://provider.example/guide.xml"));

    private static StubSourceContentLoader CreateLoader() =>
        new(
            Encoding.UTF8.GetBytes(Playlist),
            Encoding.UTF8.GetBytes(Guide));

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
        public int PlaylistLoads { get; private set; }

        public int GuideLoads { get; private set; }

        public ValueTask<LoadedSourceContent> LoadAsync(
            SourceDefinition source,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = source.Kind switch
            {
                SourceKind.Playlist => LoadPlaylist(),
                SourceKind.ProgrammeGuide when guideContent is not null => LoadGuide(),
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

        private byte[] LoadPlaylist()
        {
            PlaylistLoads++;
            return playlistContent;
        }

        private byte[] LoadGuide()
        {
            GuideLoads++;
            return guideContent!;
        }
    }
}
