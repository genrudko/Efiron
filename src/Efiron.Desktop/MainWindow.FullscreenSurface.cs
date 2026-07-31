using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Efiron.Desktop;

public sealed partial class MainWindow
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmColorDefault = unchecked((int)0xFFFFFFFF);
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);
    private const int DwmWindowCornerDefault = 0;
    private const int DwmWindowCornerDoNotRound = 1;

    private const int GwlStyle = -16;
    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    private bool _fullscreenWindowSurfaceFixEnabled;
    private bool? _fullscreenWindowSurfaceApplied;
    private Brush? _normalWindowRootBackground;
    private Brush? _normalShellRootBackground;
    private nint _normalWindowStyle;
    private bool _normalWindowStyleCaptured;

    private void EnableFullscreenWindowSurfaceFix()
    {
        if (_fullscreenWindowSurfaceFixEnabled)
        {
            return;
        }

        _fullscreenWindowSurfaceFixEnabled = true;
        _normalWindowRootBackground = WindowRoot.Background;
        _normalShellRootBackground = ShellRoot.Background;

        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _normalWindowStyle = GetWindowLongPtr(windowHandle, GwlStyle);
        _normalWindowStyleCaptured = _normalWindowStyle != 0;

        // The normal startup state is already correct. Mark it as applied and
        // do not mutate the native frame before the first useful XAML paint.
        _fullscreenWindowSurfaceApplied = false;
        AppWindow.Changed += FullscreenWindowSurface_AppWindowChanged;
        Closed += FullscreenWindowSurface_Closed;
    }

    private void FullscreenWindowSurface_AppWindowChanged(
        AppWindow sender,
        AppWindowChangedEventArgs args)
    {
        if (!args.DidPresenterChange && !args.DidSizeChange)
        {
            return;
        }

        // SetFullscreen changes _isFullscreen before changing the presenter.
        // Apply exactly once when the logical state differs from the native
        // state. Notifications caused by SWP_FRAMECHANGED then become no-ops.
        ApplyFullscreenWindowSurfaceState(force: false);
    }

    private void ApplyFullscreenWindowSurfaceState(bool force)
    {
        if (!_fullscreenWindowSurfaceFixEnabled ||
            (!force && _fullscreenWindowSurfaceApplied == _isFullscreen))
        {
            return;
        }

        _fullscreenWindowSurfaceApplied = _isFullscreen;
        WindowRoot.Background = _isFullscreen
            ? new SolidColorBrush(Microsoft.UI.Colors.Black)
            : _normalWindowRootBackground;
        ShellRoot.Background = _isFullscreen
            ? new SolidColorBrush(Microsoft.UI.Colors.Black)
            : _normalShellRootBackground;

        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ApplyNativeWindowFrameState(windowHandle);
        ApplyDwmBorderState(windowHandle);
    }

    private void ApplyNativeWindowFrameState(nint windowHandle)
    {
        var currentStyle = GetWindowLongPtr(windowHandle, GwlStyle).ToInt64();
        var requestedStyle = _isFullscreen
            ? currentStyle & ~(WsCaption | WsThickFrame)
            : _normalWindowStyleCaptured
                ? _normalWindowStyle.ToInt64()
                : currentStyle;

        // Reissuing SWP_FRAMECHANGED for an unchanged style can recursively
        // retrigger native window notifications and starve the XAML compositor.
        if (requestedStyle == currentStyle)
        {
            return;
        }

        _ = SetWindowLongPtr(windowHandle, GwlStyle, (nint)requestedStyle);
        _ = SetWindowPos(
            windowHandle,
            0,
            0,
            0,
            0,
            0,
            SwpNoSize |
            SwpNoMove |
            SwpNoZOrder |
            SwpNoActivate |
            SwpFrameChanged);
    }

    private void ApplyDwmBorderState(nint windowHandle)
    {
        var borderColor = _isFullscreen
            ? DwmColorNone
            : DwmColorDefault;
        var captionColor = _isFullscreen
            ? 0
            : DwmColorDefault;
        var cornerPreference = _isFullscreen
            ? DwmWindowCornerDoNotRound
            : DwmWindowCornerDefault;

        _ = DwmSetWindowAttribute(
            windowHandle,
            DwmwaBorderColor,
            ref borderColor,
            Marshal.SizeOf<int>());
        _ = DwmSetWindowAttribute(
            windowHandle,
            DwmwaCaptionColor,
            ref captionColor,
            Marshal.SizeOf<int>());
        _ = DwmSetWindowAttribute(
            windowHandle,
            DwmwaWindowCornerPreference,
            ref cornerPreference,
            Marshal.SizeOf<int>());
        _ = DwmFlush();
    }

    private void FullscreenWindowSurface_Closed(
        object sender,
        WindowEventArgs args)
    {
        AppWindow.Changed -= FullscreenWindowSurface_AppWindowChanged;
        Closed -= FullscreenWindowSurface_Closed;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint hwnd,
        nint hwndInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
