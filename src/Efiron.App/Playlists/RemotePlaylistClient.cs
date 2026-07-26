using System.Text;

namespace Efiron.App.Playlists;

internal sealed class RemotePlaylistClient(HttpClient httpClient)
{
    private const int MaximumPlaylistBytes = 25 * 1024 * 1024;

    public async Task<string> DownloadAsync(Uri source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !source.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Only HTTP and HTTPS playlist sources are supported.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > MaximumPlaylistBytes)
        {
            throw new InvalidDataException("The playlist exceeds the maximum supported size.");
        }

        await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var bufferStream = new MemoryStream();
        var buffer = new byte[81920];
        var totalBytes = 0;

        while (true)
        {
            var bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            totalBytes += bytesRead;
            if (totalBytes > MaximumPlaylistBytes)
            {
                throw new InvalidDataException("The playlist exceeds the maximum supported size.");
            }

            await bufferStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        return Encoding.UTF8.GetString(bufferStream.ToArray());
    }
}
