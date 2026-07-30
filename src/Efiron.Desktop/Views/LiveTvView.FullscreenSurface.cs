using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Efiron.Desktop.Views;

public sealed partial class LiveTvView
{
    private bool _fullscreenSurfaceFixEnabled;
    private bool? _fullscreenSurfaceApplied;
    private Brush? _normalLiveRootBackground;
    private Thickness _normalPlayerBorderThickness;
    private string? _appliedVideoCropGeometry;

    internal void EnableFullscreenSurfaceFix()
    {
        if (_fullscreenSurfaceFixEnabled)
        {
            return;
        }

        _fullscreenSurfaceFixEnabled = true;
        _normalLiveRootBackground = LiveRoot.Background;
        _normalPlayerBorderThickness = PlayerSurfaceBorder.BorderThickness;
        LiveRoot.LayoutUpdated += FullscreenSurface_LiveRootLayoutUpdated;
        ApplyFullscreenSurfaceState(force: true);
    }

    internal FullscreenSurfaceEvidence GetFullscreenSurfaceEvidence()
    {
        var background = LiveRoot.Background is SolidColorBrush brush
            ? brush.Color.ToString()
            : string.Empty;
        return new FullscreenSurfaceEvidence(
            _isFullscreen,
            LiveRoot.RowSpacing,
            PlayerWorkspace.RowSpacing,
            PlayerSurfaceBorder.BorderThickness.Left,
            background,
            LiveRoot.ActualWidth,
            LiveRoot.ActualHeight,
            _appliedVideoCropGeometry ?? string.Empty);
    }

    private void FullscreenSurface_LiveRootLayoutUpdated(object? sender, object e) =>
        ApplyFullscreenSurfaceState(force: false);

    private void ApplyFullscreenSurfaceState(bool force)
    {
        var stateChanged = _fullscreenSurfaceApplied != _isFullscreen;
        if (force || stateChanged)
        {
            _fullscreenSurfaceApplied = _isFullscreen;
            LiveRoot.RowSpacing = _isFullscreen ? 0 : 12;
            PlayerWorkspace.RowSpacing = _isFullscreen ? 0 : 9;
            PlayerSurfaceBorder.BorderThickness = _isFullscreen
                ? new Thickness(0)
                : _normalPlayerBorderThickness;
            LiveRoot.Background = _isFullscreen
                ? new SolidColorBrush(Microsoft.UI.Colors.Black)
                : _normalLiveRootBackground;
        }

        ApplyVideoGeometry();
    }

    private void ApplyVideoGeometry()
    {
        if (_playbackSession is null)
        {
            return;
        }

        if (!_isFullscreen)
        {
            if (_appliedVideoCropGeometry is not null)
            {
                _playbackSession.MediaPlayer.CropGeometry = null;
                _playbackSession.MediaPlayer.AspectRatio = null;
                _playbackSession.MediaPlayer.Scale = 0;
                _appliedVideoCropGeometry = null;
            }

            return;
        }

        var width = Math.Max(1, (int)Math.Round(PlayerSurfaceBorder.ActualWidth));
        var height = Math.Max(1, (int)Math.Round(PlayerSurfaceBorder.ActualHeight));
        if (width <= 1 || height <= 1)
        {
            return;
        }

        var divisor = GreatestCommonDivisor(width, height);
        var geometry = $"{width / divisor}:{height / divisor}";
        if (string.Equals(
                geometry,
                _appliedVideoCropGeometry,
                StringComparison.Ordinal))
        {
            return;
        }

        _playbackSession.MediaPlayer.AspectRatio = null;
        _playbackSession.MediaPlayer.Scale = 0;
        _playbackSession.MediaPlayer.CropGeometry = geometry;
        _appliedVideoCropGeometry = geometry;
    }

    private static int GreatestCommonDivisor(int left, int right)
    {
        while (right != 0)
        {
            (left, right) = (right, left % right);
        }

        return Math.Max(1, Math.Abs(left));
    }

    internal sealed record FullscreenSurfaceEvidence(
        bool IsFullscreen,
        double LiveRootRowSpacing,
        double PlayerWorkspaceRowSpacing,
        double PlayerBorderThickness,
        string LiveRootBackground,
        double Width,
        double Height,
        string VideoCropGeometry);
}
