using LibVLCSharp.Shared;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Efiron.App;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer _playerAttachRetryTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(100),
    };

    private readonly DispatcherTimer _playerControlsAutoHideTimer = new()
    {
        Interval = TimeSpan.FromSeconds(3),
    };

    private bool _playerControlsInitialized;
    private bool _playerEventsAttached;
    private bool _isFullscreen;
    private bool _isUpdatingVolume;
    private bool _isPointerOverPlayerControls;
    private double _lastAudibleVolume = 100;
    private bool _windowedPaneVisible;
    private bool _windowedPaneToggleVisible;
    private bool _windowedSettingsVisible;
    private bool _windowedAlwaysShowHeader;

    private void RootNavigation_Loaded(object sender, RoutedEventArgs e)
    {
        if (_playerControlsInitialized)
        {
            return;
        }

        _playerControlsInitialized = true;
        _playerAttachRetryTimer.Tick += PlayerAttachRetryTimer_Tick;
        _playerControlsAutoHideTimer.Tick += PlayerControlsAutoHideTimer_Tick;
        UpdateVolumeUi(VolumeSlider.Value);
        UpdatePlayPauseUi(isPlaying: false);
        UpdateMuteUi(isMuted: false);
        UpdateFullscreenUi();

        EnsurePlayerEventsAttached();
    }

    private void PlayerAttachRetryTimer_Tick(object? sender, object e)
    {
        EnsurePlayerEventsAttached();
    }

    private void EnsurePlayerEventsAttached()
    {
        if (_playerEventsAttached)
        {
            _playerAttachRetryTimer.Stop();
            return;
        }

        if (_mediaPlayer is null)
        {
            _playerAttachRetryTimer.Start();
            return;
        }

        _playerAttachRetryTimer.Stop();
        _playerEventsAttached = true;

        _mediaPlayer.Opening += PlayerMediaPlayer_Opening;
        _mediaPlayer.Playing += PlayerMediaPlayer_Playing;
        _mediaPlayer.Paused += PlayerMediaPlayer_Paused;
        _mediaPlayer.Stopped += PlayerMediaPlayer_Stopped;
        _mediaPlayer.EndReached += PlayerMediaPlayer_EndReached;
        _mediaPlayer.Muted += PlayerMediaPlayer_Muted;
        _mediaPlayer.Unmuted += PlayerMediaPlayer_Unmuted;
        _mediaPlayer.VolumeChanged += PlayerMediaPlayer_VolumeChanged;

        _isUpdatingVolume = true;
        _mediaPlayer.Volume = (int)Math.Round(VolumeSlider.Value);
        _isUpdatingVolume = false;
        UpdateMuteUi(_mediaPlayer.Mute);
        UpdatePlayPauseUi(_mediaPlayer.IsPlaying);
    }

    private void PlayerMediaPlayer_Opening(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            PlayerOpeningIndicator.IsActive = true;
            PlayerOpeningIndicator.Visibility = Visibility.Visible;
            PlayerEmptyState.Visibility = Visibility.Collapsed;
            ShowPlayerControls();
        });

    private void PlayerMediaPlayer_Playing(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            PlayerOpeningIndicator.IsActive = false;
            PlayerOpeningIndicator.Visibility = Visibility.Collapsed;
            PlayerEmptyState.Visibility = Visibility.Collapsed;
            UpdatePlayPauseUi(isPlaying: true);
            StatusText.Text = _resources.GetString("StatusPlaying");
            RestartPlayerControlsAutoHideTimer();
        });

    private void PlayerMediaPlayer_Paused(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            PlayerOpeningIndicator.IsActive = false;
            PlayerOpeningIndicator.Visibility = Visibility.Collapsed;
            UpdatePlayPauseUi(isPlaying: false);
            StatusText.Text = _resources.GetString("StatusPaused");
            ShowPlayerControls();
        });

    private void PlayerMediaPlayer_Stopped(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_mediaPlayer?.State is VLCState.Opening or VLCState.Buffering or VLCState.Playing)
            {
                return;
            }

            PlayerOpeningIndicator.IsActive = false;
            PlayerOpeningIndicator.Visibility = Visibility.Collapsed;
            PlayerEmptyState.Visibility = Visibility.Visible;
            UpdatePlayPauseUi(isPlaying: false);
            ShowPlayerControls();
        });

    private void PlayerMediaPlayer_EndReached(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            PlayerOpeningIndicator.IsActive = false;
            PlayerOpeningIndicator.Visibility = Visibility.Collapsed;
            PlayerEmptyState.Visibility = Visibility.Visible;
            UpdatePlayPauseUi(isPlaying: false);
            ShowPlayerControls();
        });

    private void PlayerMediaPlayer_Muted(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() => UpdateMuteUi(isMuted: true));

    private void PlayerMediaPlayer_Unmuted(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() => UpdateMuteUi(isMuted: false));

    private void PlayerMediaPlayer_VolumeChanged(object? sender, MediaPlayerVolumeChangedEventArgs e) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_mediaPlayer is null)
            {
                return;
            }

            _isUpdatingVolume = true;
            VolumeSlider.Value = Math.Clamp(_mediaPlayer.Volume, 0, 100);
            _isUpdatingVolume = false;
            UpdateVolumeUi(VolumeSlider.Value);
            UpdateMuteUi(_mediaPlayer.Mute);
        });

    private void PlayerPlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        TogglePlayPause();
    }

    private void TogglePlayPause()
    {
        EnsurePlayerEventsAttached();
        if (_mediaPlayer is null)
        {
            StatusText.Text = _resources.GetString("StatusMediaNotReady");
            return;
        }

        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Pause();
            return;
        }

        if (_mediaPlayer.State == VLCState.Paused)
        {
            _mediaPlayer.Play();
            return;
        }

        if (_currentMedia is not null)
        {
            _mediaPlayer.Play(_currentMedia);
            return;
        }

        if (Uri.TryCreate(SourceTextBox.Text?.Trim(), UriKind.Absolute, out var source))
        {
            StartPlayback(source, null);
            return;
        }

        StatusText.Text = _resources.GetString("StatusInvalidSource");
    }

    private void PlayerStopControlButton_Click(object sender, RoutedEventArgs e)
    {
        StopPlayerFromControls();
    }

    private void StopPlayerFromControls()
    {
        if (_mediaPlayer is null)
        {
            return;
        }

        _mediaPlayer.Stop();
        PlayerOpeningIndicator.IsActive = false;
        PlayerOpeningIndicator.Visibility = Visibility.Collapsed;
        PlayerEmptyState.Visibility = Visibility.Visible;
        UpdatePlayPauseUi(isPlaying: false);
        StatusText.Text = _resources.GetString("StatusStopped");
        ShowPlayerControls();
    }

    private void PlayerMuteButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMute();
    }

    private void ToggleMute()
    {
        EnsurePlayerEventsAttached();
        if (_mediaPlayer is null)
        {
            return;
        }

        var shouldUnmute = _mediaPlayer.Mute || VolumeSlider.Value <= 0;
        if (shouldUnmute)
        {
            if (VolumeSlider.Value <= 0)
            {
                VolumeSlider.Value = Math.Clamp(_lastAudibleVolume, 1, 100);
            }

            _mediaPlayer.Mute = false;
        }
        else
        {
            _mediaPlayer.Mute = true;
        }

        UpdateMuteUi(_mediaPlayer.Mute);
        ShowPlayerControls();
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        UpdateVolumeUi(e.NewValue);
        if (!_playerControlsInitialized || _isUpdatingVolume)
        {
            return;
        }

        EnsurePlayerEventsAttached();
        if (_mediaPlayer is null)
        {
            return;
        }

        var volume = (int)Math.Round(Math.Clamp(e.NewValue, 0, 100));
        if (volume > 0)
        {
            _lastAudibleVolume = volume;
        }

        _mediaPlayer.Volume = volume;
        if (volume > 0 && _mediaPlayer.Mute)
        {
            _mediaPlayer.Mute = false;
        }

        UpdateMuteUi(_mediaPlayer.Mute);
        RestartPlayerControlsAutoHideTimer();
    }

    private void PlayerFullscreenButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleFullscreen();
    }

    private void LivePlayerHost_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        ToggleFullscreen();
        e.Handled = true;
    }

    private void ToggleFullscreen()
    {
        if (LiveView.Visibility != Visibility.Visible)
        {
            return;
        }

        SetFullscreen(!_isFullscreen);
    }

    private void SetFullscreen(bool fullscreen)
    {
        if (_isFullscreen == fullscreen)
        {
            return;
        }

        _isFullscreen = fullscreen;
        if (fullscreen)
        {
            _windowedPaneVisible = RootNavigation.IsPaneVisible;
            _windowedPaneToggleVisible = RootNavigation.IsPaneToggleButtonVisible;
            _windowedSettingsVisible = RootNavigation.IsSettingsVisible;
            _windowedAlwaysShowHeader = RootNavigation.AlwaysShowHeader;

            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            RootNavigation.IsPaneVisible = false;
            RootNavigation.IsPaneToggleButtonVisible = false;
            RootNavigation.IsSettingsVisible = false;
            RootNavigation.AlwaysShowHeader = false;
            StatusNavigationItem.Visibility = Visibility.Collapsed;
            ContentRoot.Padding = new Thickness(0);
            LiveSidebar.Visibility = Visibility.Collapsed;
            LiveSidebarColumn.Width = new GridLength(0);
            LiveView.ColumnSpacing = 0;
            LivePlayerGrid.RowSpacing = 0;
            SelectedChannelText.Visibility = Visibility.Collapsed;
            PlayerSurfaceBorder.CornerRadius = new CornerRadius(0);
            ShowPlayerControls();
            RestartPlayerControlsAutoHideTimer();
        }
        else
        {
            AppWindow.SetPresenter(AppWindowPresenterKind.Default);
            RootNavigation.IsPaneVisible = _windowedPaneVisible;
            RootNavigation.IsPaneToggleButtonVisible = _windowedPaneToggleVisible;
            RootNavigation.IsSettingsVisible = _windowedSettingsVisible;
            RootNavigation.AlwaysShowHeader = _windowedAlwaysShowHeader;
            StatusNavigationItem.Visibility = Visibility.Visible;
            ContentRoot.Padding = new Thickness(24, 12, 24, 24);
            LiveSidebar.Visibility = Visibility.Visible;
            LiveSidebarColumn.Width = new GridLength(360);
            LiveView.ColumnSpacing = 16;
            LivePlayerGrid.RowSpacing = 12;
            SelectedChannelText.Visibility = Visibility.Visible;
            PlayerSurfaceBorder.CornerRadius = new CornerRadius(8);
            _playerControlsAutoHideTimer.Stop();
            PlayerControlOverlay.Visibility = Visibility.Visible;
        }

        UpdateFullscreenUi();
    }

    private void FullscreenKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        ToggleFullscreen();
        args.Handled = true;
    }

    private void ExitFullscreenKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!_isFullscreen)
        {
            return;
        }

        SetFullscreen(fullscreen: false);
        args.Handled = true;
    }

    private void RootNavigation_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox or ComboBox or Slider or CalendarDatePicker)
        {
            return;
        }

        if (e.Key == VirtualKey.Space && e.OriginalSource is ButtonBase)
        {
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Space:
                TogglePlayPause();
                e.Handled = true;
                break;
            case VirtualKey.M:
                ToggleMute();
                e.Handled = true;
                break;
            case VirtualKey.Up:
                ChangeVolume(5);
                e.Handled = true;
                break;
            case VirtualKey.Down:
                ChangeVolume(-5);
                e.Handled = true;
                break;
        }
    }

    private void ChangeVolume(double delta)
    {
        VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + delta, 0, 100);
        ShowPlayerControls();
    }

    private void LivePlayerHost_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        ShowPlayerControls();
        RestartPlayerControlsAutoHideTimer();
    }

    private void PlayerControlOverlay_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOverPlayerControls = true;
        _playerControlsAutoHideTimer.Stop();
        PlayerControlOverlay.Visibility = Visibility.Visible;
    }

    private void PlayerControlOverlay_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOverPlayerControls = false;
        RestartPlayerControlsAutoHideTimer();
    }

    private void PlayerControlsAutoHideTimer_Tick(object? sender, object e)
    {
        _playerControlsAutoHideTimer.Stop();
        if (_isFullscreen &&
            !_isPointerOverPlayerControls &&
            _mediaPlayer?.IsPlaying == true)
        {
            PlayerControlOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void RestartPlayerControlsAutoHideTimer()
    {
        _playerControlsAutoHideTimer.Stop();
        if (_isFullscreen &&
            !_isPointerOverPlayerControls &&
            _mediaPlayer?.IsPlaying == true)
        {
            _playerControlsAutoHideTimer.Start();
        }
    }

    private void ShowPlayerControls()
    {
        PlayerControlOverlay.Visibility = Visibility.Visible;
    }

    private void UpdatePlayPauseUi(bool isPlaying)
    {
        PlayerPlayPauseIcon.Glyph = isPlaying ? "\uE769" : "\uE768";
        var resourceKey = isPlaying ? "PlayerPauseLabel" : "PlayerPlayLabel";
        var label = _resources.GetString(resourceKey);
        ToolTipService.SetToolTip(PlayerPlayPauseButton, label);
        PlayerPlayPauseButton.SetValue(AutomationProperties.NameProperty, label);
    }

    private void UpdateMuteUi(bool isMuted)
    {
        PlayerMuteIcon.Glyph = isMuted || VolumeSlider.Value <= 0 ? "\uE74F" : "\uE767";
        var resourceKey = isMuted || VolumeSlider.Value <= 0 ? "PlayerUnmuteLabel" : "PlayerMuteLabel";
        var label = _resources.GetString(resourceKey);
        ToolTipService.SetToolTip(PlayerMuteButton, label);
        PlayerMuteButton.SetValue(AutomationProperties.NameProperty, label);
    }

    private void UpdateFullscreenUi()
    {
        PlayerFullscreenIcon.Glyph = _isFullscreen ? "\uE73F" : "\uE740";
        var resourceKey = _isFullscreen ? "PlayerExitFullscreenLabel" : "PlayerEnterFullscreenLabel";
        var label = _resources.GetString(resourceKey);
        ToolTipService.SetToolTip(PlayerFullscreenButton, label);
        PlayerFullscreenButton.SetValue(AutomationProperties.NameProperty, label);
    }

    private void UpdateVolumeUi(double value)
    {
        if (VolumeValueText is null)
        {
            return;
        }

        VolumeValueText.Text = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            "{0:0}%",
            Math.Clamp(value, 0, 100));
    }

    private void RootNavigation_Unloaded(object sender, RoutedEventArgs e)
    {
        _playerControlsInitialized = false;
        _playerAttachRetryTimer.Stop();
        _playerControlsAutoHideTimer.Stop();
        _playerAttachRetryTimer.Tick -= PlayerAttachRetryTimer_Tick;
        _playerControlsAutoHideTimer.Tick -= PlayerControlsAutoHideTimer_Tick;
        if (_mediaPlayer is null || !_playerEventsAttached)
        {
            return;
        }

        _mediaPlayer.Opening -= PlayerMediaPlayer_Opening;
        _mediaPlayer.Playing -= PlayerMediaPlayer_Playing;
        _mediaPlayer.Paused -= PlayerMediaPlayer_Paused;
        _mediaPlayer.Stopped -= PlayerMediaPlayer_Stopped;
        _mediaPlayer.EndReached -= PlayerMediaPlayer_EndReached;
        _mediaPlayer.Muted -= PlayerMediaPlayer_Muted;
        _mediaPlayer.Unmuted -= PlayerMediaPlayer_Unmuted;
        _mediaPlayer.VolumeChanged -= PlayerMediaPlayer_VolumeChanged;
        _playerEventsAttached = false;
    }
}
