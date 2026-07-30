using Efiron.Desktop.Views;
using Microsoft.UI.Xaml;

namespace Efiron.Desktop;

public sealed partial class MainWindow
{
    private void ShowProgrammeWorkspace()
    {
        if (_catalog is null || _catalog.Channels.Count == 0)
        {
            ProgrammeGuideWorkspace.Visibility = Visibility.Collapsed;
            ShowSourcesWorkspace();
            return;
        }

        if (_isFullscreen)
        {
            SetFullscreen(false);
        }

        ProgrammeGuideWorkspace.SetCatalog(_catalog);
        SourcesWorkspace.Visibility = Visibility.Collapsed;
        LiveTvWorkspace.Visibility = Visibility.Collapsed;
        ProgrammeGuideWorkspace.Visibility = Visibility.Visible;
        WindowContextTitle.Text = _resources.GetString(
            "WindowContextProgrammeMessage");
    }

    private async void ProgrammeGuideWorkspace_PlayChannelRequested(
        object? sender,
        PlayChannelRequestedEventArgs e)
    {
        ProgrammeGuideWorkspace.Visibility = Visibility.Collapsed;
        await ShowLiveWorkspaceAsync();
        await LiveTvWorkspace.PlayChannelByStableIdAsync(e.StableId);
        UpdateShellNavigation();
    }
}
