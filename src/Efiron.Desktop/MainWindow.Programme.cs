using Efiron.Desktop.Views;
using Microsoft.UI.Xaml;

namespace Efiron.Desktop;

public sealed partial class MainWindow
{
    private async void ShowProgrammeWorkspace()
    {
        if (_catalog is null || _catalog.Channels.Count == 0)
        {
            if (_programmeGuideWorkspace is not null)
            {
                _programmeGuideWorkspace.Visibility = Visibility.Collapsed;
            }

            ShowSourcesWorkspace();
            return;
        }

        if (_isFullscreen)
        {
            SetFullscreen(false);
        }

        var programmeWorkspace = EnsureProgrammeGuideWorkspace();
        SourcesWorkspace.Visibility = Visibility.Collapsed;
        if (_liveTvWorkspace is not null)
        {
            _liveTvWorkspace.Visibility = Visibility.Collapsed;
        }

        programmeWorkspace.Visibility = Visibility.Visible;
        WindowContextTitle.Text = _resources.GetString(
            "WindowContextProgrammeMessage");
        UpdateShellNavigation();

        try
        {
            await programmeWorkspace.SetCatalogProgressivelyAsync(
                _catalog,
                _lifetime.Token);
            _ = CaptureEpgEvidenceAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async void ProgrammeGuideWorkspace_PlayChannelRequested(
        object? sender,
        PlayChannelRequestedEventArgs e)
    {
        if (_programmeGuideWorkspace is not null)
        {
            _programmeGuideWorkspace.Visibility = Visibility.Collapsed;
        }

        await ShowLiveWorkspaceAsync();
        if (_liveTvWorkspace is not null)
        {
            await _liveTvWorkspace.PlayChannelByStableIdAsync(e.StableId);
        }

        UpdateShellNavigation();
    }
}