using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Media;

namespace Efiron.Desktop;

public sealed partial class MainWindow
{
    private const int DwmwaBorderColor = 34;
    private const int DwmColorDefault = unchecked((int)0xFFFFFFFF);
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);

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
        ApplyDwmBorderState();
    }

    private void ApplyDwmBorderState()
    {
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var borderColor = _isFullscreen
            ? DwmColorNone
            : DwmColorDefault;
        _ = DwmSetWindowAttribute(
            windowHandle,
            DwmwaBorderColor,
            ref borderColor,
            Marshal.SizeOf<int>());
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
