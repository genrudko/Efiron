using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Efiron.Desktop.Views;

public sealed partial class LiveTvView
{
    private bool _fullscreenSurfaceFixEnabled;
    private bool? _fullscreenSurfaceApplied;
    private Brush? _normalLiveRootBackground;
    private Thickness _normalPlayerBorderThickness;

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
            LiveRoot.ActualHeight);
    }

    private void FullscreenSurface_LiveRootLayoutUpdated(object? sender, object e) =>
        ApplyFullscreenSurfaceState(force: false);

    private void ApplyFullscreenSurfaceState(bool force)
    {
        if (!force && _fullscreenSurfaceApplied == _isFullscreen)
        {
            return;
        }

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

    internal sealed record FullscreenSurfaceEvidence(
        bool IsFullscreen,
        double LiveRootRowSpacing,
        double PlayerWorkspaceRowSpacing,
        double PlayerBorderThickness,
        string LiveRootBackground,
        double Width,
        double Height);
}
