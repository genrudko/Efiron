using System.Diagnostics;
using System.Runtime.InteropServices;
using Efiron.Application.Playback;
using Efiron.Domain.Playback;

namespace Efiron.Playback;

public sealed class MpvPlaybackSession : IPlaybackSession
{
    private readonly object _sync = new();
    private readonly nint _context;
    private readonly Thread _eventThread;
    private readonly Stopwatch _sessionClock = new();
    private readonly MpvPlaybackProfile _profile;

    private PlaybackSnapshot _snapshot = PlaybackSnapshot.Idle;
    private int _requestedVolume = 100;
    private bool _requestedMuted;
    private TimeSpan? _startupLatency;
    private nint _displaySwapChain;
    private bool _disposed;

    public MpvPlaybackSession(MpvPlaybackProfile profile)
    {
        _profile = profile;
        _context = MpvNative.mpv_create();
        if (_context == 0)
        {
            throw new InvalidOperationException("libmpv could not create a client context.");
        }

        try
        {
            ConfigureBeforeInitialize();
            MpvNative.ThrowIfError(
                MpvNative.mpv_initialize(_context),
                "initialize");
            ApplyAudioState();
        }
        catch
        {
            MpvNative.mpv_terminate_destroy(_context);
            throw;
        }

        _eventThread = new Thread(EventLoop)
        {
            IsBackground = true,
            Name = "Efiron libmpv event loop",
        };
        _eventThread.Start();
    }

    public event EventHandler<PlaybackSnapshotChangedEventArgs>? SnapshotChanged;

    public event EventHandler? DisplaySwapChainChanged;

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

    public nint DisplaySwapChain => Volatile.Read(ref _displaySwapChain);

    public TimeSpan? SessionDuration =>
        _sessionClock.IsRunning || _sessionClock.Elapsed > TimeSpan.Zero
            ? _sessionClock.Elapsed
            : null;

    public TimeSpan? StartupLatency => _startupLatency;

