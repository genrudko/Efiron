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
    private const int DwmColorBlack = 0;
    private const int DwmWindowCornerDefault = 0;
    private const int DwmWindowCornerDoNotRound = 1;

    private const int GwlStyle = -16;
    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;
    private const uint MonitorDefaultToNearest = 2;
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

        // Normal startup is already correct. Native frame work is delayed
        // until the first explicit fullscreen state transition.
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
        if (_isFullscreen)
        {
            CoverFullscreenMonitorBounds(windowHandle);
        }
    }

    private void ApplyNativeWindowFrameState(nint windowHandle)
    {
        var currentStyle = GetWindowLongPtr(windowHandle, GwlStyle).ToInt64();
        var requestedStyle = _isFullscreen
            ? currentStyle & ~(WsCaption | WsThickFrame)
            : _normalWindowStyleCaptured
                ? _normalWindowStyle.ToInt64()
                : currentStyle;

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

    private static void CoverFullscreenMonitorBounds(nint windowHandle)
    {
        var monitor = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        if (monitor == 0)
        {
            return;
        }

        var info = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>(),
        };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        var width = info.Monitor.Right - info.Monitor.Left;
        var height = info.Monitor.Bottom - info.Monitor.Top;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        // WinUI/DWM can leave one physical non-client pixel exposed at the
        // monitor's top edge. Overscan only that edge after fullscreen is
        // established; subsequent size notifications are state-guarded.
        _ = SetWindowPos(
            windowHandle,
            0,
            info.Monitor.Left,
            info.Monitor.Top - 1,
            width,
            height + 1,
            SwpNoZOrder | SwpNoActivate);
    }

    private void ApplyDwmBorderState(nint windowHandle)
    {
        var borderColor = _isFullscreen
            ? DwmColorBlack
            : DwmColorDefault;
        var captionColor = _isFullscreen
            ? DwmColorBlack
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

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
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

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

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
