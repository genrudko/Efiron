using Efiron.Desktop.Playback;
using Efiron.Playback;
using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace Efiron.Desktop.Views;

public sealed partial class LiveTvView
{
    private NativeVideoHostWindow? _nativePlaybackHost;
    private long _nativePlaybackVisibilityCallbackToken;
    private bool _nativePlaybackHostAttached;

    internal nint NativePlaybackHostHandle =>
        _nativePlaybackHost?.Handle ?? 0;

    internal void AttachNativePlaybackParent(nint parentWindowHandle)
    {
        if (_nativePlaybackHostAttached)
        {
            return;
        }

        _nativePlaybackHost = new NativeVideoHostWindow(parentWindowHandle);
        _nativePlaybackVisibilityCallbackToken = RegisterPropertyChangedCallback(
            VisibilityProperty,
            static (sender, _) =>
            {
                if (sender is LiveTvView view)
                {
                    view.UpdateNativePlaybackHostBounds();
                }
            });
        PlayerSurfaceBorder.LayoutUpdated +=
            NativePlaybackHost_PlayerSurfaceLayoutUpdated;
        _nativePlaybackHostAttached = true;
        UpdateNativePlaybackHostBounds();
    }

    private void NativePlaybackHost_PlayerSurfaceLayoutUpdated(
        object? sender,
        object e) =>
        UpdateNativePlaybackHostBounds();

    private void UpdateNativePlaybackHostBounds()
    {
        var host = _nativePlaybackHost;
        var shouldShow =
            !_playbackBackendControllerDisposed &&
            _playbackBackend is MpvProcessPlaybackBackend &&
            Visibility == Visibility.Visible &&
            PlayerSurfaceBorder.Visibility == Visibility.Visible &&
            PlayerSurfaceBorder.ActualWidth > 1 &&
            PlayerSurfaceBorder.ActualHeight > 1;

        if (host is null || !shouldShow)
        {
            HideNativePlaybackHost();
            return;
        }

        try
        {
            Point origin = PlayerSurfaceBorder
                .TransformToVisual(null)
                .TransformPoint(new Point(0, 0));
            var scale = XamlRoot?.RasterizationScale ?? 1d;
            var x = (int)Math.Round(origin.X * scale);
            var y = (int)Math.Round(origin.Y * scale);
            var width = Math.Max(
                1,
                (int)Math.Round(PlayerSurfaceBorder.ActualWidth * scale));
            var height = Math.Max(
                1,
                (int)Math.Round(PlayerSurfaceBorder.ActualHeight * scale));

            host.SetBounds(x, y, width, height);
            if (!host.IsVisible)
            {
                host.SetVisible(true);
            }
        }
        catch (InvalidOperationException)
        {
            HideNativePlaybackHost();
        }
    }

    private void HideNativePlaybackHost()
    {
        var host = _nativePlaybackHost;
        if (host?.IsVisible == true)
        {
            host.SetVisible(false);
        }
    }

    private void DisposeNativePlaybackHost()
    {
        if (!_nativePlaybackHostAttached)
        {
            return;
        }

        PlayerSurfaceBorder.LayoutUpdated -=
            NativePlaybackHost_PlayerSurfaceLayoutUpdated;
        if (_nativePlaybackVisibilityCallbackToken != 0)
        {
            UnregisterPropertyChangedCallback(
                VisibilityProperty,
                _nativePlaybackVisibilityCallbackToken);
            _nativePlaybackVisibilityCallbackToken = 0;
        }

        _nativePlaybackHost?.Dispose();
        _nativePlaybackHost = null;
        _nativePlaybackHostAttached = false;
    }
}
