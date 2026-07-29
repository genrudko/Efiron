using Efiron.Application.Sources;
using Efiron.Domain.Sources;
using Efiron.Infrastructure.Sources;
using Xunit;

namespace Efiron.Infrastructure.Tests.Sources;

public sealed class JsonSourceConfigurationStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "Efiron.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Load_returns_empty_configuration_when_file_is_absent()
    {
        var store = CreateStore();

        var configuration = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(SourceConfiguration.Empty, configuration);
    }

    [Fact]
    public async Task Save_and_load_round_trip_sources_atomically()
    {
        var path = GetConfigurationPath();
        var store = new JsonSourceConfigurationStore(path);
        var expected = new SourceConfiguration(
            SourceDefinition.Create(
                SourceKind.Playlist,
                "https://provider.example/playlist.m3u"),
            SourceDefinition.Create(
                SourceKind.ProgrammeGuide,
                "https://provider.example/guide.xml.gz"));

        await store.SaveAsync(expected, CancellationToken.None);
        var actual = await store.LoadAsync(CancellationToken.None);

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
        var path = GetConfigurationPath();
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(path, "{not-json");
        var store = new JsonSourceConfigurationStore(path);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await store.LoadAsync(CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private JsonSourceConfigurationStore CreateStore() =>
        new(GetConfigurationPath());

    private string GetConfigurationPath() =>
        Path.Combine(_directory, "sources.json");
}
