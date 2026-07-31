using System.Diagnostics;
using Efiron.Application.Playback;
using Efiron.Domain.Playback;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Efiron.Playback;

public sealed class WindowsMediaPlaybackSession : IPlaybackSession
{
    private readonly object _sync = new();
    private readonly MediaPlayer _mediaPlayer;
    private readonly Stopwatch _sessionClock = new();

    private MediaSource? _currentSource;
    private PlaybackSnapshot _snapshot = PlaybackSnapshot.Idle;
    private int _requestedVolume = 100;
    private bool _requestedMuted;
    private TimeSpan? _startupLatency;
    private long _rebufferCount;
    private long _bufferUnderruns;
    private bool _hasPlayed;
    private bool _disposed;

    public WindowsMediaPlaybackSession()
    {
        _mediaPlayer = new MediaPlayer
        {
            AutoPlay = false,
            IsVideoFrameServerEnabled = false,
        };
        _mediaPlayer.Volume = 1;
        _mediaPlayer.IsMuted = false;
        AttachEvents();
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

    public TimeSpan? SessionDuration =>
        _sessionClock.IsRunning || _sessionClock.Elapsed > TimeSpan.Zero
            ? _sessionClock.Elapsed
            : null;

    public TimeSpan? MediaPosition =>
        _currentSource is null
            ? null
            : _mediaPlayer.PlaybackSession.Position;

    public TimeSpan? StartupLatency => _startupLatency;

    public double? BufferedPercentage =>
        _currentSource is null
            ? null
            : Math.Clamp(_mediaPlayer.PlaybackSession.BufferingProgress * 100, 0, 100);

    public long RebufferCount => Interlocked.Read(ref _rebufferCount);

    public long BufferUnderruns => Interlocked.Read(ref _bufferUnderruns);

    public int? NaturalVideoWidth
    {
        get
        {
            if (_currentSource is null)
            {
                return null;
            }

            var width = _mediaPlayer.PlaybackSession.NaturalVideoWidth;
            return width == 0 ? null : checked((int)width);
        }
    }

    public int? NaturalVideoHeight
    {
        get
        {
            if (_currentSource is null)
            {
                return null;
            }

            var height = _mediaPlayer.PlaybackSession.NaturalVideoHeight;
            return height == 0 ? null : checked((int)height);
        }
    }

    public ValueTask PlayAsync(
        PlaybackRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var source = MediaSource.CreateFromUri(request.Source);
        var previousSource = Interlocked.Exchange(ref _currentSource, source);
        _startupLatency = null;
        _hasPlayed = false;
        Interlocked.Exchange(ref _rebufferCount, 0);
        Interlocked.Exchange(ref _bufferUnderruns, 0);
        _sessionClock.Restart();

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
            _mediaPlayer.Source = source;
            ApplyRequestedAudioState();
            _mediaPlayer.Play();
            previousSource?.Dispose();
            return ValueTask.CompletedTask;
        }
        catch (Exception exception)
        {
            _sessionClock.Stop();
            _mediaPlayer.Source = null;
            Interlocked.CompareExchange(ref _currentSource, previousSource, source);
            source.Dispose();
            Publish(Snapshot with
            {
                State = PlaybackState.Failed,
                Volume = _requestedVolume,
                IsMuted = _requestedMuted,
                ErrorMessage = DescribeException(
                    "Windows Media rejected the playback request",
                    exception),
            });
            throw;
        }
    }

    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_mediaPlayer.PlaybackSession.CanPause)
        {
            _mediaPlayer.Pause();
        }
    }

    public void Resume()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_currentSource is not null &&
            Snapshot.State is PlaybackState.Paused or PlaybackState.Stopped)
        {
            ApplyRequestedAudioState();
            _mediaPlayer.Play();
        }
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_mediaPlayer.PlaybackSession.CanPause)
        {
            _mediaPlayer.Pause();
        }

        _mediaPlayer.Source = null;
        Interlocked.Exchange(ref _currentSource, null)?.Dispose();
        _sessionClock.Stop();
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
        _mediaPlayer.IsMuted = isMuted;
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
        _mediaPlayer.Volume = volume / 100d;
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
            if (_mediaPlayer.PlaybackSession.CanPause)
            {
                _mediaPlayer.Pause();
            }
        }
        catch (ObjectDisposedException)
        {
        }

        _mediaPlayer.Source = null;
        _sessionClock.Stop();
        Interlocked.Exchange(ref _currentSource, null)?.Dispose();
        _mediaPlayer.Dispose();

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
        _mediaPlayer.MediaOpened += MediaPlayer_MediaOpened;
        _mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
        _mediaPlayer.MediaFailed += MediaPlayer_MediaFailed;
        _mediaPlayer.PlaybackSession.PlaybackStateChanged +=
            PlaybackSession_PlaybackStateChanged;
        _mediaPlayer.PlaybackSession.BufferingStarted +=
            PlaybackSession_BufferingStarted;
    }

    private void DetachEvents()
    {
        _mediaPlayer.MediaOpened -= MediaPlayer_MediaOpened;
        _mediaPlayer.MediaEnded -= MediaPlayer_MediaEnded;
        _mediaPlayer.MediaFailed -= MediaPlayer_MediaFailed;
        _mediaPlayer.PlaybackSession.PlaybackStateChanged -=
            PlaybackSession_PlaybackStateChanged;
        _mediaPlayer.PlaybackSession.BufferingStarted -=
            PlaybackSession_BufferingStarted;
    }

    private void MediaPlayer_MediaOpened(MediaPlayer sender, object args)
    {
        ApplyRequestedAudioState();
    }

    private void MediaPlayer_MediaEnded(MediaPlayer sender, object args)
    {
        _sessionClock.Stop();
        Publish(Snapshot with
        {
            State = PlaybackState.Ended,
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
            ErrorMessage = null,
        });
    }

    private void MediaPlayer_MediaFailed(
        MediaPlayer sender,
        MediaPlayerFailedEventArgs args)
    {
        _sessionClock.Stop();
        Publish(Snapshot with
        {
            State = PlaybackState.Failed,
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
            ErrorMessage = DescribeMediaFailure(args),
        });
    }

    private void PlaybackSession_PlaybackStateChanged(
        MediaPlaybackSession sender,
        object args)
    {
        var current = Snapshot;
        var state = sender.PlaybackState switch
        {
            MediaPlaybackState.Opening or MediaPlaybackState.Buffering =>
                PlaybackState.Opening,
            MediaPlaybackState.Playing => PlaybackState.Playing,
            MediaPlaybackState.Paused => PlaybackState.Paused,
            _ => current.State,
        };

        if (state == PlaybackState.Playing)
        {
            _hasPlayed = true;
            _startupLatency ??= _sessionClock.Elapsed;
            ApplyRequestedAudioState();
        }

        Publish(current with
        {
            State = state,
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
            ErrorMessage = state == PlaybackState.Failed
                ? current.ErrorMessage
                : null,
        });
    }

    private void PlaybackSession_BufferingStarted(
        MediaPlaybackSession sender,
        object args)
    {
        if (_hasPlayed)
        {
            Interlocked.Increment(ref _rebufferCount);
            Interlocked.Increment(ref _bufferUnderruns);
        }
    }

    private void ApplyRequestedAudioState()
    {
        var requestedVolume = _requestedVolume / 100d;
        if (Math.Abs(_mediaPlayer.Volume - requestedVolume) > 0.001)
        {
            _mediaPlayer.Volume = requestedVolume;
        }

        if (_mediaPlayer.IsMuted != _requestedMuted)
        {
            _mediaPlayer.IsMuted = _requestedMuted;
        }
    }

    private static string DescribeMediaFailure(MediaPlayerFailedEventArgs args)
    {
        var error = args.ExtendedErrorCode;
        var detail = !string.IsNullOrWhiteSpace(args.ErrorMessage)
            ? args.ErrorMessage.Trim()
            : !string.IsNullOrWhiteSpace(error?.Message)
                ? error.Message.Trim()
                : "Windows Media could not open the stream";
        var hresult = error?.HResult ?? 0;
        return $"{detail} (HRESULT 0x{unchecked((uint)hresult):X8}).";
    }

    private static string DescribeException(string context, Exception exception)
    {
        var detail = string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message.Trim();
        return $"{context}: {detail} " +
            $"(HRESULT 0x{unchecked((uint)exception.HResult):X8}).";
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
}
