using Efiron.Domain.Channels;

namespace Efiron.Domain.Playlists;

public sealed record PlaylistDocument(
    IReadOnlyList<ChannelDefinition> Channels,
    IReadOnlyDictionary<string, string> HeaderAttributes,
    IReadOnlyList<PlaylistParseWarning> Warnings);