    public ValueTask PlayAsync(
        PlaybackRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        _startupLatency = null;
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
            MpvNative.Command(
                _context,
                "loadfile",
                request.Source.AbsoluteUri,
                "replace");
            ApplyAudioState();
            return ValueTask.CompletedTask;
        }
        catch (Exception exception)
        {
            _sessionClock.Stop();
            Publish(Snapshot with
            {
                State = PlaybackState.Failed,
                Volume = _requestedVolume,
                IsMuted = _requestedMuted,
                ErrorMessage = exception.Message,
            });
            throw;
        }
    }

    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        MpvNative.SetProperty(_context, "pause", "yes");
        Publish(Snapshot with
        {
            State = PlaybackState.Paused,
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
            ErrorMessage = null,
        });
    }

    public void Resume()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        MpvNative.SetProperty(_context, "pause", "no");
        Publish(Snapshot with
        {
            State = PlaybackState.Playing,
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
            ErrorMessage = null,
        });
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        MpvNative.Command(_context, "stop");
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
        MpvNative.SetProperty(_context, "mute", isMuted ? "yes" : "no");
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
        MpvNative.SetProperty(
            _context,
            "volume",
            volume.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Publish(Snapshot with
        {
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
        });
    }

    public void SetCompositionSize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        MpvNative.SetProperty(
            _context,
            "d3d11-composition-size",
            $"{width}x{height}");
        RefreshDisplaySwapChain();
    }

    public MpvDiagnosticSnapshot CaptureDiagnosticSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var droppedByVo = MpvNative.GetInt64(_context, "vo-drop-frame-count");
        var droppedByDecoder = MpvNative.GetInt64(
            _context,
            "decoder-frame-drop-count");
        long? dropped = droppedByVo is null && droppedByDecoder is null
            ? null
            : Math.Max(0, droppedByVo ?? 0) + Math.Max(0, droppedByDecoder ?? 0);
        var avSyncSeconds = MpvNative.GetDouble(_context, "avsync");

        return new MpvDiagnosticSnapshot(
            DateTimeOffset.UtcNow,
            MpvNative.GetString(_context, "mpv-version"),
            MpvNative.GetString(_context, "file-format"),
            MpvNative.GetString(_context, "video-codec"),
            MpvNative.GetString(_context, "audio-codec-name"),
            ToNullableInt(MpvNative.GetInt64(_context, "width")),
            ToNullableInt(MpvNative.GetInt64(_context, "height")),
            PositiveOrNull(MpvNative.GetDouble(_context, "container-fps")),
            PositiveOrNull(MpvNative.GetDouble(_context, "estimated-vf-fps")),
            dropped,
            PositiveOrNull(MpvNative.GetDouble(_context, "demuxer-cache-duration")),
            PercentOrNull(MpvNative.GetDouble(_context, "cache-buffering-state")),
            MpvNative.GetString(_context, "hwdec-current"),
            MpvNative.GetString(_context, "vo"),
            avSyncSeconds is null || double.IsNaN(avSyncSeconds.Value)
                ? null
                : TimeSpan.FromSeconds(avSyncSeconds.Value),
            _startupLatency,
            SessionDuration,
            SecondsOrNull(MpvNative.GetDouble(_context, "time-pos")),
            Snapshot);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            MpvNative.mpv_wakeup(_context);
            if (_eventThread.IsAlive &&
                !_eventThread.Join(TimeSpan.FromSeconds(3)))
            {
                _eventThread.Join(TimeSpan.FromSeconds(1));
            }
        }
        finally
        {
            _sessionClock.Stop();
            Interlocked.Exchange(ref _displaySwapChain, 0);
            MpvNative.mpv_terminate_destroy(_context);
        }

        Publish(Snapshot with
        {
            State = PlaybackState.Disposed,
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
            ErrorMessage = null,
        });
    }

    private void ConfigureBeforeInitialize()
    {
        MpvNative.SetOption(_context, "terminal", "no");
        MpvNative.SetOption(_context, "input-default-bindings", "no");
        MpvNative.SetOption(_context, "input-vo-keyboard", "no");
        MpvNative.SetOption(_context, "osc", "no");
        MpvNative.SetOption(_context, "osd-level", "0");
        MpvNative.SetOption(_context, "idle", "yes");
        MpvNative.SetOption(_context, "force-window", "no");
        MpvNative.SetOption(_context, "vo", "gpu");
        MpvNative.SetOption(_context, "gpu-api", "d3d11");
        MpvNative.SetOption(_context, "gpu-context", "d3d11");
        MpvNative.SetOption(_context, "d3d11-output-mode", "composition");
        MpvNative.SetOption(_context, "d3d11-composition-size", "16x16");
        MpvNative.SetOption(_context, "d3d11-output-format", "bgra8");
        MpvNative.SetOption(_context, "d3d11-output-csp", "srgb");
        MpvNative.SetOption(_context, "d3d11-flip", "no");
        MpvNative.SetOption(_context, "d3d11-sync-interval", "1");
        MpvNative.SetOption(_context, "swapchain-depth", "2");
        MpvNative.SetOption(_context, "cache", "yes");
        MpvNative.SetOption(_context, "cache-secs", "8");
        MpvNative.SetOption(_context, "demuxer-max-bytes", "33554432");
        MpvNative.SetOption(_context, "demuxer-max-back-bytes", "8388608");
        MpvNative.SetOption(_context, "keep-open", "no");

        if (_profile == MpvPlaybackProfile.SmoothMotion)
        {
            MpvNative.SetOption(_context, "hwdec", "d3d11va");
            MpvNative.SetOption(_context, "video-sync", "display-resample");
            MpvNative.SetOption(_context, "interpolation", "yes");
            MpvNative.SetOption(_context, "tscale", "oversample");
        }
        else
        {
            MpvNative.SetOption(_context, "hwdec", "auto-safe");
            MpvNative.SetOption(_context, "video-sync", "audio");
            MpvNative.SetOption(_context, "interpolation", "no");
        }
    }

    private void ApplyAudioState()
    {
        MpvNative.SetProperty(
            _context,
            "volume",
            _requestedVolume.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        MpvNative.SetProperty(
            _context,
            "mute",
            _requestedMuted ? "yes" : "no");
    }

    private void EventLoop()
    {
        while (!_disposed)
        {
            try
            {
                var eventPointer = MpvNative.mpv_wait_event(_context, 0.25);
                if (eventPointer == 0)
                {
                    continue;
                }

                var nativeEvent = Marshal.PtrToStructure<MpvNative.Event>(eventPointer);
                if (nativeEvent.EventId == MpvNative.EventId.None)
                {
                    continue;
                }

                HandleEvent(nativeEvent);
            }
            catch (Exception exception) when (!_disposed)
            {
                _sessionClock.Stop();
                Publish(Snapshot with
                {
                    State = PlaybackState.Failed,
                    Volume = _requestedVolume,
                    IsMuted = _requestedMuted,
                    ErrorMessage = $"libmpv event loop failed: {exception.Message}",
                });
                return;
            }
        }
    }

    private void HandleEvent(MpvNative.Event nativeEvent)
    {
        switch (nativeEvent.EventId)
        {
            case MpvNative.EventId.StartFile:
                Publish(Snapshot with
                {
                    State = PlaybackState.Opening,
                    Volume = _requestedVolume,
                    IsMuted = _requestedMuted,
                    ErrorMessage = null,
                });
                break;
            case MpvNative.EventId.VideoReconfig:
                RefreshDisplaySwapChain();
                break;
            case MpvNative.EventId.PlaybackRestart:
                _startupLatency ??= _sessionClock.Elapsed;
                RefreshDisplaySwapChain();
                Publish(Snapshot with
                {
                    State = PlaybackState.Playing,
                    Volume = _requestedVolume,
                    IsMuted = _requestedMuted,
                    ErrorMessage = null,
                });
                break;
            case MpvNative.EventId.EndFile:
                HandleEndFile(nativeEvent.Data);
                break;
            case MpvNative.EventId.Shutdown:
                _sessionClock.Stop();
                break;
        }
    }

    private void HandleEndFile(nint data)
    {
        _sessionClock.Stop();
        var end = data == 0
            ? default
            : Marshal.PtrToStructure<MpvNative.EventEndFile>(data);
        var failed = data != 0 && end.Error < 0;
        Publish(Snapshot with
        {
            State = failed ? PlaybackState.Failed : PlaybackState.Ended,
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
            ErrorMessage = failed
                ? $"libmpv could not play the stream: {MpvNative.DescribeError(end.Error)} ({end.Error})."
                : null,
        });
    }

    private void RefreshDisplaySwapChain()
    {
        var raw = MpvNative.GetInt64(_context, "display-swapchain");
        var next = raw is > 0 ? (nint)raw.Value : 0;
        var previous = Interlocked.Exchange(ref _displaySwapChain, next);
        if (previous != next)
        {
            DisplaySwapChainChanged?.Invoke(this, EventArgs.Empty);
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

        handler?.Invoke(this, new PlaybackSnapshotChangedEventArgs(snapshot));
    }

    private static int? ToNullableInt(long? value) =>
        value is >= 0 and <= int.MaxValue ? (int)value.Value : null;

    private static double? PositiveOrNull(double? value) =>
        value is > 0 && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value)
            ? value
            : null;

    private static double? PercentOrNull(double? value) =>
        value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value)
            ? null
            : Math.Clamp(value.Value, 0, 100);

    private static TimeSpan? SecondsOrNull(double? value) =>
        value is >= 0 && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value)
            ? TimeSpan.FromSeconds(value.Value)
            : null;
}

public sealed record MpvDiagnosticSnapshot(
    DateTimeOffset SampledAtUtc,
    string? Version,
    string? Container,
    string? VideoCodec,
    string? AudioCodec,
    int? VideoWidth,
    int? VideoHeight,
    double? DeclaredFramesPerSecond,
    double? RenderedFramesPerSecond,
    long? DroppedFrames,
    double? BufferDurationSeconds,
    double? BufferedPercentage,
    string? HardwareDecoder,
    string? VideoRenderer,
    TimeSpan? AudioVideoDrift,
    TimeSpan? StartupLatency,
    TimeSpan? SessionDuration,
    TimeSpan? MediaPosition,
    PlaybackSnapshot Snapshot);