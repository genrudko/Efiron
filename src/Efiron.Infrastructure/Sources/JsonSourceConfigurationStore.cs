using System.Text.Json;
using Efiron.Application.Sources;
using Efiron.Domain.Sources;

namespace Efiron.Infrastructure.Sources;

public sealed class JsonSourceConfigurationStore(string filePath)
    : ISourceConfigurationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _filePath = string.IsNullOrWhiteSpace(filePath)
        ? throw new ArgumentException(
            "A source configuration path is required.",
            nameof(filePath))
        : Path.GetFullPath(filePath);

    public async ValueTask<SourceConfiguration> LoadAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return SourceConfiguration.Empty;
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var document = await JsonSerializer.DeserializeAsync<StoredConfiguration>(
                stream,
                SerializerOptions,
                cancellationToken);

            if (document is null)
            {
                throw new InvalidDataException(
                    "The source configuration file is empty.");
            }

            return new SourceConfiguration(
                CreateSource(SourceKind.Playlist, document.Playlist),
                CreateSource(SourceKind.ProgrammeGuide, document.ProgrammeGuide));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The source configuration file contains invalid JSON.",
                exception);
        }
    }

    public async ValueTask SaveAsync(
        SourceConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var stored = new StoredConfiguration(
            ToStoredSource(configuration.Playlist),
            ToStoredSource(configuration.ProgrammeGuide));
        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    stored,
                    SerializerOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static SourceDefinition? CreateSource(
        SourceKind kind,
        StoredSource? source) =>
        source is null
            ? null
            : SourceDefinition.Create(
                kind,
                source.Location,
                source.IsEnabled);

    private static StoredSource? ToStoredSource(SourceDefinition? source) =>
        source is null
            ? null
            : new StoredSource(source.Location, source.IsEnabled);

    private sealed record StoredConfiguration(
        StoredSource? Playlist,
        StoredSource? ProgrammeGuide);

    private sealed record StoredSource(
        string Location,
        bool IsEnabled);
}
