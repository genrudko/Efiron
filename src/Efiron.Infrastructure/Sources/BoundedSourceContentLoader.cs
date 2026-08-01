using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Efiron.Application.Sources;
using Efiron.Domain.Sources;

namespace Efiron.Infrastructure.Sources;

public sealed class BoundedSourceContentLoader
    : ISourceContentLoader
{
    public const int MaximumPlaylistBytes = 32 * 1024 * 1024;
    public const int MaximumProgrammeGuidePayloadBytes = 64 * 1024 * 1024;

    private static readonly ConcurrentDictionary<string, byte> ActiveRefreshes =
        new(StringComparer.Ordinal);

    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly bool _refreshCachedSourcesInBackground;

    public BoundedSourceContentLoader(
        HttpClient httpClient,
        string? cacheDirectory = null,
        bool refreshCachedSourcesInBackground = true)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Efiron",
            "source-cache");
        _refreshCachedSourcesInBackground = refreshCachedSourcesInBackground;
    }

    public async ValueTask<LoadedSourceContent> LoadAsync(
        SourceDefinition source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        var maximumBytes = source.Kind == SourceKind.Playlist
            ? MaximumPlaylistBytes
            : MaximumProgrammeGuidePayloadBytes;

        if (TryResolveRemoteUri(source.Location, out var remoteUri))
        {
            var cachePath = GetCachePath(source, remoteUri);
            var cached = await TryLoadCachedAsync(
                source,
                remoteUri,
                cachePath,
                maximumBytes,
                cancellationToken);
            if (cached is not null)
            {
                if (_refreshCachedSourcesInBackground)
                {
                    QueueBackgroundRefresh(
                        source,
                        remoteUri,
                        cachePath,
                        maximumBytes);
                }

                return cached;
            }

            var downloaded = await DownloadRemoteAsync(
                source,
                remoteUri,
                maximumBytes,
                cancellationToken);
            await SaveCacheAsync(
                cachePath,
                downloaded.Content,
                cancellationToken);
            return downloaded;
        }

        var path = ResolveLocalPath(source.Location);
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        var content = await ReadBoundedAsync(
            stream,
            maximumBytes,
            cancellationToken);

        if (content.Length == 0)
        {
            throw new InvalidDataException(
                $"Source file '{path}' is empty.");
        }

        return new LoadedSourceContent(
            source,
            content,
            new Uri(path),
            ContentType: null,
            DateTimeOffset.UtcNow,
            IsCacheHit: false);
    }

    private async ValueTask<LoadedSourceContent?> TryLoadCachedAsync(
        SourceDefinition source,
        Uri uri,
        string cachePath,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(cachePath))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                cachePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                useAsync: true);
            var content = await ReadBoundedAsync(
                stream,
                maximumBytes,
                cancellationToken);
            if (content.Length == 0)
            {
                return null;
            }

            return new LoadedSourceContent(
                source,
                content,
                uri,
                ContentType: null,
                new DateTimeOffset(File.GetLastWriteTimeUtc(cachePath), TimeSpan.Zero),
                IsCacheHit: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    private void QueueBackgroundRefresh(
        SourceDefinition source,
        Uri uri,
        string cachePath,
        int maximumBytes)
    {
        if (!ActiveRefreshes.TryAdd(cachePath, 0))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var downloaded = await DownloadRemoteAsync(
                    source,
                    uri,
                    maximumBytes,
                    CancellationToken.None);
                await SaveCacheAsync(
                    cachePath,
                    downloaded.Content,
                    CancellationToken.None);
            }
            catch (Exception exception) when (
                exception is HttpRequestException or
                    IOException or
                    UnauthorizedAccessException or
                    InvalidDataException or
                    ObjectDisposedException or
                    TaskCanceledException)
            {
            }
            finally
            {
                ActiveRefreshes.TryRemove(cachePath, out _);
            }
        });
    }

    private async ValueTask<LoadedSourceContent> DownloadRemoteAsync(
        SourceDefinition source,
        Uri uri,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        request.Headers.UserAgent.ParseAdd("Efiron/greenfield");

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode is HttpStatusCode.NoContent)
        {
            throw new InvalidDataException(
                $"Source '{uri}' returned an empty response.");
        }

        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength.HasValue && contentLength.Value > maximumBytes)
        {
            throw new InvalidDataException(
                $"Source '{uri}' exceeds the {maximumBytes} byte payload limit.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        var content = await ReadBoundedAsync(
            stream,
            maximumBytes,
            cancellationToken);

        if (content.Length == 0)
        {
            throw new InvalidDataException(
                $"Source '{uri}' returned an empty response.");
        }

        return new LoadedSourceContent(
            source,
            content,
            response.RequestMessage?.RequestUri ?? uri,
            response.Content.Headers.ContentType?.MediaType,
            DateTimeOffset.UtcNow,
            IsCacheHit: false);
    }

    private async ValueTask SaveCacheAsync(
        string cachePath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_cacheDirectory);
        var temporaryPath = cachePath + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(
                temporaryPath,
                content.ToArray(),
                cancellationToken);
            File.Move(temporaryPath, cachePath, overwrite: true);
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

    private string GetCachePath(SourceDefinition source, Uri uri)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)));
        return Path.Combine(
            _cacheDirectory,
            $"{source.Kind.ToString().ToLowerInvariant()}-{hash}.bin");
    }

    private static bool TryResolveRemoteUri(
        string location,
        out Uri uri)
    {
        if (Uri.TryCreate(location, UriKind.Absolute, out var candidate))
        {
            if (candidate.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                candidate.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                uri = candidate;
                return true;
            }

            if (!candidate.IsFile)
            {
                throw new NotSupportedException(
                    $"Source URI scheme '{candidate.Scheme}' is not supported.");
            }
        }

        uri = null!;
        return false;
    }

    private static string ResolveLocalPath(string location)
    {
        if (Uri.TryCreate(location, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            return Path.GetFullPath(uri.LocalPath);
        }

        return Path.GetFullPath(location);
    }

    private static async ValueTask<byte[]> ReadBoundedAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        using var output = new MemoryStream();

        while (true)
        {
            var count = await stream.ReadAsync(buffer, cancellationToken);
            if (count == 0)
            {
                break;
            }

            if (output.Length + count > maximumBytes)
            {
                throw new InvalidDataException(
                    $"Source content exceeds the {maximumBytes} byte payload limit.");
            }

            output.Write(buffer, 0, count);
        }

        return output.ToArray();
    }
}
