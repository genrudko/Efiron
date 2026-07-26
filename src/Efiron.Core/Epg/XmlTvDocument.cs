namespace Efiron.Core.Epg;

public sealed record XmlTvDocument(
    IReadOnlyList<XmlTvChannel> Channels,
    IReadOnlyList<XmlTvProgramme> Programmes,
    IReadOnlyList<XmlTvParseWarning> Warnings);
