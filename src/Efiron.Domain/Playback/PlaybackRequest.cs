namespace Efiron.Domain.Playback;

public sealed record PlaybackRequest
{
    private static readonly HashSet<string> SupportedSchemes = new(
        StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp,
        Uri.UriSchemeHttps,
        "rtsp",
        "rtmp",
        "rtp",
        "udp",
    };

    public PlaybackRequest(
        Uri source,
        string? channelStableId = null,
        string? displayName = null,
        IReadOnlyDictionary<string, string>? directives = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.IsAbsoluteUri)
        {
            throw new ArgumentException(
                "Playback source must be an absolute URI.",
                nameof(source));
        }

        if (!SupportedSchemes.Contains(source.Scheme))
        {
            throw new NotSupportedException(
                $"The URI scheme '{source.Scheme}' is not supported.");
        }

        Source = source;
        ChannelStableId = NullIfWhiteSpace(channelStableId);
        DisplayName = NullIfWhiteSpace(displayName);
        Directives = directives is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(directives, StringComparer.OrdinalIgnoreCase);
    }

    public Uri Source { get; }

    public string? ChannelStableId { get; }

    public string? DisplayName { get; }

    public IReadOnlyDictionary<string, string> Directives { get; }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
