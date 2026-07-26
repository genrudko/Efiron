using System.IO.Compression;
using System.Net.Http.Headers;

namespace Efiron.App.Epg;

internal sealed class RemoteEpgClient(HttpClient httpClient)
{
    private const long MaxCompressedBytes = 32L * 1024 * 1024;
    private const long MaxExpandedBytes = 256L * 1024 * 1024;

    public async Task<MemoryStream> DownloadAsync(Uri source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !source.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Only HTTP and HTTPS EPG sources are supported.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > MaxCompressedBytes)
        {
            throw new InvalidDataException("The compressed XMLTV document exceeds the allowed size.");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var downloaded = new MemoryStream();
        await CopyWithLimitAsync(
            responseStream,
            downloaded,
            MaxCompressedBytes,
            "The XMLTV download exceeds the allowed size.",
            cancellationToken);
        downloaded.Position = 0;

        if (!IsGzip(source, response.Content.Headers, downloaded))
        {
            return downloaded;
        }

        var expanded = new MemoryStream();
        try
        {
            await using var gzip = new GZipStream(downloaded, CompressionMode.Decompress, leaveOpen: false);
            await CopyWithLimitAsync(
                gzip,
                expanded,
                MaxExpandedBytes,
                "The decompressed XMLTV document exceeds the allowed size.",
                cancellationToken);
            expanded.Position = 0;
            return expanded;
        }
        catch
        {
            expanded.Dispose();
            downloaded.Dispose();
            throw;
        }
    }

    private static bool IsGzip(Uri source, HttpContentHeaders headers, Stream content)
    {
        if (headers.ContentEncoding.Any(static value => value.Equals("gzip", StringComparison.OrdinalIgnoreCase)) ||
            source.AbsolutePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!content.CanSeek || content.Length < 2)
        {
            return false;
        }

        var originalPosition = content.Position;
        var first = content.ReadByte();
        var second = content.ReadByte();
        content.Position = originalPosition;
        return first == 0x1f && second == 0x8b;
    }

    private static async Task CopyWithLimitAsync(
        Stream source,
        Stream destination,
        long limit,
        string limitMessage,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > limit)
            {
                throw new InvalidDataException(limitMessage);
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}
