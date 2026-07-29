using Efiron.Domain.Playlists;
using Efiron.Domain.ProgrammeGuide;

namespace Efiron.Application.Live;

public sealed record LiveCatalogSnapshot(
    IReadOnlyList<LiveChannelSnapshot> Channels,
    IReadOnlyList<string> Categories,
    IReadOnlyList<PlaylistParseWarning> PlaylistWarnings,
    IReadOnlyList<ProgrammeGuideParseWarning> ProgrammeGuideWarnings,
    int ProgrammeGuideExactMatches,
    int ProgrammeGuideNameMatches,
    DateTimeOffset RefreshedAtUtc)
{
    public int MatchedChannelCount =>
        ProgrammeGuideExactMatches + ProgrammeGuideNameMatches;
}
