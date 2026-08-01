using Efiron.Application.Live;
using Efiron.Application.Sources;
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

        var programmeCatalog = await LoadProgrammeCatalogAsync();
        if (programmeCatalog is null)
        {
            programmeCatalog = _catalog;
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

    private async Task<LiveCatalogSnapshot?> LoadProgrammeCatalogAsync()
    {
        SourceConfiguration configuration;
        try
        {
            configuration = await _sourceConfigurationService.LoadAsync(
                _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var cached = await TryLoadProgrammeCatalogAsync(configuration);
        if (cached is not null)
        {
            return cached;
        }

        await StartOrGetBackgroundCatalogRefresh(
            configuration,
            requireProgrammeGuide: true);
        return await TryLoadProgrammeCatalogAsync(configuration);
    }

    private async Task<LiveCatalogSnapshot?> TryLoadProgrammeCatalogAsync(
        SourceConfiguration configuration)
    {
        try
        {
            var cachedProgrammeCatalog =
                await _programmeGuideCatalogCache.LoadAsync(
                    configuration,
                    _lifetime.Token);
            return cachedProgrammeCatalog is
            {
                Channels.Count: > 0,
                RetainedProgrammeCount: > 0,
            }
                ? cachedProgrammeCatalog
                : null;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return null;
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
