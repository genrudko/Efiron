using System.ComponentModel;
using System.Diagnostics;
using Efiron.Application.Playback;
using Efiron.Domain.Playback;
using FlyleafLib;
using FlyleafLib.MediaPlayer;

namespace Efiron.Playback;

public sealed class FlyleafPlaybackBackend : IPlaybackBackend
{
    private static readonly PlaybackBackendCapabilities BackendCapabilities = new(
        ContainerMetadata: false,
        CodecMetadata: true,
        FrameStatistics: true,
        InputBitrate: true,
        Buffering: true,
        HardwareDecodingStatus: true,
        RendererMetadata: true,
        AudioTracks: true,
        SubtitleTracks: true,
        MediaPosition: true);

    private readonly FlyleafPlaybackSession _session;

    public FlyleafPlaybackBackend()
    {
        FlyleafEngineRuntime.EnsureStarted();
        _session = new FlyleafPlaybackSession();
    }

    public PlaybackBackendId Id => PlaybackBackendId.Flyleaf;

    public string? Version => typeof(Player).Assembly.GetName().Version?.ToString();

    public string SelectedProfile => "D3D11 VSync / FFmpeg";

    public PlaybackBackendCapabilities Capabilities => BackendCapabilities;

    public IPlaybackSession Session => _session;

    public Player Player => _session.Player;

    public PlaybackBackendDiagnostics CaptureDiagnostics()
    {
        var player = _session.Player;
        var snapshot = _session.Snapshot;
        return new PlaybackBackendDiagnostics(
            DateTimeOffset.UtcNow,
            Id,
            Version,
            SelectedProfile,
            BackendCapabilities,
            snapshot.Source?.Scheme,
            Container: null,
            VideoCodec: NullIfWhiteSpace(player.Video.Codec),
            AudioCodec: NullIfWhiteSpace(player.Audio.Codec),
            VideoWidth: player.Video.Width > 0 ? player.Video.Width : null,
            VideoHeight: player.Video.Height > 0 ? player.Video.Height : null,
            DeclaredFramesPerSecond: PositiveOrNull(player.Video.FPS),
            RenderedFramesPerSecond: PositiveOrNull(player.Video.FPSCurrent),
            DisplayedFrames: player.Video.FramesDisplayed,
            DroppedFrames: player.Video.FramesDropped,
            InputBitrateBitsPerSecond: PositiveOrNull(player.BitRate * 1000d),
            BufferDuration: player.BufferedDuration > 0
                ? TimeSpan.FromTicks(player.BufferedDuration)
                : null,
            BufferedPercentage: null,
            BufferUnderruns: null,
            RebufferCount: null,
            Discontinuities: null,
            HardwareDecodingRequested: true,
            HardwareDecodingActive: player.Video.IsOpened
                ? player.Video.VideoAcceleration
                : null,
            Decoder: NullIfWhiteSpace(player.Video.Codec),
            GraphicsDevice: null,
            VideoRenderer: "Flyleaf D3D11 / DirectComposition",
            AudioVideoDrift: null,
            StartupLatency: _session.StartupLatency,
            TimeToFirstFrame: _session.StartupLatency,
            PlaybackState: snapshot.State,
            PlaybackError: snapshot.ErrorMessage,
            SessionDuration: _session.SessionDuration,
            MediaPosition: player.CurTime > 0
                ? TimeSpan.FromTicks(player.CurTime)
                : null,
            AudioTrack: player.Audio.IsOpened ? player.Audio.Codec : null,
            SubtitleTrack: player.Subtitles.IsOpened ? "enabled" : null,
            DisplayFramesPerSecond: PositiveOrNull(player.Video.FPSCurrent),
            EstimatedDisplayFramesPerSecond: null,
            VideoSpeedCorrection: player.Speed,
            AudioSpeedCorrection: player.Speed,
            VSyncRatio: null,
            MistimedFrames: null,
            DelayedFrames: null,
            PixelFormat: NullIfWhiteSpace(player.Video.PixelFormat),
            PresentationMode: "Flyleaf D3D11 swap chain with VSync",
            InterpolationActive: false);
    }

    public void Dispose() => _session.Dispose();

