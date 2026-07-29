namespace Efiron.Desktop.Views;

public sealed class FavoriteChangedEventArgs(
    string stableId,
    bool isFavorite) : EventArgs
{
    public string StableId { get; } =
        string.IsNullOrWhiteSpace(stableId)
            ? throw new ArgumentException(
                "A channel stable id is required.",
                nameof(stableId))
            : stableId;

    public bool IsFavorite { get; } = isFavorite;
}
