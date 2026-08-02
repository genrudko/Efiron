using System.Text;
using Efiron.Domain.Channels;
using Efiron.Domain.ProgrammeGuide;

namespace Efiron.Application.ProgrammeGuide;

public sealed class ProgrammeGuideChannelMatcher
{
    public ProgrammeGuideMatchResult Match(
        IReadOnlyCollection<ChannelDefinition> playlistChannels,
        IReadOnlyCollection<ProgrammeGuideChannel> programmeGuideChannels)
    {
        ArgumentNullException.ThrowIfNull(playlistChannels);
        ArgumentNullException.ThrowIfNull(programmeGuideChannels);

        var guideById = programmeGuideChannels
            .GroupBy(static channel => channel.Id, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() == 1)
            .ToDictionary(
                static group => group.Key,
                static group => group.Single(),
                StringComparer.OrdinalIgnoreCase);
        var normalizedNameIndex = BuildNameIndex(programmeGuideChannels);
        var matches = new Dictionary<string, string>(StringComparer.Ordinal);
        var exactMatches = 0;
        var nameMatches = 0;

        foreach (var playlistChannel in playlistChannels)
        {
            if (!string.IsNullOrWhiteSpace(playlistChannel.ProgrammeGuideId) &&
                guideById.TryGetValue(playlistChannel.ProgrammeGuideId, out var exactChannel))
            {
                matches[playlistChannel.StableId] = exactChannel.Id;
                exactMatches++;
                continue;
            }

            var candidateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddNameCandidates(
                candidateIds,
                normalizedNameIndex,
                playlistChannel.ProviderName);
            AddNameCandidates(
                candidateIds,
                normalizedNameIndex,
                playlistChannel.Name);

            if (candidateIds.Count == 1)
            {
                matches[playlistChannel.StableId] = candidateIds.Single();
                nameMatches++;
            }
        }

        return new ProgrammeGuideMatchResult(matches, exactMatches, nameMatches);
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

    private static Dictionary<string, HashSet<string>> BuildNameIndex(
        IEnumerable<ProgrammeGuideChannel> channels)
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
}
