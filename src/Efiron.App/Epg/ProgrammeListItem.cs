using Efiron.Core.Epg;

namespace Efiron.App.Epg;

internal sealed record ProgrammeListItem(
    XmlTvProgramme Programme,
    string TimeRange,
    string Title,
    string Subtitle,
    string Categories,
    string Description);
