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

    public int RetainedProgrammeCount =>
        Channels.Sum(static channel => channel.Schedule.Count);

    public bool CatalogCacheHit { get; init; }

    public bool PlaylistSourceCacheHit { get; init; }

    public bool ProgrammeGuideSourceCacheHit { get; init; }

    public bool ProgrammeGuideParseCacheHit { get; init; }
}
