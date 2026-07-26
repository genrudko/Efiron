using System.Text;
using Efiron.Core.Playlists;

namespace Efiron.Core.Epg;

public sealed class EpgChannelMatcher
{
    public EpgMatchResult Match(
        IReadOnlyCollection<PlaylistChannel> playlistChannels,
        IReadOnlyCollection<XmlTvChannel> xmlTvChannels)
    {
        ArgumentNullException.ThrowIfNull(playlistChannels);
        ArgumentNullException.ThrowIfNull(xmlTvChannels);

        var xmlById = xmlTvChannels
            .GroupBy(static channel => channel.Id, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() == 1)
            .ToDictionary(static group => group.Key, static group => group.Single(), StringComparer.OrdinalIgnoreCase);

        var normalizedNameIndex = BuildNameIndex(xmlTvChannels);
        var matches = new Dictionary<string, string>(StringComparer.Ordinal);
        var exactMatches = 0;
        var nameMatches = 0;

        foreach (var playlistChannel in playlistChannels)
        {
            if (!string.IsNullOrWhiteSpace(playlistChannel.TvgId) &&
                xmlById.TryGetValue(playlistChannel.TvgId, out var exactChannel))
            {
                matches[playlistChannel.StableId] = exactChannel.Id;
                exactMatches++;
                continue;
            }

            var candidateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddNameCandidates(candidateIds, normalizedNameIndex, playlistChannel.TvgName);
            AddNameCandidates(candidateIds, normalizedNameIndex, playlistChannel.Name);

            if (candidateIds.Count == 1)
            {
                matches[playlistChannel.StableId] = candidateIds.Single();
                nameMatches++;
            }
        }

        return new EpgMatchResult(matches, exactMatches, nameMatches);
    }

    private static Dictionary<string, HashSet<string>> BuildNameIndex(
        IEnumerable<XmlTvChannel> channels)
    {
        var index = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var channel in channels)
        {
            foreach (var displayName in channel.DisplayNames)
            {
                var normalized = NormalizeName(displayName);
                if (normalized.Length == 0)
                {
                    continue;
                }

                if (!index.TryGetValue(normalized, out var channelIds))
                {
                    channelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    index.Add(normalized, channelIds);
                }

                channelIds.Add(channel.Id);
            }
        }

        return index;
    }

    private static void AddNameCandidates(
        ISet<string> candidateIds,
        IReadOnlyDictionary<string, HashSet<string>> nameIndex,
        string? sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return;
        }

        var normalized = NormalizeName(sourceName);
        if (normalized.Length == 0 ||
            !nameIndex.TryGetValue(normalized, out var matchedIds) ||
            matchedIds.Count != 1)
        {
            return;
        }

        candidateIds.Add(matchedIds.Single());
    }

    internal static string NormalizeName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString();
    }
}
