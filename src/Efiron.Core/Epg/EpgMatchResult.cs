namespace Efiron.Core.Epg;

public sealed record EpgMatchResult(
    IReadOnlyDictionary<string, string> PlaylistChannelMatches,
    int ExactIdMatches,
    int UniqueNameMatches);
