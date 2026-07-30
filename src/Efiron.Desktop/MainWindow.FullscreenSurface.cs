using Microsoft.UI.Xaml.Media;

namespace Efiron.Desktop;

public sealed partial class MainWindow
{
    private bool _fullscreenWindowSurfaceFixEnabled;
    private bool? _fullscreenWindowSurfaceApplied;
    private Brush? _normalWindowRootBackground;

    private void EnableFullscreenWindowSurfaceFix()
    {
        if (_fullscreenWindowSurfaceFixEnabled)
        {
            return;
        }

        _fullscreenWindowSurfaceFixEnabled = true;
        _normalWindowRootBackground = WindowRoot.Background;
        ShellRoot.LayoutUpdated += FullscreenWindowSurface_LayoutUpdated;
        ApplyFullscreenWindowSurfaceState(force: true);
    }

    private void FullscreenWindowSurface_LayoutUpdated(object? sender, object e) =>
        ApplyFullscreenWindowSurfaceState(force: false);

    private void ApplyFullscreenWindowSurfaceState(bool force)
    {
        if (!force && _fullscreenWindowSurfaceApplied == _isFullscreen)
        {
            return;
        }

        _fullscreenWindowSurfaceApplied = _isFullscreen;
        WindowRoot.Background = _isFullscreen
            ? new SolidColorBrush(Microsoft.UI.Colors.Black)
            : _normalWindowRootBackground;
        ShellRoot.Background = _isFullscreen
            ? new SolidColorBrush(Microsoft.UI.Colors.Black)
            : null;
    }
}
