using Efiron.Desktop.Views;
using Microsoft.UI.Xaml;

namespace Efiron.Desktop;

public sealed partial class MainWindow
{
    private LiveTvView? _liveTvWorkspace;
    private ProgrammeGuideView? _programmeGuideWorkspace;

    private LiveTvView LiveTvWorkspace => EnsureLiveTvWorkspace();

    private ProgrammeGuideView ProgrammeGuideWorkspace =>
        EnsureProgrammeGuideWorkspace();

    private bool IsLiveWorkspaceCreated => _liveTvWorkspace is not null;

    private bool IsProgrammeGuideWorkspaceCreated =>
        _programmeGuideWorkspace is not null;

    private bool IsLiveWorkspaceVisible =>
        _liveTvWorkspace?.Visibility == Visibility.Visible;

    private bool IsProgrammeGuideWorkspaceVisible =>
        _programmeGuideWorkspace?.Visibility == Visibility.Visible;

    private LiveTvView EnsureLiveTvWorkspace()
    {
        if (_liveTvWorkspace is not null)
        {
            return _liveTvWorkspace;
        }

        var workspace = new LiveTvView
        {
            Visibility = Visibility.Collapsed,
        };
        workspace.BackRequested += LiveTvWorkspace_BackRequested;
        workspace.FullscreenToggleRequested +=
            LiveTvWorkspace_FullscreenToggleRequested;
        workspace.FavoriteChanged += LiveTvWorkspace_FavoriteChanged;
        workspace.EnablePresentationPolish();
        workspace.EnableCategoryController();
        workspace.EnableFullscreenSurfaceFix();

        _liveTvWorkspace = workspace;
        LiveTvWorkspaceHost.Content = workspace;
        LiveTvWorkspaceHost.Visibility = Visibility.Visible;
        if (_catalog is not null)
        {
            workspace.SetCatalog(_catalog, _favoriteStableIds);
        }

        return workspace;
    }

    private ProgrammeGuideView EnsureProgrammeGuideWorkspace()
    {
        if (_programmeGuideWorkspace is not null)
        {
            return _programmeGuideWorkspace;
        }

        var workspace = new ProgrammeGuideView
        {
            Visibility = Visibility.Collapsed,
        };
        workspace.PlayChannelRequested +=
            ProgrammeGuideWorkspace_PlayChannelRequested;

        _programmeGuideWorkspace = workspace;
        ProgrammeGuideWorkspaceHost.Content = workspace;
        ProgrammeGuideWorkspaceHost.Visibility = Visibility.Visible;
        return workspace;
    }

    private void ReleaseWorkspaceEventHandlers()
    {
        if (_liveTvWorkspace is not null)
        {
            _liveTvWorkspace.BackRequested -= LiveTvWorkspace_BackRequested;
            _liveTvWorkspace.FullscreenToggleRequested -=
                LiveTvWorkspace_FullscreenToggleRequested;
            _liveTvWorkspace.FavoriteChanged -= LiveTvWorkspace_FavoriteChanged;
        }

        if (_programmeGuideWorkspace is not null)
        {
            _programmeGuideWorkspace.PlayChannelRequested -=
                ProgrammeGuideWorkspace_PlayChannelRequested;
        }
    }
}