    private static double? PositiveOrNull(double value) => value > 0 ? value : null;

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

internal static class FlyleafEngineRuntime
{
    private static readonly object Sync = new();

    public static void EnsureStarted()
    {
        if (Engine.IsLoaded)
        {
            return;
        }

        lock (Sync)
        {
            if (Engine.IsLoaded)
            {
                return;
            }

            var ffmpegDirectory = Path.Combine(AppContext.BaseDirectory, "FFmpeg");
            if (!Directory.Exists(ffmpegDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"Flyleaf FFmpeg runtime was not found: {ffmpegDirectory}");
            }

            Engine.Start(new EngineConfig
            {
                UIRefresh = true,
                UIRefreshInterval = 250,
                PluginsPath = ":Plugins",
                FFmpegPath = ":FFmpeg",
            });
        }
    }
}

internal sealed class FlyleafPlaybackSession : IPlaybackSession
{
    private readonly object _sync = new();
    private readonly Stopwatch _sessionClock = Stopwatch.StartNew();
    private readonly Player _player;
    private TaskCompletionSource<bool>? _openCompletion;
    private Stopwatch? _startupClock;
    private PlaybackSnapshot _snapshot = PlaybackSnapshot.Idle;
    private bool _requestedMuted;
    private bool _disposed;

    public FlyleafPlaybackSession()
    {
        var config = new Config();
        config.Player.AutoPlay = true;
        config.Player.Stats = true;
        config.Player.MinBufferDuration = TimeSpan.FromMilliseconds(350).Ticks;
        config.Demuxer.BufferDuration = TimeSpan.FromSeconds(20).Ticks;
        config.Demuxer.OpenTimeout = TimeSpan.FromSeconds(20).Ticks;
        config.Demuxer.ReadLiveTimeout = TimeSpan.FromSeconds(12).Ticks;

        _player = new Player(config);
        _player.PropertyChanged += Player_PropertyChanged;
        _player.OpenCompleted += (_, args) => CompleteOpen(args.Success, args.Error);
    }

    public event EventHandler<PlaybackSnapshotChangedEventArgs>? SnapshotChanged;

    public Player Player => _player;

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

    public TimeSpan? StartupLatency { get; private set; }

    public TimeSpan SessionDuration => _sessionClock.Elapsed;

    public async ValueTask PlayAsync(
        PlaybackRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();

        // Flyleaf has a native loop facility. Map the existing M3U repeat
        // directive to it rather than relying on a VLC-only option parser.
        _player.LoopPlayback = ShouldLoop(request.Directives);

        TaskCompletionSource<bool> completion;
        lock (_sync)
        {
            _startupClock = Stopwatch.StartNew();
            StartupLatency = null;
            completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _openCompletion?.TrySetCanceled();
            _openCompletion = completion;
            SetSnapshotUnsafe(new PlaybackSnapshot(
                PlaybackState.Opening,
                request.Source,
                request.ChannelStableId,
                request.DisplayName,
                Math.Clamp(_player.Audio.Volume, 0, 100),
                _requestedMuted,
                ErrorMessage: null));
        }

        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetCanceled(),
            completion);

        _player.OpenAsync(request.Source.AbsoluteUri);
        await completion.Task.ConfigureAwait(false);
    }

    public void Pause()
    {
        ThrowIfDisposed();
        _player.Pause();
        RefreshStateFromPlayer();
    }

    public void Resume()
    {
        ThrowIfDisposed();
        _player.Play();
        RefreshStateFromPlayer();
    }

    public void Stop()
    {
        if (_disposed)
        {
            return;
        }

        _player.LoopPlayback = false;
        _player.Stop();
        lock (_sync)
        {
            _openCompletion?.TrySetCanceled();
            _openCompletion = null;
            SetSnapshotUnsafe(_snapshot with
            {
                State = PlaybackState.Stopped,
                ErrorMessage = null,
            });
        }
    }

    public void SetMuted(bool isMuted)
    {
        ThrowIfDisposed();
        lock (_sync)
        {
            _requestedMuted = isMuted;
        }

        ApplyRequestedMuteToPlayer();
        UpdateAudioSnapshot();
    }

