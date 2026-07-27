using System.Globalization;
using Efiron.Core.Playlists;

namespace Efiron.Core.Channels;

public sealed record ChannelPresentation(
    PlaylistChannel ProviderChannel,
    string DisplayName,
    string? CategoryName,
    int ProviderOrder,
    int EffectiveOrder,
    int? Number,
    bool IsFavorite,
    int? FavoriteOrder,
    bool IsHidden)
{
    public string NumberText => Number?.ToString("000", CultureInfo.InvariantCulture) ?? string.Empty;

    public string NumberedName => Number is null
        ? DisplayName
        : string.Create(
            CultureInfo.InvariantCulture,
            $"{Number.Value:000}  {DisplayName}");

    public PlaylistChannel EffectiveChannel => ProviderChannel with
    {
        Name = DisplayName,
        GroupName = CategoryName,
    };
}
