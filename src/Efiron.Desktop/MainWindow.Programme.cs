using Efiron.Application.Live;
using Efiron.Desktop.Views;
using Microsoft.UI.Xaml;

namespace Efiron.Desktop;

public sealed partial class MainWindow
{
    private async void ShowProgrammeWorkspace()
    {
        if (_catalog is null || _catalog.Channels.Count == 0)
        {
            ReleaseProgrammeGuideWorkspace();
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

        LiveCatalogSnapshot programmeCatalog = _catalog;
        try
        {
            var configuration = await _sourceConfigurationService.LoadAsync(
                _lifetime.Token);
            var cachedProgrammeCatalog = await _programmeGuideCatalogCache.LoadAsync(
                configuration,
                _lifetime.Token);
            if (cachedProgrammeCatalog is
                {
                    Channels.Count: > 0,
                    RetainedProgrammeCount: > 0,
                })
            {
                programmeCatalog = cachedProgrammeCatalog;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            // The lightweight Live catalogue remains usable while a full
            // background EPG catalogue is unavailable or being replaced.
        }

        try
        {
            await programmeWorkspace.SetCatalogProgressivelyAsync(
                programmeCatalog,
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
        ReleaseProgrammeGuideWorkspace();

        await ShowLiveWorkspaceAsync();
        if (_liveTvWorkspace is not null)
        {
            await _liveTvWorkspace.PlayChannelByStableIdAsync(e.StableId);
        }

        UpdateShellNavigation();
    }
}