    public void SetVolume(int volume)
    {
        ThrowIfDisposed();
        _player.Audio.Volume = Math.Clamp(volume, 0, 100);
        ApplyRequestedMuteToPlayer();
        UpdateAudioSnapshot();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _player.PropertyChanged -= Player_PropertyChanged;
        lock (_sync)
        {
            _openCompletion?.TrySetCanceled();
            _openCompletion = null;
        }

        _player.Dispose();
        lock (_sync)
        {
            SetSnapshotUnsafe(_snapshot with { State = PlaybackState.Disposed });
        }
    }

    private void CompleteOpen(bool success, string? error)
    {
        if (success)
        {
            ApplyRequestedMuteToPlayer();
        }

        TaskCompletionSource<bool>? completion;
        lock (_sync)
        {
            completion = _openCompletion;
            _openCompletion = null;
            if (success)
            {
                StartupLatency = _startupClock?.Elapsed;
                SetSnapshotUnsafe(_snapshot with
                {
                    State = MapStatus(_player.Status.ToString()),
                    Volume = Math.Clamp(_player.Audio.Volume, 0, 100),
                    IsMuted = _requestedMuted,
                    ErrorMessage = null,
                });
            }
            else
            {
                SetSnapshotUnsafe(_snapshot with
                {
                    State = PlaybackState.Failed,
                    ErrorMessage = string.IsNullOrWhiteSpace(error)
                        ? _player.LastError
                        : error,
                });
            }
        }

        if (success)
        {
            completion?.TrySetResult(true);
        }
        else
        {
            completion?.TrySetException(new InvalidOperationException(
                string.IsNullOrWhiteSpace(error) ? "Flyleaf failed to open media." : error));
        }
    }

    private void Player_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed ||
            (e.PropertyName is not nameof(Player.Status) and
             not nameof(Player.LastError)))
        {
            return;
        }

        ApplyRequestedMuteToPlayer();
        RefreshStateFromPlayer();
    }

    private void ApplyRequestedMuteToPlayer()
    {
        bool requestedMuted;
        lock (_sync)
        {
            requestedMuted = _requestedMuted;
        }

        // Flyleaf ignores Audio.Mute assignments until its XAudio2 source
        // voice exists. Preserve the application-level intent and apply it as
        // soon as an audio stream/output becomes available. Video-only media
        // still exposes the requested state consistently to the controls.
        if (_player.Audio.IsOpened && _player.Audio.Mute != requestedMuted)
        {
            _player.Audio.Mute = requestedMuted;
        }
    }

    private void RefreshStateFromPlayer()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            SetSnapshotUnsafe(_snapshot with
            {
                State = MapStatus(_player.Status.ToString()),
                Volume = Math.Clamp(_player.Audio.Volume, 0, 100),
                IsMuted = _requestedMuted,
                ErrorMessage = string.IsNullOrWhiteSpace(_player.LastError)
                    ? null
                    : _player.LastError,
            });
        }
    }

    private void UpdateAudioSnapshot()
    {
        lock (_sync)
        {
            SetSnapshotUnsafe(_snapshot with
            {
                Volume = Math.Clamp(_player.Audio.Volume, 0, 100),
                IsMuted = _requestedMuted,
            });
        }
    }

    private void SetSnapshotUnsafe(PlaybackSnapshot snapshot)
    {
        _snapshot = snapshot;
        SnapshotChanged?.Invoke(this, new PlaybackSnapshotChangedEventArgs(snapshot));
    }

    private static bool ShouldLoop(IReadOnlyDictionary<string, string> directives)
    {
        if (!directives.TryGetValue("extvlcopt:input-repeat", out var value))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(value) &&
            !string.Equals(value.Trim(), "0", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(value.Trim(), "false", StringComparison.OrdinalIgnoreCase);
    }

    private static PlaybackState MapStatus(string status) => status switch
    {
        "Opening" => PlaybackState.Opening,
        "Playing" => PlaybackState.Playing,
        "Paused" => PlaybackState.Paused,
        "Ended" => PlaybackState.Ended,
        "Failed" => PlaybackState.Failed,
        "Stopped" => PlaybackState.Stopped,
        _ => PlaybackState.Idle,
    };

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
