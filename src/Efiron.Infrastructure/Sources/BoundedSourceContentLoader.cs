using System.Net;
using System.Net.Http.Headers;
using Efiron.Application.Sources;
using Efiron.Domain.Sources;

namespace Efiron.Infrastructure.Sources;

public sealed class BoundedSourceContentLoader(HttpClient httpClient) : ISourceContentLoader
{
    public const int MaximumPlaylistBytes = 32 * 1024 * 1024;
    public const int MaximumProgrammeGuidePayloadBytes = 64 * 1024 * 1024;

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
            return await LoadRemoteAsync(
                source,
                remoteUri,
                maximumBytes,
                cancellationToken);
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
            contentType: null,
            DateTimeOffset.UtcNow);
    }

    private async ValueTask<LoadedSourceContent> LoadRemoteAsync(
        SourceDefinition source,
        Uri uri,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        request.Headers.UserAgent.ParseAdd("Efiron/greenfield");

        using var response = await httpClient.SendAsync(
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
            DateTimeOffset.UtcNow);
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
