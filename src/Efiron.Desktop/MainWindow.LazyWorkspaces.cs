using Efiron.Desktop.Views;
using Efiron.Domain.Appearance;
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
        workspace.AttachNativePlaybackParent(
            WinRT.Interop.WindowNative.GetWindowHandle(this));
        workspace.EnableFlyleafPlayback();
        workspace.EnableWorkspaceLifecycle();
        workspace.BackRequested += LiveTvWorkspace_BackRequested;
        workspace.FullscreenToggleRequested +=
            LiveTvWorkspace_FullscreenToggleRequested;
        workspace.FavoriteChanged += LiveTvWorkspace_FavoriteChanged;
        workspace.EnablePresentationPolish();
        workspace.EnableCategoryController();
        workspace.EnableFullscreenSurfaceFix();
        workspace.EnableColdStartThemeEvidence();

        _liveTvWorkspace = workspace;
        LiveTvWorkspaceHost.Content = workspace;
        LiveTvWorkspaceHost.Visibility = Visibility.Visible;
        ApplyThemeToLazyWorkspace(workspace, _appearancePreferences.Theme);
        if (_catalog is not null)
        {
            workspace.SetCatalog(_catalog, _favoriteStableIds);
        }

        TryStartAppearanceTransitionVerification();
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
        ApplyThemeToLazyWorkspace(workspace, _appearancePreferences.Theme);
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
        workspace.DisposeWorkspace();
    }

    private void ApplyThemeToLazyWorkspaces()
    {
        if (_liveTvWorkspace is not null)
        {
            ApplyThemeToLazyWorkspace(
                _liveTvWorkspace,
                _appearancePreferences.Theme);
        }

        if (_programmeGuideWorkspace is not null)
        {
            ApplyThemeToLazyWorkspace(
                _programmeGuideWorkspace,
                _appearancePreferences.Theme);
        }
    }

    private static void ApplyThemeToLazyWorkspace(
        FrameworkElement workspace,
        AppearanceTheme preference)
    {
        // A lazy UserControl is constructed while detached from WindowRoot.
        // With a persisted Light preference and a dark Windows theme its root
        // ThemeResource can therefore be resolved from the system theme before
        // inheritance starts. Apply the explicit user preference only after the
        // control has been attached to its host. Do not copy ActualTheme here:
        // ActualTheme can still contain the previous value during a live switch.
        var requestedTheme = preference switch
        {
            AppearanceTheme.Light => ElementTheme.Light,
            AppearanceTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        if (workspace.RequestedTheme != requestedTheme)
        {
            workspace.RequestedTheme = requestedTheme;
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
