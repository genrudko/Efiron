using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Efiron.Desktop.Views;

public sealed partial class LiveTvView
{
    private const string ColdStartThemeVerificationEnvironmentVariable =
        "EFIRON_CI_INTERACTION_VERIFICATION";

    private bool _coldStartThemeEvidenceEnabled;

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

    internal void EnableColdStartThemeEvidence()
    {
        if (_coldStartThemeEvidenceEnabled ||
            !string.Equals(
                Environment.GetEnvironmentVariable(
                    ColdStartThemeVerificationEnvironmentVariable),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        _coldStartThemeEvidenceEnabled = true;
        Loaded += LiveTvView_ColdStartThemeEvidenceLoaded;
    }

    private async void LiveTvView_ColdStartThemeEvidenceLoaded(
        object sender,
        RoutedEventArgs e)
    {
        Loaded -= LiveTvView_ColdStartThemeEvidenceLoaded;

        try
        {
            // Theme propagation and ThemeResource invalidation complete after
            // the lazy workspace has joined the already-loaded WindowRoot.
            await Task.Delay(TimeSpan.FromMilliseconds(350));
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Efiron",
                "diagnostics",
                "cold-start-theme-runtime.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(GetThemeRuntimeEvidence()));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}

internal sealed record LiveThemeRuntimeEvidence(
    string WorkspaceRequestedTheme,
    string WorkspaceActualTheme,
    string RootRequestedTheme,
    string RootActualTheme,
    string RootBackgroundColor,
    DateTimeOffset RecordedAtUtc);
