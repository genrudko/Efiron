namespace Efiron.Core.Epg;

public sealed record XmlTvChannel(
    string Id,
    IReadOnlyList<string> DisplayNames,
    Uri? IconUri);
