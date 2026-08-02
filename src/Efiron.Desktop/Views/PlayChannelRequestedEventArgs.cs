namespace Efiron.Desktop.Views;

public sealed class PlayChannelRequestedEventArgs(string stableId) : EventArgs
{
    public string StableId { get; } =
        string.IsNullOrWhiteSpace(stableId)
            ? throw new ArgumentException("Stable channel ID is required.", nameof(stableId))
            : stableId;
}
