using Efiron.Desktop.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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

        ConfigureWorkspaceHost(LiveTvWorkspaceHost);
        var workspace = new LiveTvView
        {
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            RequestedTheme = ElementTheme.Default,
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

        ConfigureWorkspaceHost(ProgrammeGuideWorkspaceHost);
        var workspace = new ProgrammeGuideView
        {
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            RequestedTheme = ElementTheme.Default,
        };
        workspace.PlayChannelRequested +=
            ProgrammeGuideWorkspace_PlayChannelRequested;

        _programmeGuideWorkspace = workspace;
        ProgrammeGuideWorkspaceHost.Content = workspace;
        ProgrammeGuideWorkspaceHost.Visibility = Visibility.Visible;
        return workspace;
    }

    private void ReleaseProgrammeGuideWorkspace()
    {
        var workspace = _programmeGuideWorkspace;
        if (workspace is null)
        {
            return;
        }

        workspace.PlayChannelRequested -= ProgrammeGuideWorkspace_PlayChannelRequested;
        workspace.Visibility = Visibility.Collapsed;
        ProgrammeGuideWorkspaceHost.Content = null;
        ProgrammeGuideWorkspaceHost.Visibility = Visibility.Collapsed;
        _programmeGuideWorkspace = null;
    }

    private void ApplyThemeToLazyWorkspaces()
    {
        // Keep lazy workspaces on the inherited theme. Explicitly copying the
        // current Light/Dark value creates a second theme boundary and can
        // leave root ThemeResource values stale during an in-process switch.
        if (_liveTvWorkspace is not null &&
            _liveTvWorkspace.RequestedTheme != ElementTheme.Default)
        {
            _liveTvWorkspace.RequestedTheme = ElementTheme.Default;
        }

        if (_programmeGuideWorkspace is not null &&
            _programmeGuideWorkspace.RequestedTheme != ElementTheme.Default)
        {
            _programmeGuideWorkspace.RequestedTheme = ElementTheme.Default;
        }
    }

    private static void ConfigureWorkspaceHost(ContentControl host)
    {
        host.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        host.VerticalContentAlignment = VerticalAlignment.Stretch;
        host.HorizontalAlignment = HorizontalAlignment.Stretch;
        host.VerticalAlignment = VerticalAlignment.Stretch;
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

        ReleaseProgrammeGuideWorkspace();
    }
}
