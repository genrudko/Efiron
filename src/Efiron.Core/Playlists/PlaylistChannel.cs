namespace Efiron.Core.Playlists;

public sealed record PlaylistChannel(
    string StableId,
    string Name,
    Uri StreamUri,
    string? TvgId,
    string? TvgName,
    Uri? LogoUri,
    string? GroupName,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyDictionary<string, string> Directives,
    int SourceLine);
