using Efiron.Domain.Appearance;
using Efiron.Infrastructure.Appearance;
using Xunit;

namespace Efiron.Infrastructure.Tests.Appearance;

public sealed class JsonAppearancePreferencesStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "Efiron.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Load_returns_system_blue_when_file_is_absent()
    {
        var store = CreateStore();

        var preferences = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AppearancePreferences.Default, preferences);
    }

    [Fact]
    public async Task Save_and_load_round_trip_theme_and_accent_atomically()
    {
        var path = GetPreferencesPath();
        var store = new JsonAppearancePreferencesStore(path);
        var expected = new AppearancePreferences(
            AppearanceTheme.Dark,
            AccentPalette.Violet);

        await store.SaveAsync(expected, TestContext.Current.CancellationToken);
        var actual = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, actual);
        Assert.True(File.Exists(path));
        Assert.Empty(Directory.EnumerateFiles(
            _directory,
            "*.tmp",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task Load_rejects_malformed_json()
    {
        var path = GetPreferencesPath();
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            path,
            "{not-json",
            TestContext.Current.CancellationToken);
        var store = new JsonAppearancePreferencesStore(path);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await store.LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Load_rejects_unsupported_enum_values()
    {
        var path = GetPreferencesPath();
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            path,
            """
            {
              "version": 1,
              "theme": 99,
              "accent": 99
            }
            """,
            TestContext.Current.CancellationToken);
        var store = new JsonAppearancePreferencesStore(path);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await store.LoadAsync(TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private JsonAppearancePreferencesStore CreateStore() =>
        new(GetPreferencesPath());

    private string GetPreferencesPath() =>
        Path.Combine(_directory, "appearance.json");
}
