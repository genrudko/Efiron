namespace Efiron.Core.Playlists;

public sealed record PlaylistParseResult(
    IReadOnlyList<PlaylistChannel> Channels,
    IReadOnlyDictionary<string, string> HeaderAttributes,
    IReadOnlyList<PlaylistParseWarning> Warnings);
