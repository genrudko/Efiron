using Efiron.Application.Playback;
using Efiron.Domain.Playback;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Efiron.Desktop.Views;

public sealed partial class LiveTvView
{
    private static readonly TimeSpan PlayerChromeHideDelay = TimeSpan.FromSeconds(3.2);

    private bool _presentationPolishEnabled;
    private bool _pointerOverPlayerChrome;
    private DispatcherQueueTimer? _playerChromeTimer;
    private UIElement? _playerProgrammeOverlay;
    private UIElement? _playerOverlayScrim;
    private PlaybackState _playerChromePlaybackState;

    internal void EnablePresentationPolish()
    {
        if (_presentationPolishEnabled)
        {
            return;
        }

        _presentationPolishEnabled = true;
        ResolvePlayerChromeElements();
        ConfigureFloatingPlayerControls();

        PlaybackSnapshotChanged += PresentationPolish_PlaybackSnapshotChanged;
        LiveRoot.SizeChanged += PresentationPolish_LiveRootSizeChanged;
        LiveRoot.LayoutUpdated += PresentationPolish_LiveRootLayoutUpdated;
        LiveRoot.KeyDown += PresentationPolish_LiveRootKeyDown;
        CategoryRailListView.SelectionChanged +=
            PresentationPolish_CategoryRailSelectionChanged;

        PlayerSurfaceBorder.PointerEntered += PlayerSurface_PointerEntered;
        PlayerSurfaceBorder.PointerMoved += PlayerSurface_PointerMoved;
        PlayerSurfaceBorder.PointerPressed += PlayerSurface_PointerPressed;
        PlayerSurfaceBorder.PointerExited += PlayerSurface_PointerExited;
        PlayerControlsBorder.PointerEntered += PlayerControls_PointerEntered;
        PlayerControlsBorder.PointerExited += PlayerControls_PointerExited;

        _playerChromeTimer = DispatcherQueue.CreateTimer();
        _playerChromeTimer.Interval = PlayerChromeHideDelay;
        _playerChromeTimer.IsRepeating = false;
        _playerChromeTimer.Tick += PlayerChromeTimer_Tick;

        ApplyCompactChannelWidth(LiveRoot.ActualWidth);
        ShowPlayerChrome(restartAutoHide: false);
    }

    private void ResolvePlayerChromeElements()
    {
        if (PlayerSurfaceBorder.Child is not Grid playerGrid)
        {
            return;
        }

        DependencyObject current = SelectedChannelText;
        while (VisualTreeHelper.GetParent(current) is DependencyObject parent &&
               !ReferenceEquals(parent, playerGrid))
        {
            current = parent;
        }

        _playerProgrammeOverlay = current as UIElement;
        _playerOverlayScrim = playerGrid.Children
            .OfType<Grid>()
            .FirstOrDefault(grid =>
                grid.Height >= 180 &&
                grid.VerticalAlignment == VerticalAlignment.Bottom &&
                !ReferenceEquals(grid, _playerProgrammeOverlay));
    }

    private void ConfigureFloatingPlayerControls()
    {
        PlayerControlsBorder.HorizontalAlignment = HorizontalAlignment.Center;
        PlayerControlsBorder.Background = new SolidColorBrush(
            Microsoft.UI.ColorHelper.FromArgb(218, 7, 11, 17));
        PlayerControlsBorder.BorderThickness = new Thickness(0);
        PlayerControlsBorder.CornerRadius = new CornerRadius(24);
        PlayerControlsBorder.Padding = new Thickness(8, 6, 8, 6);
        PlayerControlsBorder.Margin = new Thickness(0, 0, 0, 14);

        SelectedChannelPanel.Visibility = Visibility.Collapsed;
        SelectedInfoColumn.Width = new GridLength(0);
        VolumeColumn.Width = new GridLength(112);

        foreach (var button in PlaybackControlsGrid.Children.OfType<Button>())
        {
            button.Background = new SolidColorBrush(
                Microsoft.UI.ColorHelper.FromArgb(34, 255, 255, 255));
            button.BorderThickness = new Thickness(0);
            button.CornerRadius = new CornerRadius(18);
            button.Width = 36;
            button.Height = 36;
        }
    }

