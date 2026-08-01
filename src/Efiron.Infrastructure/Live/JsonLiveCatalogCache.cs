using System.IO.Compression;
using System.Text.Json;
using Efiron.Application.Live;
using Efiron.Application.Sources;

namespace Efiron.Infrastructure.Live;

public sealed class JsonLiveCatalogCache(string path)
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async ValueTask<LiveCatalogSnapshot?> LoadAsync(
        SourceConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var file = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                useAsync: true);
            await using var gzip = new GZipStream(
                file,
                CompressionMode.Decompress,
                leaveOpen: false);
            var envelope = await JsonSerializer.DeserializeAsync<CacheEnvelope>(
                gzip,
                JsonOptions,
                cancellationToken);
            if (envelope is null ||
                envelope.Version != CurrentVersion ||
                !Matches(configuration, envelope))
            {
                return null;
            }

            return envelope.Catalog with { CatalogCacheHit = true };
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                JsonException or
                NotSupportedException)
        {
            return null;
        }
    }

    public async ValueTask SaveAsync(
        SourceConfiguration configuration,
        LiveCatalogSnapshot catalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(catalog);
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                "The live catalog cache path must include a directory.");
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";
        var envelope = new CacheEnvelope(
            CurrentVersion,
            configuration.Playlist?.Location,
            configuration.ProgrammeGuide?.Location,
            DateTimeOffset.UtcNow,
            catalog with { CatalogCacheHit = false });

        try
        {
            await using (var file = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             useAsync: true))
            await using (var gzip = new GZipStream(
                             file,
                             CompressionLevel.Fastest,
                             leaveOpen: false))
            {
                await JsonSerializer.SerializeAsync(
                    gzip,
                    envelope,
                    JsonOptions,
                    cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool Matches(
        SourceConfiguration configuration,
        CacheEnvelope envelope) =>
        string.Equals(
            configuration.Playlist?.Location,
            envelope.PlaylistLocation,
            StringComparison.Ordinal) &&
        string.Equals(
            configuration.ProgrammeGuide?.Location,
            envelope.ProgrammeGuideLocation,
            StringComparison.Ordinal);

    private sealed record CacheEnvelope(
        int Version,
        string? PlaylistLocation,
        string? ProgrammeGuideLocation,
        DateTimeOffset SavedAtUtc,
        LiveCatalogSnapshot Catalog);
}
