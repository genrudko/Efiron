using Efiron.Domain.ProgrammeGuide;
using Microsoft.UI.Xaml;

namespace Efiron.Desktop.Presentation;

public sealed record EpgProgrammeBlockItem(
    string ChannelStableId,
    Programme Programme,
    double Left,
    double Width,
    string TimeText,
    bool IsCurrent)
{
    public string Title => Programme.Title;

    public string Description =>
        Programme.Description ?? Programme.Subtitle ?? string.Empty;

    public Visibility CurrentBadgeVisibility =>
        IsCurrent ? Visibility.Visible : Visibility.Collapsed;
}
