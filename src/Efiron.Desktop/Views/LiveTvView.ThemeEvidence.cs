using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Efiron.Desktop.Views;

public sealed partial class LiveTvView
{
    internal LiveThemeRuntimeEvidence GetThemeRuntimeEvidence()
    {
        var backgroundColor = LiveRoot.Background is SolidColorBrush background
            ? background.Color.ToString()
            : string.Empty;
        return new LiveThemeRuntimeEvidence(
            RequestedTheme.ToString(),
            ActualTheme.ToString(),
            LiveRoot.RequestedTheme.ToString(),
            LiveRoot.ActualTheme.ToString(),
            backgroundColor,
            DateTimeOffset.UtcNow);
    }
}

internal sealed record LiveThemeRuntimeEvidence(
    string WorkspaceRequestedTheme,
    string WorkspaceActualTheme,
    string RootRequestedTheme,
    string RootActualTheme,
    string RootBackgroundColor,
    DateTimeOffset RecordedAtUtc);
