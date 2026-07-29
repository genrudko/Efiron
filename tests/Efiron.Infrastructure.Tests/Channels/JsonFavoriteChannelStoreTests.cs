using Efiron.Infrastructure.Channels;
using Xunit;

namespace Efiron.Infrastructure.Tests.Channels;

public sealed class JsonFavoriteChannelStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "Efiron.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LoadAsync_returns_empty_set_when_store_is_absent()
    {
        var store = CreateStore();

        var result = await store.LoadAsync(
            TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task SaveAsync_round_trips_distinct_stable_ids_atomically()
    {
        var store = CreateStore();
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "channel-b",
            "channel-a",
        };

        await store.SaveAsync(
            expected,
            TestContext.Current.CancellationToken);
        var result = await store.LoadAsync(
            TestContext.Current.CancellationToken);

        Assert.True(expected.SetEquals(result));
        Assert.Empty(Directory.EnumerateFiles(
            _directory,
            "*.tmp",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task LoadAsync_rejects_malformed_json()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            GetPath(),
            "{not-json",
            TestContext.Current.CancellationToken);
        var store = CreateStore();

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await store.LoadAsync(
                TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private JsonFavoriteChannelStore CreateStore() => new(GetPath());

    private string GetPath() => Path.Combine(_directory, "favorites.json");
}
