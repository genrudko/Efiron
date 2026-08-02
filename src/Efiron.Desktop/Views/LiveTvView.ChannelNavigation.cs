namespace Efiron.Desktop.Views;

public sealed partial class LiveTvView
{
    public async Task<bool> PlayChannelByStableIdAsync(string stableId)
    {
        if (string.IsNullOrWhiteSpace(stableId))
        {
            return false;
        }

        var item = _allItems.FirstOrDefault(candidate => string.Equals(
            candidate.Snapshot.Channel.StableId,
            stableId,
            StringComparison.Ordinal));
        if (item is null)
        {
            return false;
        }

        await SelectChannelAsync(item);
        return true;
    }
}
