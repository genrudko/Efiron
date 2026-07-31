using Efiron.Playback;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Efiron.Desktop.Views;

public sealed partial class LiveTvView
{
    private bool _fullscreenSurfaceFixEnabled;
    private bool? _fullscreenSurfaceApplied;
    private Brush? _normalLiveRootBackground;
    private Thickness _normalPlayerBorderThickness;
    private GridLength _normalCategoryRailWidth;
    private GridLength _normalChannelBrowserWidth;
    private GridLength _normalPlayerWidth;
    private double _normalLiveContentColumnSpacing;
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
        _normalCategoryRailWidth = CategoryRailColumn.Width;
        _normalChannelBrowserWidth = ChannelBrowserColumn.Width;
        _normalPlayerWidth = PlayerColumn.Width;
        _normalLiveContentColumnSpacing = LiveContentGrid.ColumnSpacing;
        LiveRoot.LayoutUpdated += FullscreenSurface_LiveRootLayoutUpdated;
        ApplyFullscreenSurfaceState(force: true);
    }

    internal FullscreenSurfaceEvidence GetFullscreenSurfaceEvidence()
    {
        var background = LiveRoot.Background is SolidColorBrush brush
            ? brush.Color.ToString()
            : string.Empty;
        var snapshot = _playbackSession?.Snapshot;
        return new FullscreenSurfaceEvidence(
            _isFullscreen,
            LiveRoot.RowSpacing,
            PlayerWorkspace.RowSpacing,
            PlayerSurfaceBorder.BorderThickness.Left,
            background,
            LiveRoot.ActualWidth,
            LiveRoot.ActualHeight,
            PlayerSurfaceBorder.ActualWidth,
            PlayerSurfaceBorder.ActualHeight,
            _appliedVideoCropGeometry ?? string.Empty,
            snapshot?.State.ToString() ?? string.Empty,
            snapshot?.Source?.ToString() ?? string.Empty);
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

            if (_isFullscreen)
            {
                CategoryRailCard.Visibility = Visibility.Collapsed;
                ChannelBrowserCard.Visibility = Visibility.Collapsed;
                ProgrammeCard.Visibility = Visibility.Collapsed;
                CategoryRailColumn.Width = new GridLength(0);
                ChannelBrowserColumn.Width = new GridLength(0);
                PlayerColumn.Width = new GridLength(1, GridUnitType.Star);
                LiveContentGrid.ColumnSpacing = 0;
                Grid.SetColumn(PlayerWorkspace, 0);
                Grid.SetColumnSpan(PlayerWorkspace, 3);
            }
            else
            {
                CategoryRailCard.Visibility = Visibility.Visible;
                ChannelBrowserCard.Visibility = Visibility.Visible;
                ProgrammeCard.Visibility = Visibility.Visible;
                CategoryRailColumn.Width = _normalCategoryRailWidth;
                ChannelBrowserColumn.Width = _normalChannelBrowserWidth;
                PlayerColumn.Width = _normalPlayerWidth;
                LiveContentGrid.ColumnSpacing = _normalLiveContentColumnSpacing;
                Grid.SetColumn(PlayerWorkspace, 2);
                Grid.SetColumnSpan(PlayerWorkspace, 1);
            }
        }

        ApplyVideoGeometry();
    }

    private void ApplyVideoGeometry()
    {
        if (_playbackBackend is WindowsMediaPlaybackBackend)
        {
            if (_windowsMediaSurface is not null)
            {
                _windowsMediaSurface.Stretch = _isFullscreen
                    ? Stretch.UniformToFill
                    : Stretch.Uniform;
            }

            _appliedVideoCropGeometry = _isFullscreen
                ? "WindowsMedia:UniformToFill"
                : null;
            return;
        }

        if (_playbackBackend is MpvPlaybackBackend mpv)
        {
            if (_mpvSurface is not null && _mpvSurface.IsLoaded)
            {
                UpdateMpvCompositionSize(mpv);
                AttachMpvSwapChain(mpv.DisplaySwapChain);
            }

            _appliedVideoCropGeometry = _isFullscreen
                ? "mpv:composition-fill"
                : null;
            return;
        }

        if (_playbackBackend is not LibVlcPlaybackBackend libVlc)
        {
            return;
        }

        if (!_isFullscreen)
        {
            if (_appliedVideoCropGeometry is not null)
            {
                libVlc.MediaPlayer.CropGeometry = null;
                libVlc.MediaPlayer.AspectRatio = null;
                libVlc.MediaPlayer.Scale = 0;
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

        libVlc.MediaPlayer.AspectRatio = null;
        libVlc.MediaPlayer.Scale = 0;
        libVlc.MediaPlayer.CropGeometry = geometry;
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
        double PlayerWidth,
        double PlayerHeight,
        string VideoCropGeometry,
        string PlaybackState,
        string PlaybackSource);
}
