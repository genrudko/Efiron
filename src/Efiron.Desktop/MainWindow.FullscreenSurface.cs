using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Efiron.Desktop;

public sealed partial class MainWindow
{
    private const int DwmwaNcRenderingPolicy = 2;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmNcRenderingUseWindowStyle = 0;
    private const int DwmNcRenderingDisabled = 1;
    private const int DwmColorDefault = unchecked((int)0xFFFFFFFF);
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);
    private const int DwmColorBlack = 0;
    private const int DwmWindowCornerDefault = 0;
    private const int DwmWindowCornerDoNotRound = 1;

    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsOverlappedWindow = 0x00CF0000L;
    private const long WsPopup = unchecked((long)0x80000000L);
    private const long WsVisible = 0x10000000L;
    private const long WsClipChildren = 0x02000000L;
    private const long WsClipSiblings = 0x04000000L;
    private const long WsExDlgModalFrame = 0x00000001L;
    private const long WsExWindowEdge = 0x00000100L;
    private const long WsExClientEdge = 0x00000200L;
    private const long WsExStaticEdge = 0x00020000L;

    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpNoOwnerZOrder = 0x0200;

    private bool _fullscreenWindowSurfaceFixEnabled;
    private bool? _fullscreenWindowSurfaceApplied;
    private bool _fullscreenFinalizeQueued;
    private bool _switchingFullscreenPresenter;
    private int _fullscreenFinalizeRemaining;
    private Brush? _normalWindowRootBackground;
    private Brush? _normalShellRootBackground;
    private nint _normalWindowStyle;
    private nint _normalWindowExStyle;
    private bool _normalWindowStyleCaptured;
    private OverlappedPresenterState _normalOverlappedState =
        OverlappedPresenterState.Restored;
    private bool _normalOverlappedStateCaptured;

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
        CaptureNormalWindowFrame(windowHandle);
        CaptureNormalPresenterState(AppWindow);

        _fullscreenWindowSurfaceApplied = false;
        AppWindow.Changed += FullscreenWindowSurface_AppWindowChanged;
        WindowRoot.LayoutUpdated += FullscreenWindowSurface_WindowRootLayoutUpdated;
        Closed += FullscreenWindowSurface_Closed;
    }

    private void FullscreenWindowSurface_AppWindowChanged(
        AppWindow sender,
        AppWindowChangedEventArgs args)
    {
        if (!args.DidPresenterChange &&
            !args.DidSizeChange &&
            !args.DidPositionChange)
        {
            return;
        }

        if (_fullscreenWindowSurfaceApplied != _isFullscreen ||
            (_isFullscreen &&
             sender.Presenter.Kind == AppWindowPresenterKind.FullScreen))
        {
            ApplyFullscreenWindowSurfaceState(force: true);
            return;
        }

        if (_isFullscreen)
        {
            QueueFullscreenWindowFinalize();
        }
        else
        {
            CaptureNormalPresenterState(sender);
        }
    }

    private void FullscreenWindowSurface_WindowRootLayoutUpdated(
        object? sender,
        object e) =>
        ApplyFullscreenWindowSurfaceState(force: false);

    private void ApplyFullscreenWindowSurfaceState(bool force)
    {
        if (!_fullscreenWindowSurfaceFixEnabled)
        {
            return;
        }

        if (_isFullscreen &&
            AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
        {
            if (_switchingFullscreenPresenter)
            {
                return;
            }

            _switchingFullscreenPresenter = true;
            try
            {
                AppWindow.SetPresenter(AppWindowPresenterKind.Default);
            }
            finally
            {
                _switchingFullscreenPresenter = false;
            }

            return;
        }

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
            : _normalShellRootBackground;

        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (_isFullscreen)
        {
            EnterNativePopupFullscreen(windowHandle);
            _fullscreenFinalizeRemaining = 4;
            QueueFullscreenWindowFinalize();
        }
        else
        {
            _fullscreenFinalizeRemaining = 0;
            ExitNativePopupFullscreen(windowHandle);
            RestoreCustomTitleBarContract();
            RestoreNormalPresenterState();
        }
    }

    private void CaptureNormalWindowFrame(nint windowHandle)
    {
        if (_isFullscreen || _normalWindowStyleCaptured)
        {
            return;
        }

        var style = GetWindowLongPtr(windowHandle, GwlStyle);
        var exStyle = GetWindowLongPtr(windowHandle, GwlExStyle);
        if (style == 0)
        {
            return;
        }

        _normalWindowStyle = style;
        _normalWindowExStyle = exStyle;
        _normalWindowStyleCaptured = true;
    }

    private void CaptureNormalPresenterState(AppWindow window)
    {
        if (_isFullscreen || window.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        _normalOverlappedState = presenter.State;
        _normalOverlappedStateCaptured = true;
    }

    private void RestoreNormalPresenterState()
    {
        if (!_normalOverlappedStateCaptured ||
            AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        if (_normalOverlappedState == OverlappedPresenterState.Maximized)
        {
            presenter.Maximize();
        }
        else
        {
            presenter.Restore();
        }
    }

    private void EnterNativePopupFullscreen(nint windowHandle)
    {
        if (!_normalWindowStyleCaptured)
        {
            CaptureNormalWindowFrame(windowHandle);
        }

        var normalStyle = _normalWindowStyleCaptured
            ? _normalWindowStyle.ToInt64()
            : GetWindowLongPtr(windowHandle, GwlStyle).ToInt64();
        var popupStyle =
            (normalStyle & ~WsOverlappedWindow) |
            WsPopup |
            WsVisible |
            WsClipChildren |
            WsClipSiblings;
        var normalExStyle = _normalWindowStyleCaptured
            ? _normalWindowExStyle.ToInt64()
            : GetWindowLongPtr(windowHandle, GwlExStyle).ToInt64();
        var popupExStyle = normalExStyle &
            ~(WsExDlgModalFrame |
              WsExWindowEdge |
              WsExClientEdge |
              WsExStaticEdge);

        SetWindowStyleIfDifferent(windowHandle, GwlStyle, popupStyle);
        SetWindowStyleIfDifferent(windowHandle, GwlExStyle, popupExStyle);
        ApplyDwmState(windowHandle, fullscreen: true);
        FitPopupClientToMonitor(windowHandle);
    }

    private void ExitNativePopupFullscreen(nint windowHandle)
    {
        _ = SetWindowRgn(windowHandle, 0, true);
        ApplyDwmState(windowHandle, fullscreen: false);

        if (_normalWindowStyleCaptured)
        {
            SetWindowStyleIfDifferent(
                windowHandle,
                GwlStyle,
                _normalWindowStyle.ToInt64());
            SetWindowStyleIfDifferent(
                windowHandle,
                GwlExStyle,
                _normalWindowExStyle.ToInt64());
        }

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
            SwpNoOwnerZOrder |
            SwpFrameChanged);
    }

    private void RestoreCustomTitleBarContract()
    {
        if (_isFullscreen)
        {
            return;
        }

        TitleBarDragRegion.Visibility = Visibility.Visible;
        if (!ExtendsContentIntoTitleBar)
        {
            ExtendsContentIntoTitleBar = true;
        }

        SetTitleBar(TitleBarDragRegion);
        ApplyTitleBarContrast();
    }

    private static void SetWindowStyleIfDifferent(
        nint windowHandle,
        int index,
        long requestedStyle)
    {
        var currentStyle = GetWindowLongPtr(windowHandle, index).ToInt64();
        if (currentStyle != requestedStyle)
        {
            _ = SetWindowLongPtr(
                windowHandle,
                index,
                (nint)requestedStyle);
        }
    }

    private static void FitPopupClientToMonitor(nint windowHandle)
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

        var monitorWidth = info.Monitor.Right - info.Monitor.Left;
        var monitorHeight = info.Monitor.Bottom - info.Monitor.Top;
        if (monitorWidth <= 0 || monitorHeight <= 0)
        {
            return;
        }

        _ = SetWindowRgn(windowHandle, 0, false);
        _ = SetWindowPos(
            windowHandle,
            0,
            info.Monitor.Left,
            info.Monitor.Top,
            monitorWidth,
            monitorHeight,
            SwpNoZOrder |
            SwpNoActivate |
            SwpNoOwnerZOrder |
            SwpFrameChanged |
            SwpShowWindow);

        if (TryGetClientScreenBounds(windowHandle, out var client))
        {
            var insetLeft = Math.Max(0, client.Left - info.Monitor.Left);
            var insetTop = Math.Max(0, client.Top - info.Monitor.Top);
            var insetRight = Math.Max(0, info.Monitor.Right - client.Right);
            var insetBottom = Math.Max(0, info.Monitor.Bottom - client.Bottom);
            if (insetLeft > 0 || insetTop > 0 ||
                insetRight > 0 || insetBottom > 0)
            {
                _ = SetWindowPos(
                    windowHandle,
                    0,
                    info.Monitor.Left - insetLeft,
                    info.Monitor.Top - insetTop,
                    monitorWidth + insetLeft + insetRight,
                    monitorHeight + insetTop + insetBottom,
                    SwpNoZOrder |
                    SwpNoActivate |
                    SwpNoOwnerZOrder |
                    SwpFrameChanged |
                    SwpShowWindow);
            }
        }

        if (!GetWindowRect(windowHandle, out var finalWindow))
        {
            return;
        }

        var finalWidth = Math.Max(1, finalWindow.Right - finalWindow.Left);
        var finalHeight = Math.Max(1, finalWindow.Bottom - finalWindow.Top);
        var region = CreateRectRgn(0, 0, finalWidth, finalHeight);
        if (region != 0 && SetWindowRgn(windowHandle, region, true) == 0)
        {
            _ = DeleteObject(region);
        }
    }

    private static bool TryGetClientScreenBounds(
        nint windowHandle,
        out NativeRect bounds)
    {
        bounds = default;
        if (!GetClientRect(windowHandle, out var client))
        {
            return false;
        }

        var origin = new NativePoint();
        if (!ClientToScreen(windowHandle, ref origin))
        {
            return false;
        }

        bounds.Left = origin.X;
        bounds.Top = origin.Y;
        bounds.Right = origin.X + client.Right - client.Left;
        bounds.Bottom = origin.Y + client.Bottom - client.Top;
        return bounds.Right > bounds.Left && bounds.Bottom > bounds.Top;
    }

    private void QueueFullscreenWindowFinalize()
    {
        if (_fullscreenFinalizeQueued ||
            !_isFullscreen ||
            _fullscreenFinalizeRemaining <= 0)
        {
            return;
        }

        _fullscreenFinalizeQueued = true;
        DispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Low,
            () =>
            {
                _fullscreenFinalizeQueued = false;
                if (!_isFullscreen)
                {
                    return;
                }

                _fullscreenFinalizeRemaining--;
                var windowHandle =
                    WinRT.Interop.WindowNative.GetWindowHandle(this);
                EnterNativePopupFullscreen(windowHandle);
                QueueFullscreenWindowFinalize();
            });
    }

    private static void ApplyDwmState(
        nint windowHandle,
        bool fullscreen)
    {
        var ncRenderingPolicy = fullscreen
            ? DwmNcRenderingDisabled
            : DwmNcRenderingUseWindowStyle;
        var borderColor = fullscreen
            ? DwmColorNone
            : DwmColorDefault;
        var captionColor = fullscreen
            ? DwmColorBlack
            : DwmColorDefault;
        var cornerPreference = fullscreen
            ? DwmWindowCornerDoNotRound
            : DwmWindowCornerDefault;

        _ = DwmSetWindowAttribute(
            windowHandle,
            DwmwaNcRenderingPolicy,
            ref ncRenderingPolicy,
            Marshal.SizeOf<int>());
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
        WindowRoot.LayoutUpdated -= FullscreenWindowSurface_WindowRootLayoutUpdated;
        Closed -= FullscreenWindowSurface_Closed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
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
    private static extern nint SetWindowLongPtr(
        nint hwnd,
        int index,
        nint value);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        nint monitor,
        ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(
        nint hwnd,
        out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(
        nint hwnd,
        out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(
        nint hwnd,
        ref NativePoint point);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRectRgn(
        int left,
        int top,
        int right,
        int bottom);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(
        nint hwnd,
        nint region,
        [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);

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
