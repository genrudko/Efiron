using Efiron.Application.Playback;
using Efiron.Domain.Playback;

namespace Efiron.Playback;

internal sealed class RestartingMpvProcessPlaybackSession : IPlaybackSession
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly nint _hostWindowHandle;
    private readonly MpvPlaybackProfile _profile;

    private MpvProcessPlaybackSession? _inner;
    private PlaybackSnapshot _snapshot = PlaybackSnapshot.Idle;
    private int _requestedVolume = 100;
    private bool _requestedMuted;
    private bool _disposed;

    public RestartingMpvProcessPlaybackSession(
        nint hostWindowHandle,
        MpvPlaybackProfile profile)
    {
        _hostWindowHandle = hostWindowHandle;
        _profile = profile;
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

    public MpvPlaybackProfile Profile => _profile;

    public int? HostProcessId
    {
        get
        {
            lock (_sync)
            {
                return _inner?.HostProcessId;
            }
        }
    }

    public async ValueTask PlayAsync(
        PlaybackRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

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

        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var previous = DetachInner();
            DisposeInner(previous);

            var next = new MpvProcessPlaybackSession(
                _hostWindowHandle,
                _profile);
            next.SetVolume(_requestedVolume);
            next.SetMuted(_requestedMuted);
            next.SnapshotChanged += Inner_SnapshotChanged;
            lock (_sync)
            {
                _inner = next;
            }

            try
            {
                await next.PlayAsync(request, cancellationToken);
            }
            catch
            {
                next.SnapshotChanged -= Inner_SnapshotChanged;
                lock (_sync)
                {
                    if (ReferenceEquals(_inner, next))
                    {
                        _inner = null;
                    }
                }
                DisposeInner(next);
                throw;
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GetInnerOrThrow().Pause();
    }

    public void Resume()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GetInnerOrThrow().Resume();
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        MpvProcessPlaybackSession? inner;
        lock (_sync)
        {
            inner = _inner;
        }

        inner?.Stop();
        if (inner is null)
        {
            Publish(Snapshot with
            {
                State = PlaybackState.Stopped,
                Volume = _requestedVolume,
                IsMuted = _requestedMuted,
                ErrorMessage = null,
            });
        }
    }

    public void SetMuted(bool isMuted)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _requestedMuted = isMuted;
        MpvProcessPlaybackSession? inner;
        lock (_sync)
        {
            inner = _inner;
        }

        if (inner is not null)
        {
            inner.SetMuted(isMuted);
        }
        else
        {
            Publish(Snapshot with
            {
                Volume = _requestedVolume,
                IsMuted = _requestedMuted,
            });
        }
    }

    public void SetVolume(int volume)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfLessThan(volume, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(volume, 100);

        _requestedVolume = volume;
        MpvProcessPlaybackSession? inner;
        lock (_sync)
        {
            inner = _inner;
        }

        if (inner is not null)
        {
            inner.SetVolume(volume);
        }
        else
        {
            Publish(Snapshot with
            {
                Volume = _requestedVolume,
                IsMuted = _requestedMuted,
            });
        }
    }

    public MpvProcessDiagnosticSnapshot CaptureDiagnosticSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_sync)
        {
            return _inner?.CaptureDiagnosticSnapshot() ??
                new MpvProcessDiagnosticSnapshot(
                    DateTimeOffset.UtcNow,
                    Version: null,
                    Container: null,
                    VideoCodec: null,
                    AudioCodec: null,
                    VideoWidth: null,
                    VideoHeight: null,
                    DeclaredFramesPerSecond: null,
                    RenderedFramesPerSecond: null,
                    DroppedFrames: null,
                    BufferDurationSeconds: null,
                    BufferedPercentage: null,
                    HardwareDecoder: null,
                    VideoRenderer: null,
                    AudioVideoDrift: null,
                    StartupLatency: null,
                    SessionDuration: null,
                    MediaPosition: null,
                    Snapshot,
                    DisplayFramesPerSecond: null,
                    EstimatedDisplayFramesPerSecond: null,
                    VideoSpeedCorrection: null,
                    AudioSpeedCorrection: null,
                    VSyncRatio: null,
                    MistimedFrames: null,
                    DelayedFrames: null,
                    PixelFormat: null,
                    InterpolationActive: null,
                    VideoSync: null,
                    HostProcessId: null,
                    HostWorkingSetBytes: null,
                    HostPrivateMemoryBytes: null,
                    HostHandleCount: null);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifecycle.Wait();
        try
        {
            DisposeInner(DetachInner());
        }
        finally
        {
            _lifecycle.Release();
            _lifecycle.Dispose();
        }

        Publish(Snapshot with
        {
            State = PlaybackState.Disposed,
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
            ErrorMessage = null,
        });
    }

    private MpvProcessPlaybackSession GetInnerOrThrow()
    {
        lock (_sync)
        {
            return _inner ?? throw new InvalidOperationException(
                "mpv host has no active playback process.");
        }
    }

    private MpvProcessPlaybackSession? DetachInner()
    {
        lock (_sync)
        {
            var inner = _inner;
            _inner = null;
            if (inner is not null)
            {
                inner.SnapshotChanged -= Inner_SnapshotChanged;
            }
            return inner;
        }
    }

    private static void DisposeInner(MpvProcessPlaybackSession? inner)
    {
        if (inner is null)
        {
            return;
        }

        try
        {
            inner.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void Inner_SnapshotChanged(
        object? sender,
        PlaybackSnapshotChangedEventArgs eventArgs)
    {
        lock (_sync)
        {
            if (!ReferenceEquals(sender, _inner))
            {
                return;
            }
        }

        Publish(eventArgs.Snapshot);
    }

    private void Publish(PlaybackSnapshot snapshot)
    {
        EventHandler<PlaybackSnapshotChangedEventArgs>? handler;
        lock (_sync)
        {
            _snapshot = snapshot;
            handler = SnapshotChanged;
        }

        handler?.Invoke(this, new PlaybackSnapshotChangedEventArgs(snapshot));
    }
}
