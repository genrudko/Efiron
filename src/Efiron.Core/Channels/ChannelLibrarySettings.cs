namespace Efiron.Core.Channels;

public sealed record ChannelLibrarySettings(
    ChannelNumberingMode NumberingMode,
    bool IncludeHiddenInNumbering,
    bool FavoritesUseIndependentNumbering)
{
    public static ChannelLibrarySettings Default { get; } = new(
        ChannelNumberingMode.Continuous,
        IncludeHiddenInNumbering: false,
        FavoritesUseIndependentNumbering: true);
}
