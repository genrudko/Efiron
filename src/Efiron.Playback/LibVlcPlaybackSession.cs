using Efiron.Application.Playback;
using Efiron.Domain.Playback;
using LibVLCSharp.Platforms.Windows;
using LibVLCSharp.Shared;

namespace Efiron.Playback;

public sealed class LibVlcPlaybackSession : IPlaybackSession
{
    private readonly object _sync = new();
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;

    private Media? _currentMedia;
    private PlaybackSnapshot _snapshot = PlaybackSnapshot.Idle;
    private int _requestedVolume;
    private bool _requestedMuted;
    private bool _disposed;

    public LibVlcPlaybackSession(
        InitializedEventArgs initialization,
        bool enableDebugLogs = false)
    {
        ArgumentNullException.ThrowIfNull(initialization);

        _libVlc = new LibVLC(
            enableDebugLogs,
            initialization.SwapChainOptions);
        _mediaPlayer = new MediaPlayer(_libVlc);
        _requestedVolume = Math.Clamp(_mediaPlayer.Volume, 0, 100);
        _requestedMuted = _mediaPlayer.Mute;
        AttachEvents();
        Publish(_snapshot with
        {
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
        });
    }

    public event EventHandler<PlaybackSnapshotChangedEventArgs>? SnapshotChanged;

