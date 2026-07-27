using Efiron.Core.Channels;
using Efiron.Core.Playlists;

namespace Efiron.App.Playlists;

internal sealed class ChannelListItem
{
    public ChannelListItem(PlaylistChannel channel, string groupDisplayName)
    {
        Channel = channel;
        Name = channel.Name;
        DisplayName = channel.Name;
        GroupDisplayName = groupDisplayName;
    }

    public ChannelListItem(
        ChannelPresentation presentation,
        string groupDisplayName,
        string hiddenLabel)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        Presentation = presentation;
        Channel = presentation.ProviderChannel;
        Name = presentation.NumberedName;
        DisplayName = presentation.DisplayName;
        GroupDisplayName = string.Concat(
            presentation.IsFavorite ? "★ " : string.Empty,
            groupDisplayName,
            presentation.IsHidden ? $" • {hiddenLabel}" : string.Empty);
    }

    public ChannelPresentation? Presentation { get; }

    public PlaylistChannel Channel { get; }

    public string Name { get; }

    public string DisplayName { get; }

    public string GroupDisplayName { get; }

    public bool IsFavorite => Presentation?.IsFavorite == true;

    public bool IsHidden => Presentation?.IsHidden == true;

    public string StreamAddress => Channel.StreamUri.AbsoluteUri;
}
