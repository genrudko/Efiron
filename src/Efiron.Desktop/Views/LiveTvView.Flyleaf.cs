using Efiron.Domain.Playback;
using Efiron.Playback;
using FlyleafLib.Controls.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Efiron.Desktop.Views;

public sealed partial class LiveTvView
{
    private FlyleafHost? _flyleafSurface;
    private bool _flyleafPlaybackEnabled;

    internal void EnableFlyleafPlayback()
    {
        if (_flyleafPlaybackEnabled)
        {
            return;
        }

        if (PlayerSurfaceBorder.Child is not Grid playerSurface ||
            _playbackBackendSelector is null)
        {
            throw new InvalidOperationException(
                "Playback backend controls must exist before Flyleaf is enabled.");
        }

        _flyleafSurface = new FlyleafHost
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            KeyBindings = false,
        };
        playerSurface.Children.Insert(
            Math.Min(3, playerSurface.Children.Count),
            _flyleafSurface);

        _playbackBackendSelector.SelectionChanged -=
            NativePlaybackBackendSelector_SelectionChanged;
        _playbackBackendSelector.Items.Insert(
            Math.Min(4, _playbackBackendSelector.Items.Count),
            CreateBackendOption(
                "Flyleaf DirectX (эксп.)",
                PlaybackBackendId.Flyleaf));
        _playbackBackendSelector.SelectionChanged +=
            RepairKPlaybackBackendSelector_SelectionChanged;
        _flyleafPlaybackEnabled = true;
    }

    private async void RepairKPlaybackBackendSelector_SelectionChanged(
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

        if (selected != PlaybackBackendId.Flyleaf)
        {
            HideFlyleafSurface();
            NativePlaybackBackendSelector_SelectionChanged(sender, e);
            return;
        }

        _selectedPlaybackBackend = selected;
        HideNativePlaybackHost();
        ApplyFlyleafProfileSelectorVisibility();
        await SwitchToFlyleafAsync(restartCurrentRequest: true);
    }

    private void ApplyFlyleafProfileSelectorVisibility()
    {
        if (_libVlcProfileSelector is not null)
        {
            _libVlcProfileSelector.Visibility = Visibility.Collapsed;
        }

        if (_mpvProfileSelector is not null)
        {
            _mpvProfileSelector.Visibility = Visibility.Collapsed;
        }
    }

    private async Task SwitchToFlyleafAsync(bool restartCurrentRequest)
    {
        await _playbackBackendSwitchLock.WaitAsync();
        try
        {
            if (_playbackBackendControllerDisposed || _flyleafSurface is null)
            {
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
            HideFlyleafSurface();
            ReleaseCurrentPlaybackBackend();

            var backend = new FlyleafPlaybackBackend();
            _playbackBackend = backend;
            _playbackSession = backend.Session;
            _playbackSession.SetVolume(Math.Clamp(volume, 0, 100));
            _playbackSession.SetMuted(isMuted);
            _playbackSession.SnapshotChanged += PlaybackSession_SnapshotChanged;
            BindFlyleafSurface(backend);
            _playbackDiagnosticsWriter.Attach(backend);
            UpdatePlaybackBackendStatus(
                $"Flyleaf DirectX · {backend.SelectedProfile}");

            if (request is not null)
            {
                _pendingPlaybackRequest = request;
                await _playbackSession.PlayAsync(request);
                _pendingPlaybackRequest = null;
            }
            else
            {
                ApplyPlaybackSnapshot(_playbackSession.Snapshot);
            }
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            HideFlyleafSurface();
            UpdatePlaybackBackendStatus($"Ошибка Flyleaf: {exception.Message}");
            UpdatePlaybackStatus(
                PlaybackState.Failed,
                _resources.GetString("PlaybackStatusFailedMessage"));
        }
        finally
        {
            _playbackBackendSwitchLock.Release();
        }
    }

    private void BindFlyleafSurface(FlyleafPlaybackBackend backend)
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

        if (_flyleafSurface is not null)
        {
            _flyleafSurface.Player = backend.Player;
            _flyleafSurface.Visibility = Visibility.Visible;
        }
    }

    private void HideFlyleafSurface()
    {
        if (_flyleafSurface is null)
        {
            return;
        }

        _flyleafSurface.Visibility = Visibility.Collapsed;
        _flyleafSurface.Player = null;
    }
}
