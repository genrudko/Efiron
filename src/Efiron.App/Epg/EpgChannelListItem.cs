using Efiron.Core.Playlists;

namespace Efiron.App.Epg;

internal sealed record EpgChannelListItem(
    PlaylistChannel Channel,
    string XmlTvChannelId)
{
    public string Name => Channel.Name;
}
