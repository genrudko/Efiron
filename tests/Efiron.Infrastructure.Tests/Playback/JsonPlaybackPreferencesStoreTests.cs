using Efiron.Domain.Playback;
using Efiron.Infrastructure.Playback;
using Xunit;

namespace Efiron.Infrastructure.Tests.Playback;

public sealed class JsonPlaybackPreferencesStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "Efiron.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LoadAsync_returns_defaults_when_store_is_absent()
    {
        var store = CreateStore();

        var result = await store.LoadAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(PlaybackPreferences.Default, result);
    }

    [Fact]
    public async Task SaveAsync_round_trips_selected_channel_volume_and_mute()
    {
        var store = CreateStore();
        var expected = new PlaybackPreferences(
            "m3u:stable-channel",
            37,
            isMuted: true);

        await store.SaveAsync(
            expected,
            TestContext.Current.CancellationToken);
        var result = await store.LoadAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result);
        Assert.Empty(Directory.EnumerateFiles(
            _directory,
            "*.tmp",
            SearchOption.TopDirectoryOnly));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task LoadAsync_rejects_invalid_volume(int volume)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            GetPath(),
            $$"""
            {
              "version": 1,
              "selectedChannelStableId": "channel",
              "volume": {{volume}},
              "isMuted": false
            }
            """,
            TestContext.Current.CancellationToken);
        var store = CreateStore();

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await store.LoadAsync(
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadAsync_rejects_unknown_document_version()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            GetPath(),
            """
            {
              "version": 99,
              "selectedChannelStableId": null,
              "volume": 100,
              "isMuted": false
            }
            """,
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

    private JsonPlaybackPreferencesStore CreateStore() => new(GetPath());

    private string GetPath() => Path.Combine(_directory, "playback.json");
}
