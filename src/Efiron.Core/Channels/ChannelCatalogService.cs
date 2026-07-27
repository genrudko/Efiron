using Efiron.Core.Playlists;

namespace Efiron.Core.Channels;

public sealed class ChannelCatalogService
{
    public IReadOnlyList<ChannelPresentation> Build(
        IReadOnlyList<PlaylistChannel> providerChannels,
        ChannelLibrarySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(providerChannels);
        ArgumentNullException.ThrowIfNull(snapshot);

        var normalized = snapshot.Normalize();
        var candidates = providerChannels
            .Select((channel, index) => CreateCandidate(channel, index, normalized.Overrides))
            .OrderBy(static candidate => candidate.CustomOrder is null ? 1 : 0)
            .ThenBy(static candidate => candidate.CustomOrder ?? int.MaxValue)
            .ThenBy(static candidate => candidate.ProviderIndex)
            .ToArray();

        var numbers = AssignNumbers(candidates, normalized.Settings);
        var result = new ChannelPresentation[candidates.Length];
        for (var index = 0; index < candidates.Length; index++)
        {
            var candidate = candidates[index];
            result[index] = new ChannelPresentation(
                candidate.Channel,
                candidate.DisplayName,
                candidate.CategoryName,
                candidate.ProviderIndex + 1,
                index,
                numbers[index],
                candidate.Override?.IsFavorite == true,
                candidate.Override?.FavoriteOrder,
                candidate.Override?.IsHidden == true);
        }

        return result;
    }

    public IReadOnlyList<ChannelPresentation> BuildFavorites(
        IReadOnlyList<PlaylistChannel> providerChannels,
        ChannelLibrarySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var favorites = Build(providerChannels, snapshot)
            .Where(static channel => channel.IsFavorite)
            .OrderBy(static channel => channel.FavoriteOrder ?? int.MaxValue)
            .ThenBy(static channel => channel.EffectiveOrder)
            .ToArray();

        if (!snapshot.Settings.FavoritesUseIndependentNumbering)
        {
            return favorites;
        }

        return favorites
            .Select((channel, index) => channel with { Number = index + 1 })
            .ToArray();
    }

    private static Candidate CreateCandidate(
        PlaylistChannel channel,
        int providerIndex,
        IReadOnlyDictionary<string, ChannelUserOverride> overrides)
    {
        overrides.TryGetValue(channel.StableId, out var userOverride);
        userOverride = userOverride?.Normalize();

        var displayName = userOverride?.CustomName ?? channel.Name;
        var categoryName = userOverride?.CustomCategory ?? channel.GroupName;
        return new Candidate(
            channel,
            providerIndex,
            displayName,
            categoryName,
            userOverride?.CustomOrder,
            userOverride);
    }

    private static int?[] AssignNumbers(
        IReadOnlyList<Candidate> candidates,
        ChannelLibrarySettings settings)
    {
        var numbers = new int?[candidates.Count];
        switch (settings.NumberingMode)
        {
            case ChannelNumberingMode.ProviderOrder:
                for (var index = 0; index < candidates.Count; index++)
                {
                    numbers[index] = candidates[index].ProviderIndex + 1;
                }

                break;

            case ChannelNumberingMode.Continuous:
                var number = 0;
                for (var index = 0; index < candidates.Count; index++)
                {
                    if (!ParticipatesInNumbering(candidates[index], settings))
                    {
                        continue;
                    }

                    numbers[index] = ++number;
                }

                break;

            case ChannelNumberingMode.PerCategory:
                var categoryCounters = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
                for (var index = 0; index < candidates.Count; index++)
                {
                    var candidate = candidates[index];
                    if (!ParticipatesInNumbering(candidate, settings))
                    {
                        continue;
                    }

                    var category = candidate.CategoryName ?? string.Empty;
                    categoryCounters.TryGetValue(category, out var categoryNumber);
                    categoryNumber++;
                    categoryCounters[category] = categoryNumber;
                    numbers[index] = categoryNumber;
                }

                break;

            case ChannelNumberingMode.Manual:
                for (var index = 0; index < candidates.Count; index++)
                {
                    numbers[index] = candidates[index].Override?.ManualNumber;
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(settings),
                    settings.NumberingMode,
                    "Unsupported channel numbering mode.");
        }

        return numbers;
    }

    private static bool ParticipatesInNumbering(
        Candidate candidate,
        ChannelLibrarySettings settings) =>
        settings.IncludeHiddenInNumbering || candidate.Override?.IsHidden != true;

    private sealed record Candidate(
        PlaylistChannel Channel,
        int ProviderIndex,
        string DisplayName,
        string? CategoryName,
        int? CustomOrder,
        ChannelUserOverride? Override);
}
