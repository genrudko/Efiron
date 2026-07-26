namespace Efiron.Core.Epg;

public sealed record XmlTvProgramme(
    string ChannelId,
    DateTimeOffset Start,
    DateTimeOffset? Stop,
    string Title,
    string? Subtitle,
    string? Description,
    IReadOnlyList<string> Categories);
