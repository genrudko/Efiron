namespace Efiron.Core.Channels;

public sealed record ChannelLibrarySnapshot(
    int Version,
    ChannelLibrarySettings Settings,
    IReadOnlyDictionary<string, ChannelUserOverride> Overrides)
{
    public const int CurrentVersion = 1;

    public static ChannelLibrarySnapshot Empty { get; } = new(
        CurrentVersion,
        ChannelLibrarySettings.Default,
        new Dictionary<string, ChannelUserOverride>(StringComparer.Ordinal));

    public ChannelLibrarySnapshot Normalize()
    {
        var normalized = new Dictionary<string, ChannelUserOverride>(StringComparer.Ordinal);
        foreach (var pair in Overrides ?? new Dictionary<string, ChannelUserOverride>())
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null)
            {
                continue;
            }

            var item = pair.Value with { StableId = pair.Key };
            item = item.Normalize();
            if (!item.IsEmpty)
            {
                normalized[pair.Key] = item;
            }
        }

        return new ChannelLibrarySnapshot(
            CurrentVersion,
            Settings ?? ChannelLibrarySettings.Default,
            normalized);
    }
}
