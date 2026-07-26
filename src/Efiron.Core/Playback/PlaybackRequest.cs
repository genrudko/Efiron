namespace Efiron.Core.Playback;

public sealed record PlaybackRequest
{
    private static readonly HashSet<string> SupportedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp,
        Uri.UriSchemeHttps,
        "rtsp",
        "rtmp",
        "rtp",
        "udp",
    };

    public PlaybackRequest(Uri source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.IsAbsoluteUri)
        {
            throw new ArgumentException("Playback source must be an absolute URI.", nameof(source));
        }

        if (!SupportedSchemes.Contains(source.Scheme))
        {
            throw new NotSupportedException($"The URI scheme '{source.Scheme}' is not supported.");
        }

        Source = source;
    }

    public Uri Source { get; }
}
