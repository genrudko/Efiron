using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Efiron.Desktop.Playback;

internal sealed class NativeVideoHostWindow : IDisposable
{
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsClipSiblings = 0x04000000;
    private const uint WsClipChildren = 0x02000000;
    private const uint SsBlackRect = 0x00000004;
    private const uint WsExNoActivate = 0x08000000;
    private const uint WsExNoParentNotify = 0x00000004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;

    private int _x = int.MinValue;
    private int _y = int.MinValue;
    private int _width = -1;
    private int _height = -1;
    private bool _disposed;

    public NativeVideoHostWindow(nint parentWindowHandle)
    {
        if (parentWindowHandle == 0)
        {
            throw new ArgumentException(
                "A parent HWND is required.",
                nameof(parentWindowHandle));
        }

        Handle = CreateWindowExW(
            WsExNoActivate | WsExNoParentNotify,
            "STATIC",
            "Efiron native playback host",
            WsChild | WsVisible | WsClipSiblings | WsClipChildren | SsBlackRect,
            0,
            0,
            1,
            1,
            parentWindowHandle,
            0,
            0,
            0);
        if (Handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        IsVisible = true;
        SetVisible(false);
    }

    public nint Handle { get; private set; }

    public bool IsVisible { get; private set; }

    public void SetBounds(int x, int y, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (width <= 0 || height <= 0)
        {
            SetVisible(false);
            return;
        }

        if (_x == x && _y == y && _width == width && _height == height)
        {
            return;
        }

        if (!SetWindowPos(
                Handle,
                0,
                x,
                y,
                width,
                height,
                SwpNoActivate | (IsVisible ? SwpShowWindow : 0)))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        _x = x;
        _y = y;
        _width = width;
        _height = height;
    }

    public void SetVisible(bool isVisible)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsVisible == isVisible)
        {
            return;
        }

        ShowWindow(Handle, isVisible ? SwShowNoActivate : SwHide);
        IsVisible = isVisible;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        IsVisible = false;
        _x = int.MinValue;
        _y = int.MinValue;
        _width = -1;
        _height = -1;
        var handle = Handle;
        Handle = 0;
        if (handle != 0)
        {
            DestroyWindow(handle);
        }
    }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint extendedStyle,
        [MarshalAs(UnmanagedType.LPWStr)] string className,
        [MarshalAs(UnmanagedType.LPWStr)] string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parentWindow,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);
}