    public PlaybackSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return _snapshot;
            }
        }
    }

    public MediaPlayer MediaPlayer => _mediaPlayer;

    public ValueTask PlayAsync(
        PlaybackRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var media = new Media(_libVlc, request.Source);
        ApplyPlaybackOptions(media, request.Directives);

        var previousMedia = Interlocked.Exchange(ref _currentMedia, media);
        Publish(Snapshot with
        {
            State = PlaybackState.Opening,
            Source = request.Source,
            ChannelStableId = request.ChannelStableId,
            DisplayName = request.DisplayName,
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
            ErrorMessage = null,
        });

        try
        {
            _mediaPlayer.Play(media);
            ApplyRequestedAudioState();
            previousMedia?.Dispose();
            return ValueTask.CompletedTask;
        }
        catch
        {
            Interlocked.CompareExchange(ref _currentMedia, previousMedia, media);
            media.Dispose();
            Publish(Snapshot with
            {
                State = PlaybackState.Failed,
                Volume = _requestedVolume,
                IsMuted = _requestedMuted,
                ErrorMessage = "LibVLC rejected the playback request.",
            });
            throw;
        }
    }

    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Pause();
        }
    }

    public void Resume()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_currentMedia is not null &&
            Snapshot.State is PlaybackState.Paused or PlaybackState.Stopped)
        {
            _mediaPlayer.Play();
            ApplyRequestedAudioState();
        }
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _mediaPlayer.Stop();
        Publish(Snapshot with
        {
            State = PlaybackState.Stopped,
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
            ErrorMessage = null,
        });
    }

    public void SetMuted(bool isMuted)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _requestedMuted = isMuted;
        _mediaPlayer.Mute = isMuted;
        Publish(Snapshot with
        {
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
        });
    }

    public void SetVolume(int volume)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfLessThan(volume, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(volume, 100);

        _requestedVolume = volume;
        _mediaPlayer.Volume = volume;
        Publish(Snapshot with
        {
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DetachEvents();

        try
        {
            _mediaPlayer.Stop();
        }
        catch (ObjectDisposedException)
        {
        }

        Interlocked.Exchange(ref _currentMedia, null)?.Dispose();
        _mediaPlayer.Dispose();
        _libVlc.Dispose();

        Publish(Snapshot with
        {
            State = PlaybackState.Disposed,
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
            ErrorMessage = null,
        });
    }

    private void AttachEvents()
    {
        _mediaPlayer.Opening += MediaPlayer_Opening;
        _mediaPlayer.Playing += MediaPlayer_Playing;
        _mediaPlayer.Paused += MediaPlayer_Paused;
        _mediaPlayer.Stopped += MediaPlayer_Stopped;
        _mediaPlayer.EndReached += MediaPlayer_EndReached;
        _mediaPlayer.EncounteredError += MediaPlayer_EncounteredError;
        _mediaPlayer.Muted += MediaPlayer_Muted;
        _mediaPlayer.Unmuted += MediaPlayer_Unmuted;
        _mediaPlayer.VolumeChanged += MediaPlayer_VolumeChanged;
    }

    private void DetachEvents()
    {
        _mediaPlayer.Opening -= MediaPlayer_Opening;
        _mediaPlayer.Playing -= MediaPlayer_Playing;
        _mediaPlayer.Paused -= MediaPlayer_Paused;
        _mediaPlayer.Stopped -= MediaPlayer_Stopped;
        _mediaPlayer.EndReached -= MediaPlayer_EndReached;
        _mediaPlayer.EncounteredError -= MediaPlayer_EncounteredError;
        _mediaPlayer.Muted -= MediaPlayer_Muted;
        _mediaPlayer.Unmuted -= MediaPlayer_Unmuted;
        _mediaPlayer.VolumeChanged -= MediaPlayer_VolumeChanged;
    }

    private void MediaPlayer_Opening(object? sender, EventArgs e)
    {
        ApplyRequestedAudioState();
        Publish(Snapshot with
        {
            State = PlaybackState.Opening,
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
            ErrorMessage = null,
        });
    }

    private void MediaPlayer_Playing(object? sender, EventArgs e)
    {
        ApplyRequestedAudioState();
        Publish(Snapshot with
        {
            State = PlaybackState.Playing,
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
            ErrorMessage = null,
        });
    }

    private void MediaPlayer_Paused(object? sender, EventArgs e) =>
        Publish(Snapshot with
        {
            State = PlaybackState.Paused,
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
            ErrorMessage = null,
        });

    private void MediaPlayer_Stopped(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        Publish(Snapshot with
        {
            State = PlaybackState.Stopped,
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
            ErrorMessage = null,
        });
    }

    private void MediaPlayer_EndReached(object? sender, EventArgs e) =>
        Publish(Snapshot with
        {
            State = PlaybackState.Ended,
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
            ErrorMessage = null,
        });

    private void MediaPlayer_EncounteredError(object? sender, EventArgs e) =>
        Publish(Snapshot with
        {
            State = PlaybackState.Failed,
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
            ErrorMessage = "LibVLC encountered a playback error.",
        });

    private void MediaPlayer_Muted(object? sender, EventArgs e) =>
        Publish(Snapshot with
        {
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
        });

    private void MediaPlayer_Unmuted(object? sender, EventArgs e) =>
        Publish(Snapshot with
        {
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
        });

    private void MediaPlayer_VolumeChanged(
        object? sender,
        MediaPlayerVolumeChangedEventArgs e) =>
        Publish(Snapshot with
        {
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
        });

    private void ApplyRequestedAudioState()
    {
        if (_mediaPlayer.Volume != _requestedVolume)
        {
            _mediaPlayer.Volume = _requestedVolume;
        }

        if (_mediaPlayer.Mute != _requestedMuted)
        {
            _mediaPlayer.Mute = _requestedMuted;
        }
    }

    private void Publish(PlaybackSnapshot snapshot)
    {
        EventHandler<PlaybackSnapshotChangedEventArgs>? handler;
        lock (_sync)
        {
            _snapshot = snapshot;
            handler = SnapshotChanged;
        }

        handler?.Invoke(
            this,
            new PlaybackSnapshotChangedEventArgs(snapshot));
    }

    private static void ApplyPlaybackOptions(
        Media media,
        IReadOnlyDictionary<string, string> directives)
    {
        foreach (var pair in directives)
        {
            const string prefix = "extvlcopt:";
            if (!pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var optionName = pair.Key[prefix.Length..].Trim();
            if (optionName.Length == 0)
            {
                continue;
            }

            media.AddOption($":{optionName}={pair.Value}");
        }
    }
}