    private void PresentationPolish_PlaybackSnapshotChanged(
        object? sender,
        PlaybackSnapshotChangedEventArgs e)
    {
        _playerChromePlaybackState = e.Snapshot.State;
        DispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Low,
            () =>
            {
                if (e.Snapshot.State == PlaybackState.Opening)
                {
                    PlayerEmptyState.Visibility = Visibility.Collapsed;
                }

                ShowPlayerChrome(
                    restartAutoHide: e.Snapshot.State == PlaybackState.Playing);
            });
    }

    private void PresentationPolish_CategoryRailSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (CategoryRailListView.SelectedIndex < 0)
        {
            return;
        }

        if (CategoryComboBox.SelectedIndex != CategoryRailListView.SelectedIndex)
        {
            CategoryComboBox.SelectedIndex = CategoryRailListView.SelectedIndex;
        }

        // Do not rely on the hidden ComboBox event as an implementation bridge.
        // The visible category rail owns the user action and applies the filter directly.
        ApplyFilters();
    }

    private void PresentationPolish_LiveRootSizeChanged(
        object sender,
        SizeChangedEventArgs e) =>
        ApplyCompactChannelWidth(e.NewSize.Width);

    private void PresentationPolish_LiveRootLayoutUpdated(
        object? sender,
        object e)
    {
        ApplyCompactChannelWidth(LiveRoot.ActualWidth);
        ConfigureFloatingPlayerControls();
    }

    private void PresentationPolish_LiveRootKeyDown(
        object sender,
        KeyRoutedEventArgs e) =>
        ShowPlayerChrome(restartAutoHide: true);

    private void PlayerSurface_PointerEntered(
        object sender,
        PointerRoutedEventArgs e)
    {
        _pointerOverPlayerChrome = true;
        ShowPlayerChrome(restartAutoHide: false);
    }

    private void PlayerSurface_PointerMoved(
        object sender,
        PointerRoutedEventArgs e) =>
        ShowPlayerChrome(restartAutoHide: true);

    private void PlayerSurface_PointerPressed(
        object sender,
        PointerRoutedEventArgs e) =>
        ShowPlayerChrome(restartAutoHide: true);

    private void PlayerSurface_PointerExited(
        object sender,
        PointerRoutedEventArgs e)
    {
        _pointerOverPlayerChrome = false;
        RestartPlayerChromeTimer();
    }

    private void PlayerControls_PointerEntered(
        object sender,
        PointerRoutedEventArgs e)
    {
        _pointerOverPlayerChrome = true;
        _playerChromeTimer?.Stop();
    }

    private void PlayerControls_PointerExited(
        object sender,
        PointerRoutedEventArgs e)
    {
        _pointerOverPlayerChrome = false;
        RestartPlayerChromeTimer();
    }

    private void PlayerChromeTimer_Tick(
        DispatcherQueueTimer sender,
        object args)
    {
        if (_playerChromePlaybackState == PlaybackState.Playing &&
            !_pointerOverPlayerChrome)
        {
            SetPlayerChromeVisibility(Visibility.Collapsed);
        }
    }

    private void ShowPlayerChrome(bool restartAutoHide)
    {
        SetPlayerChromeVisibility(Visibility.Visible);
        if (restartAutoHide)
        {
            RestartPlayerChromeTimer();
        }
        else
        {
            _playerChromeTimer?.Stop();
        }
    }

    private void RestartPlayerChromeTimer()
    {
        _playerChromeTimer?.Stop();
        if (_playerChromePlaybackState == PlaybackState.Playing)
        {
            _playerChromeTimer?.Start();
        }
    }

    private void SetPlayerChromeVisibility(Visibility visibility)
    {
        if (_playerProgrammeOverlay is not null)
        {
            _playerProgrammeOverlay.Visibility = visibility;
        }

        if (_playerOverlayScrim is not null)
        {
            _playerOverlayScrim.Visibility = visibility;
        }

        PlayerControlsBorder.Visibility = visibility;
    }

    private void ApplyCompactChannelWidth(double width)
    {
        if (!_isFullscreen && width is >= 620 and < 760)
        {
            CategoryRailColumn.Width = new GridLength(292);
            ChannelBrowserColumn.Width = new GridLength(1, GridUnitType.Star);
            PlayerColumn.Width = new GridLength(0);
        }
    }
}
