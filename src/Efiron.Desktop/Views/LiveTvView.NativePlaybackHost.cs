using Efiron.Application.Playback;
using Efiron.Desktop.Playback;
using Efiron.Domain.Playback;
using Efiron.Playback;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;

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
        LiveRoot.KeyDown += NativePlaybackHost_KeyDown;
        ConfigureNativePlaybackSelector();
        _nativePlaybackHostAttached = true;
        UpdateNativePlaybackHostBounds();
    }

    private void ConfigureNativePlaybackSelector()
    {
        if (_playbackBackendSelector is null || _mpvProfileSelector is null)
        {
            throw new InvalidOperationException(
                "Playback backend controls were not initialized.");
        }

        _playbackBackendSelector.SelectionChanged -=
            PlaybackBackendSelector_SelectionChanged;
        _playbackBackendSelector.Items.Insert(
            Math.Min(3, _playbackBackendSelector.Items.Count),
            CreateBackendOption(
                "mpv Native Host (эксп.)",
                PlaybackBackendId.MpvHost));
        _playbackBackendSelector.SelectionChanged +=
            NativePlaybackBackendSelector_SelectionChanged;
        _mpvProfileSelector.SelectionChanged +=
            NativeHostMpvProfileSelector_SelectionChanged;
    }

    private async void NativePlaybackBackendSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_updatingPlaybackBackendSelectors ||
            _playbackBackendSelector?.SelectedItem is not ComboBoxItem
            {
                Tag: PlaybackBackendId selected,
            })
        {
            return;
        }

        _selectedPlaybackBackend = selected;
        if (selected == PlaybackBackendId.MpvHost)
        {
            ApplyNativeHostProfileSelectorVisibility();
            await SwitchToNativePlaybackHostAsync(restartCurrentRequest: true);
            return;
        }

        HideNativePlaybackHost();
        UpdateProfileSelectorVisibility();
        await SwitchPlaybackBackendAsync(restartCurrentRequest: true);
    }

    private async void NativeHostMpvProfileSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_updatingPlaybackBackendSelectors ||
            _selectedPlaybackBackend != PlaybackBackendId.MpvHost ||
            _mpvProfileSelector?.SelectedItem is not ComboBoxItem
            {
                Tag: MpvPlaybackProfile selected,
            })
        {
            return;
        }

        _selectedMpvProfile = selected;
        await SwitchToNativePlaybackHostAsync(restartCurrentRequest: true);
    }

    private void NativePlaybackHost_KeyDown(
        object sender,
        KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.F8 && TryLeaveNativePlaybackHost())
        {
            e.Handled = true;
        }
    }

    private void ApplyNativeHostProfileSelectorVisibility()
    {
        if (_libVlcProfileSelector is not null)
        {
            _libVlcProfileSelector.Visibility = Visibility.Collapsed;
        }

        if (_mpvProfileSelector is not null)
        {
            _mpvProfileSelector.Visibility = Visibility.Visible;
        }
    }

    private async Task SwitchToNativePlaybackHostAsync(
        bool restartCurrentRequest)
    {
        await _playbackBackendSwitchLock.WaitAsync();
        try
        {
            if (_playbackBackendControllerDisposed)
            {
                return;
            }

            if (NativePlaybackHostHandle == 0)
            {
                UpdatePlaybackBackendStatus(
                    "Ожидание native playback host…");
                return;
            }

            var request = restartCurrentRequest
                ? _currentPlaybackRequest ?? _pendingPlaybackRequest
                : null;
            var previousSnapshot = _playbackSession?.Snapshot;
            var volume = previousSnapshot?.Volume ??
                (int)Math.Round(VolumeSlider.Value);
            var isMuted = previousSnapshot?.IsMuted ?? false;

            await _playbackDiagnosticsWriter.DetachAsync();
            HideNativePlaybackHost();
            ReleaseCurrentPlaybackBackend();

            _playbackBackend = new MpvProcessPlaybackBackend(
                NativePlaybackHostHandle,
                _selectedMpvProfile);
            _playbackSession = _playbackBackend.Session;
            _playbackSession.SetVolume(Math.Clamp(volume, 0, 100));
            _playbackSession.SetMuted(isMuted);
            _playbackSession.SnapshotChanged += PlaybackSession_SnapshotChanged;
            BindNativePlaybackHostSurface();
            _playbackDiagnosticsWriter.Attach(_playbackBackend);
            UpdatePlaybackBackendStatus(
                $"mpv Native Host · {_playbackBackend.SelectedProfile} · F8: выйти");

            if (request is not null)
            {
                _pendingPlaybackRequest = request;
                await _playbackSession.PlayAsync(request);
                _pendingPlaybackRequest = null;
                UpdateNativePlaybackHostBounds();
            }
            else
            {
                ApplyPlaybackSnapshot(_playbackSession.Snapshot);
            }
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            HideNativePlaybackHost();
            UpdatePlaybackBackendStatus($"Ошибка Native Host: {exception.Message}");
            UpdatePlaybackStatus(
                PlaybackState.Failed,
                _resources.GetString("PlaybackStatusFailedMessage"));
        }
        finally
        {
            _playbackBackendSwitchLock.Release();
        }
    }

    private void BindNativePlaybackHostSurface()
    {
        VideoView.MediaPlayer = null;
        VideoView.Visibility = Visibility.Collapsed;
        ClearMpvSwapChain();
        if (_mpvSurface is not null)
        {
            _mpvSurface.Visibility = Visibility.Collapsed;
        }

        if (_windowsMediaSurface is not null)
        {
            _windowsMediaSurface.Source = null;
            _windowsMediaSurface.SetMediaPlayer(null!);
            _windowsMediaSurface.Visibility = Visibility.Collapsed;
        }

        UpdateNativePlaybackHostBounds();
    }

    private bool TryLeaveNativePlaybackHost()
    {
        if (_selectedPlaybackBackend != PlaybackBackendId.MpvHost ||
            _playbackBackendSelector is null)
        {
            return false;
        }

        for (var index = 0;
             index < _playbackBackendSelector.Items.Count;
             index++)
        {
            if (_playbackBackendSelector.Items[index] is ComboBoxItem
                {
                    Tag: PlaybackBackendId.LibVlc,
                })
            {
                _playbackBackendSelector.SelectedIndex = index;
                return true;
            }
        }

        return false;
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

        HideNativePlaybackHost();
        PlayerSurfaceBorder.LayoutUpdated -=
            NativePlaybackHost_PlayerSurfaceLayoutUpdated;
        LiveRoot.KeyDown -= NativePlaybackHost_KeyDown;
        if (_nativePlaybackVisibilityCallbackToken != 0)
        {
            UnregisterPropertyChangedCallback(
                VisibilityProperty,
                _nativePlaybackVisibilityCallbackToken);
            _nativePlaybackVisibilityCallbackToken = 0;
        }

        if (_playbackBackendSelector is not null)
        {
            _playbackBackendSelector.SelectionChanged -=
                NativePlaybackBackendSelector_SelectionChanged;
        }
        if (_mpvProfileSelector is not null)
        {
            _mpvProfileSelector.SelectionChanged -=
                NativeHostMpvProfileSelector_SelectionChanged;
        }

        _nativePlaybackHost?.Dispose();
        _nativePlaybackHost = null;
        _nativePlaybackHostAttached = false;
    }
}
