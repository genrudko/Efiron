using System.Runtime.InteropServices;
using Efiron.Application.Playback;
using Efiron.Desktop.Diagnostics;
using Efiron.Domain.Playback;
using Efiron.Playback;
using LibVLCSharp.Platforms.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT;

namespace Efiron.Desktop.Views;

public sealed partial class LiveTvView
{
    private readonly SemaphoreSlim _playbackBackendSwitchLock = new(1, 1);
    private readonly PlaybackDiagnosticsWriter _playbackDiagnosticsWriter = new(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Efiron",
            "diagnostics"));

    private MediaPlayerElement? _windowsMediaSurface;
    private SwapChainPanel? _mpvSurface;
    private Border? _playbackBackendPanel;
    private ComboBox? _playbackBackendSelector;
    private ComboBox? _libVlcProfileSelector;
    private ComboBox? _mpvProfileSelector;
    private TextBlock? _playbackBackendStatus;
    private PlaybackBackendId _selectedPlaybackBackend = PlaybackBackendId.Auto;
    private LibVlcPlaybackProfile _selectedLibVlcProfile = LibVlcPlaybackProfile.Auto;
    private MpvPlaybackProfile _selectedMpvProfile = MpvPlaybackProfile.Auto;
    private bool _updatingPlaybackBackendSelectors;
    private bool _playbackBackendControllerDisposed;

    private void InitializePlaybackBackendController()
    {
        if (PlayerSurfaceBorder.Child is not Grid playerSurface)
        {
            throw new InvalidOperationException(
                "The player surface must be a Grid for switchable playback backends.");
        }

        _windowsMediaSurface = new MediaPlayerElement
        {
            AreTransportControlsEnabled = false,
            AutoPlay = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        playerSurface.Children.Insert(1, _windowsMediaSurface);

        _mpvSurface = new SwapChainPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        _mpvSurface.SizeChanged += MpvSurface_SizeChanged;
        playerSurface.Children.Insert(2, _mpvSurface);

        _playbackBackendSelector = new ComboBox
        {
            MinWidth = 142,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _playbackBackendSelector.Items.Add(CreateBackendOption(
            "Автоматически",
            PlaybackBackendId.Auto));
        _playbackBackendSelector.Items.Add(CreateBackendOption(
            "LibVLC",
            PlaybackBackendId.LibVlc));
        _playbackBackendSelector.Items.Add(CreateBackendOption(
            "mpv",
            PlaybackBackendId.Mpv));
        _playbackBackendSelector.Items.Add(CreateBackendOption(
            "Windows Media",
            PlaybackBackendId.WindowsMedia));
        _playbackBackendSelector.SelectionChanged +=
            PlaybackBackendSelector_SelectionChanged;

        _libVlcProfileSelector = new ComboBox
        {
            MinWidth = 112,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _libVlcProfileSelector.Items.Add(CreateProfileOption(
            "Auto",
            LibVlcPlaybackProfile.Auto));
        _libVlcProfileSelector.Items.Add(CreateProfileOption(
            "D3D11VA",
            LibVlcPlaybackProfile.D3D11Va));
        _libVlcProfileSelector.Items.Add(CreateProfileOption(
            "DXVA2",
            LibVlcPlaybackProfile.Dxva2));
        _libVlcProfileSelector.Items.Add(CreateProfileOption(
            "Software",
            LibVlcPlaybackProfile.Software));
        _libVlcProfileSelector.SelectionChanged +=
            LibVlcProfileSelector_SelectionChanged;

        _mpvProfileSelector = new ComboBox
        {
            MinWidth = 132,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Visibility = Visibility.Collapsed,
        };
        _mpvProfileSelector.Items.Add(CreateMpvProfileOption(
            "Auto",
            MpvPlaybackProfile.Auto));
        _mpvProfileSelector.Items.Add(CreateMpvProfileOption(
            "Smooth Motion",
            MpvPlaybackProfile.SmoothMotion));
        _mpvProfileSelector.SelectionChanged +=
            MpvProfileSelector_SelectionChanged;

        var diagnosticsButton = new Button
        {
            Content = "Снимок",
            Padding = new Thickness(10, 5, 10, 5),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        diagnosticsButton.Click += DiagnosticsButton_Click;

        _playbackBackendStatus = new TextBlock
        {
            Text = "LibVLC · Auto",
            FontSize = 11,
            Opacity = 0.78,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var panel = new StackPanel
        {
            Spacing = 6,
        };
        panel.Children.Add(_playbackBackendSelector);
        panel.Children.Add(_libVlcProfileSelector);
        panel.Children.Add(_mpvProfileSelector);
        panel.Children.Add(diagnosticsButton);
        panel.Children.Add(_playbackBackendStatus);

        _playbackBackendPanel = new Border
        {
            Padding = new Thickness(8),
            Margin = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Background = ResolvePlaybackBrush("EfironSurfaceRaisedBrush"),
            BorderBrush = ResolvePlaybackBrush("EfironStrokeSubtleBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Child = panel,
        };
        playerSurface.Children.Add(_playbackBackendPanel);

        _updatingPlaybackBackendSelectors = true;
        _playbackBackendSelector.SelectedIndex = 0;
        _libVlcProfileSelector.SelectedIndex = 0;
        _mpvProfileSelector.SelectedIndex = 0;
        _updatingPlaybackBackendSelectors = false;
    }

    private async Task EnsurePlaybackBackendAsync()
    {
        if (_playbackBackendControllerDisposed)
        {
            return;
        }

        await SwitchPlaybackBackendAsync(restartCurrentRequest: true);
    }

    private async Task SwitchPlaybackBackendAsync(bool restartCurrentRequest)
    {
        await _playbackBackendSwitchLock.WaitAsync();
        try
        {
            if (_playbackBackendControllerDisposed)
            {
                return;
            }

            var effectiveBackend = _selectedPlaybackBackend == PlaybackBackendId.Auto
                ? PlaybackBackendId.LibVlc
                : _selectedPlaybackBackend;
            if (effectiveBackend == PlaybackBackendId.LibVlc &&
                _libVlcInitialization is null)
            {
                UpdatePlaybackBackendStatus("Ожидание LibVLC surface…");
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
            ReleaseCurrentPlaybackBackend();

            _playbackBackend = effectiveBackend switch
            {
                PlaybackBackendId.Mpv =>
                    new MpvPlaybackBackend(_selectedMpvProfile),
                PlaybackBackendId.WindowsMedia =>
                    new WindowsMediaPlaybackBackend(),
                _ => new LibVlcPlaybackBackend(
                    _libVlcInitialization!,
                    _selectedLibVlcProfile),
            };
            _playbackSession = _playbackBackend.Session;
            _playbackSession.SetVolume(Math.Clamp(volume, 0, 100));
            _playbackSession.SetMuted(isMuted);
            _playbackSession.SnapshotChanged += PlaybackSession_SnapshotChanged;
            BindPlaybackSurface(_playbackBackend);
            _playbackDiagnosticsWriter.Attach(_playbackBackend);
            UpdatePlaybackBackendStatus(
                $"{_playbackBackend.Id} · {_playbackBackend.SelectedProfile}");

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
            UpdatePlaybackBackendStatus($"Ошибка: {exception.Message}");
            UpdatePlaybackStatus(
                PlaybackState.Failed,
                _resources.GetString("PlaybackStatusFailedMessage"));
        }
        finally
        {
            _playbackBackendSwitchLock.Release();
        }
    }

    private void BindPlaybackSurface(IPlaybackBackend backend)
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

        switch (backend)
        {
            case LibVlcPlaybackBackend libVlc:
                VideoView.MediaPlayer = libVlc.MediaPlayer;
                VideoView.Visibility = Visibility.Visible;
                break;
            case MpvPlaybackBackend mpv when _mpvSurface is not null:
                _mpvSurface.Visibility = Visibility.Visible;
                mpv.DisplaySwapChainChanged += MpvBackend_DisplaySwapChainChanged;
                UpdateMpvCompositionSize(mpv);
                AttachMpvSwapChain(mpv.DisplaySwapChain);
                break;
            case WindowsMediaPlaybackBackend windowsMedia
                when _windowsMediaSurface is not null:
                _windowsMediaSurface.SetMediaPlayer(windowsMedia.MediaPlayer);
                _windowsMediaSurface.Visibility = Visibility.Visible;
                break;
            default:
                throw new NotSupportedException(
                    $"Playback backend '{backend.Id}' has no desktop surface.");
        }
    }

    private void ReleaseCurrentPlaybackBackend()
    {
        if (_playbackSession is not null)
        {
            _playbackSession.SnapshotChanged -= PlaybackSession_SnapshotChanged;
            try
            {
                _playbackSession.Stop();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        if (_playbackBackend is MpvPlaybackBackend mpv)
        {
            mpv.DisplaySwapChainChanged -= MpvBackend_DisplaySwapChainChanged;
        }

        ClearMpvSwapChain();
        if (_mpvSurface is not null)
        {
            _mpvSurface.Visibility = Visibility.Collapsed;
        }

        VideoView.MediaPlayer = null;
        VideoView.Visibility = Visibility.Visible;
        if (_windowsMediaSurface is not null)
        {
            _windowsMediaSurface.Source = null;
            _windowsMediaSurface.SetMediaPlayer(null!);
            _windowsMediaSurface.Visibility = Visibility.Collapsed;
        }

        _playbackBackend?.Dispose();
        _playbackBackend = null;
        _playbackSession = null;
    }

    private void DisposePlaybackBackendController()
    {
        if (_playbackBackendControllerDisposed)
        {
            return;
        }

        _playbackBackendControllerDisposed = true;
        if (_mpvSurface is not null)
        {
            _mpvSurface.SizeChanged -= MpvSurface_SizeChanged;
        }

        _playbackDiagnosticsWriter.DetachAsync().GetAwaiter().GetResult();
        ReleaseCurrentPlaybackBackend();
        _playbackDiagnosticsWriter.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _playbackBackendSwitchLock.Dispose();
    }

    private void SetPlaybackBackendPanelFullscreen(bool isFullscreen)
    {
        if (_playbackBackendPanel is not null)
        {
            _playbackBackendPanel.Visibility = isFullscreen
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }

    private async void PlaybackBackendSelector_SelectionChanged(
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
        UpdateProfileSelectorVisibility();
        await SwitchPlaybackBackendAsync(restartCurrentRequest: true);
    }

    private async void LibVlcProfileSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_updatingPlaybackBackendSelectors ||
            _selectedPlaybackBackend is PlaybackBackendId.Mpv or
                PlaybackBackendId.WindowsMedia ||
            _libVlcProfileSelector?.SelectedItem is not ComboBoxItem
            {
                Tag: LibVlcPlaybackProfile selected,
            })
        {
            return;
        }

        _selectedLibVlcProfile = selected;
        await SwitchPlaybackBackendAsync(restartCurrentRequest: true);
    }

    private async void MpvProfileSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_updatingPlaybackBackendSelectors ||
            _selectedPlaybackBackend != PlaybackBackendId.Mpv ||
            _mpvProfileSelector?.SelectedItem is not ComboBoxItem
            {
                Tag: MpvPlaybackProfile selected,
            })
        {
            return;
        }

        _selectedMpvProfile = selected;
        await SwitchPlaybackBackendAsync(restartCurrentRequest: true);
    }

    private void UpdateProfileSelectorVisibility()
    {
        if (_libVlcProfileSelector is not null)
        {
            _libVlcProfileSelector.Visibility =
                _selectedPlaybackBackend is PlaybackBackendId.Auto or
                    PlaybackBackendId.LibVlc
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        if (_mpvProfileSelector is not null)
        {
            _mpvProfileSelector.Visibility =
                _selectedPlaybackBackend == PlaybackBackendId.Mpv
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
    }

    private async void DiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        await _playbackDiagnosticsWriter.RecordNowAsync();
        UpdatePlaybackBackendStatus(
            $"{_playbackBackend?.Id ?? PlaybackBackendId.Auto} · снимок сохранён");
    }

    private void MpvBackend_DisplaySwapChainChanged(
        object? sender,
        EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (ReferenceEquals(sender, _playbackBackend) &&
                sender is MpvPlaybackBackend mpv)
            {
                UpdateMpvCompositionSize(mpv);
                AttachMpvSwapChain(mpv.DisplaySwapChain);
            }
        });
    }

    private void MpvSurface_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_playbackBackend is MpvPlaybackBackend mpv)
        {
            UpdateMpvCompositionSize(mpv);
        }
    }

    private void UpdateMpvCompositionSize(MpvPlaybackBackend mpv)
    {
        if (_mpvSurface is null ||
            _mpvSurface.ActualWidth <= 0 ||
            _mpvSurface.ActualHeight <= 0)
        {
            return;
        }

        var scale = _mpvSurface.XamlRoot?.RasterizationScale ?? 1d;
        var width = Math.Max(1, (int)Math.Round(_mpvSurface.ActualWidth * scale));
        var height = Math.Max(1, (int)Math.Round(_mpvSurface.ActualHeight * scale));
        mpv.SetCompositionSize(width, height);
    }

    private void AttachMpvSwapChain(nint swapChain)
    {
        if (_mpvSurface is null)
        {
            return;
        }

        var nativePanel = _mpvSurface.As<ISwapChainPanelNative>();
        Marshal.ThrowExceptionForHR(nativePanel.SetSwapChain(swapChain));
    }

    private void ClearMpvSwapChain()
    {
        if (_mpvSurface is null)
        {
            return;
        }

        try
        {
            AttachMpvSwapChain(0);
        }
        catch (Exception) when (_playbackBackendControllerDisposed)
        {
        }
    }

    private void UpdatePlaybackBackendStatus(string text)
    {
        if (_playbackBackendStatus is not null)
        {
            _playbackBackendStatus.Text = text;
        }
    }

    private static ComboBoxItem CreateBackendOption(
        string text,
        PlaybackBackendId id) =>
        new()
        {
            Content = text,
            Tag = id,
        };

    private static ComboBoxItem CreateProfileOption(
        string text,
        LibVlcPlaybackProfile profile) =>
        new()
        {
            Content = text,
            Tag = profile,
        };

    private static ComboBoxItem CreateMpvProfileOption(
        string text,
        MpvPlaybackProfile profile) =>
        new()
        {
            Content = text,
            Tag = profile,
        };

    private static Brush ResolvePlaybackBrush(string key) =>
        Microsoft.UI.Xaml.Application.Current.Resources[key] as Brush ??
        new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    [ComImport]
    [Guid("63AAD0B8-7C24-40FF-85A8-640D944CC325")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISwapChainPanelNative
    {
        [PreserveSig]
        int SetSwapChain(nint swapChain);
    }
}
