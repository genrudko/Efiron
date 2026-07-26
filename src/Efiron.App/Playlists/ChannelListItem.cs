using Efiron.Core.Playlists;

namespace Efiron.App.Playlists;

internal sealed class ChannelListItem(PlaylistChannel channel, string groupDisplayName)
{
    public PlaylistChannel Channel { get; } = channel;

    public string Name => Channel.Name;

    public string GroupDisplayName { get; } = groupDisplayName;

    public string StreamAddress => Channel.StreamUri.AbsoluteUri;
}
