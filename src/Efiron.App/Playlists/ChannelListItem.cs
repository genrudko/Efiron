using Efiron.Core.Channels;
using Efiron.Core.Playlists;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Efiron.App.Playlists;

internal sealed class ChannelListItem
{
    public ChannelListItem(PlaylistChannel channel, string groupDisplayName)
    {
        Channel = channel;
        Name = channel.Name;
        DisplayName = channel.Name;
        GroupDisplayName = groupDisplayName;
        LogoImage = CreateLogoImage(channel.LogoUri);
        LogoFallback = CreateLogoFallback(DisplayName);
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
        LogoImage = CreateLogoImage(Channel.LogoUri);
        LogoFallback = CreateLogoFallback(DisplayName);
    }

    public ChannelPresentation? Presentation { get; }

    public PlaylistChannel Channel { get; }

    public string Name { get; }

    public string DisplayName { get; }

    public string GroupDisplayName { get; }

    public bool IsFavorite => Presentation?.IsFavorite == true;

    public bool IsHidden => Presentation?.IsHidden == true;

    public string StreamAddress => Channel.StreamUri.AbsoluteUri;

    public string NumberText => Presentation?.NumberText ?? string.Empty;

    public BitmapImage? LogoImage { get; }

    public string LogoFallback { get; }

    public string CurrentProgrammeTitle { get; private set; } = string.Empty;

    public string CurrentProgrammeTime { get; private set; } = string.Empty;

    public string FavoriteGlyph => IsFavorite ? "\uE735" : string.Empty;

    public string PlayingGlyph { get; private set; } = string.Empty;

    internal void ApplyProgramme(string? title, string? timeRange)
    {
        CurrentProgrammeTitle = title ?? string.Empty;
        CurrentProgrammeTime = timeRange ?? string.Empty;
    }

    internal void SetPlaying(bool isPlaying) =>
        PlayingGlyph = isPlaying ? "\uE9D9" : string.Empty;

    private static BitmapImage? CreateLogoImage(Uri? logoUri)
    {
        if (logoUri is null)
        {
            return null;
        }

        try
        {
            return new BitmapImage(logoUri);
        }
        catch (Exception exception) when (exception is ArgumentException or UriFormatException)
        {
            return null;
        }
    }

    private static string CreateLogoFallback(string name)
    {
        var first = name
            .Trim()
            .EnumerateRunes()
            .FirstOrDefault();
        return first.Value == 0
            ? "TV"
            : first.ToString().ToUpperInvariant();
    }
}
