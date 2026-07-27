using Efiron.Core.Channels;
using Efiron.Core.Playlists;

namespace Efiron.App.Epg;

internal sealed class EpgChannelListItem
{
    public EpgChannelListItem(PlaylistChannel channel, string xmlTvChannelId)
    {
        Channel = channel;
        XmlTvChannelId = xmlTvChannelId;
        Name = channel.Name;
    }

    public EpgChannelListItem(ChannelPresentation presentation, string xmlTvChannelId)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        Presentation = presentation;
        Channel = presentation.EffectiveChannel;
        XmlTvChannelId = xmlTvChannelId;
        Name = presentation.NumberedName;
    }

    public ChannelPresentation? Presentation { get; }

    public PlaylistChannel Channel { get; }

    public string XmlTvChannelId { get; }

    public string Name { get; }
}
