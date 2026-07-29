namespace Efiron.Application.ProgrammeGuide;

public sealed record ProgrammeGuideMatchResult(
    IReadOnlyDictionary<string, string> ProgrammeGuideChannelByStableId,
    int ExactIdMatches,
    int UniqueNameMatches)
{
    public int TotalMatches => ProgrammeGuideChannelByStableId.Count;
}
