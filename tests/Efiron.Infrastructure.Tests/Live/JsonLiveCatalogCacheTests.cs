using Efiron.Application.Live;
using Efiron.Application.Sources;
using Efiron.Domain.Channels;
using Efiron.Domain.ProgrammeGuide;
using Efiron.Domain.Sources;
using Efiron.Infrastructure.Live;
using Xunit;

namespace Efiron.Infrastructure.Tests.Live;

public sealed class JsonLiveCatalogCacheTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "Efiron.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Save_and_load_restore_the_last_known_good_catalog()
    {
        var path = Path.Combine(_directory, "live-catalog.json.gz");
        var cache = new JsonLiveCatalogCache(path);
        var configuration = CreateConfiguration("one");
        var programme = new Programme(
            "guide.one",
            new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 1, 11, 0, 0, TimeSpan.Zero),
            "Programme",
            null,
            "Description",
            ["News"]);
        var channel = new ChannelDefinition(
            "channel.one",
            "Channel One",
            new Uri("https://media.example/live.m3u8"),
            "guide.one",
            null,
            null,
            "News",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            1);
        var snapshot = new LiveChannelSnapshot(
            channel,
            "guide.one",
            programme,
            null)
        {
            Schedule = [programme],
        };
        var catalog = new LiveCatalogSnapshot(
            [snapshot],
            ["News"],
            [],
            [],
            1,
            0,
            DateTimeOffset.UtcNow);

        await cache.SaveAsync(
            configuration,
            catalog,
            TestContext.Current.CancellationToken);
        var restored = await cache.LoadAsync(
            configuration,
            TestContext.Current.CancellationToken);

        Assert.NotNull(restored);
        Assert.True(restored.CatalogCacheHit);
        Assert.Equal("Channel One", Assert.Single(restored.Channels).Channel.Name);
        Assert.Equal("Programme", Assert.Single(restored.Channels[0].Schedule).Title);
    }

    [Fact]
    public async Task Load_rejects_a_cache_for_different_sources()
    {
        var cache = new JsonLiveCatalogCache(
            Path.Combine(_directory, "live-catalog.json.gz"));
        var catalog = new LiveCatalogSnapshot([], [], [], [], 0, 0, DateTimeOffset.UtcNow);

        await cache.SaveAsync(
            CreateConfiguration("one"),
            catalog,
            TestContext.Current.CancellationToken);
        var restored = await cache.LoadAsync(
            CreateConfiguration("two"),
            TestContext.Current.CancellationToken);

        Assert.Null(restored);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static SourceConfiguration CreateConfiguration(string suffix) =>
        new(
            SourceDefinition.Create(
                SourceKind.Playlist,
                $"https://provider.example/{suffix}/playlist.m3u"),
            SourceDefinition.Create(
                SourceKind.ProgrammeGuide,
                $"https://provider.example/{suffix}/guide.xml"));
}
