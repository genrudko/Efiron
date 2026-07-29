using System.Text.Json;
using Efiron.Application.Channels;

namespace Efiron.Infrastructure.Channels;

public sealed class JsonFavoriteChannelStore(string path) : IFavoriteChannelStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public async ValueTask<IReadOnlySet<string>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(path))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            useAsync: true);

        try
        {
            var document = await JsonSerializer.DeserializeAsync<FavoriteDocument>(
                stream,
                SerializerOptions,
                cancellationToken);
            var values = document?.StableIds ?? [];
            return values
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The favorite-channel store is not valid JSON.",
                exception);
        }
    }

    public async ValueTask SaveAsync(
        IReadOnlySet<string> stableIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stableIds);
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        var document = new FavoriteDocument(
            Version: 1,
            StableIds: stableIds
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray());

        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 16 * 1024,
                             useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    SerializerOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private sealed record FavoriteDocument(
        int Version,
        IReadOnlyList<string> StableIds);
}
