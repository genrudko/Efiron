namespace Efiron.Core.Channels;

public sealed record ChannelUserOverride(
    string StableId,
    string? CustomName,
    int? ManualNumber,
    bool IsFavorite,
    int? FavoriteOrder,
    bool IsHidden,
    string? CustomCategory,
    int? CustomOrder)
{
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(CustomName) &&
        ManualNumber is null &&
        !IsFavorite &&
        FavoriteOrder is null &&
        !IsHidden &&
        string.IsNullOrWhiteSpace(CustomCategory) &&
        CustomOrder is null;

    public ChannelUserOverride Normalize()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(StableId);

        return this with
        {
            CustomName = NormalizeText(CustomName),
            ManualNumber = ManualNumber is > 0 ? ManualNumber : null,
            FavoriteOrder = IsFavorite && FavoriteOrder is > 0 ? FavoriteOrder : null,
            CustomCategory = NormalizeText(CustomCategory),
            CustomOrder = CustomOrder is >= 0 ? CustomOrder : null,
        };
    }

    private static string? NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
