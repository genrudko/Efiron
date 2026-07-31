using System.Diagnostics;
using Efiron.Application.Playback;
using Efiron.Domain.Playback;
using LibVLCSharp.Platforms.Windows;
using LibVLCSharp.Shared;

namespace Efiron.Playback;

public sealed class LibVlcPlaybackSession : IPlaybackSession
{
    private readonly object _sync = new();
    private readonly object _diagnosticsSync = new();
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private readonly Stopwatch _sessionClock = new();

    private Media? _currentMedia;
    private PlaybackSnapshot _snapshot = PlaybackSnapshot.Idle;
    private int _requestedVolume;
    private bool _requestedMuted;
    private TimeSpan? _startupLatency;
    private bool _hasPlayed;
    private bool _bufferingActive;
    private double? _bufferedPercentage;
    private long _rebufferCount;
    private bool? _hardwareDecodingActive;
    private string? _decoder;
    private string? _graphicsDevice;
    private string? _videoRenderer;
    private bool _disposed;

    public LibVlcPlaybackSession(
        InitializedEventArgs initialization,
        LibVlcPlaybackProfile profile = LibVlcPlaybackProfile.Auto,
        bool enableDebugLogs = false)
    {
        ArgumentNullException.ThrowIfNull(initialization);

        Profile = profile;
        var options = new List<string>();
        options.AddRange(initialization.SwapChainOptions);
        options.AddRange(GetProfileOptions(profile));
        _libVlc = new LibVLC(enableDebugLogs, options.ToArray());
        _libVlc.Log += LibVlc_Log;
        _mediaPlayer = new MediaPlayer(_libVlc);
        _requestedVolume = Math.Clamp(_mediaPlayer.Volume, 0, 100);
        _requestedMuted = _mediaPlayer.Mute;
        AttachEvents();
        ResetDiagnosticState();
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

    public LibVlcPlaybackProfile Profile { get; }

    public MediaPlayer MediaPlayer => _mediaPlayer;

    public TimeSpan? SessionDuration =>
        _sessionClock.IsRunning || _sessionClock.Elapsed > TimeSpan.Zero
            ? _sessionClock.Elapsed
            : null;

    public TimeSpan? MediaPosition =>
        _mediaPlayer.Time >= 0
            ? TimeSpan.FromMilliseconds(_mediaPlayer.Time)
            : null;

    public TimeSpan? StartupLatency => _startupLatency;

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
        _startupLatency = null;
        ResetDiagnosticState();
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
            _mediaPlayer.Play(media);
            ApplyRequestedAudioState();
            previousMedia?.Dispose();
            return ValueTask.CompletedTask;
        }
        catch
        {
            _sessionClock.Stop();
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

    internal LibVlcDiagnosticSnapshot CaptureDiagnosticSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var sampledAtUtc = DateTimeOffset.UtcNow;
        var statistics = default(MediaStats);
        var hasStatistics = false;
        string? videoCodec = null;
        string? audioCodec = null;
        int? videoWidth = null;
        int? videoHeight = null;
        double? declaredFramesPerSecond = null;

        var media = Volatile.Read(ref _currentMedia);
        if (media is not null)
        {
            try
            {
                statistics = media.Statistics;
                hasStatistics =
                    statistics.ReadBytes > 0 ||
                    statistics.DemuxReadBytes > 0 ||
                    statistics.DecodedVideo > 0 ||
                    statistics.DecodedAudio > 0 ||
                    statistics.DisplayedPictures > 0 ||
                    statistics.LostPictures > 0;

                MediaTrack? videoTrack = null;
                MediaTrack? audioTrack = null;
                foreach (var track in media.Tracks)
                {
                    if (track.TrackType == TrackType.Video && videoTrack is null)
                    {
                        videoTrack = track;
                    }
                    else if (track.TrackType == TrackType.Audio && audioTrack is null)
                    {
                        audioTrack = track;
                    }
                }

                if (videoTrack is { } video)
                {
                    videoCodec = SafeCodecDescription(media, video);
                    videoWidth = video.Data.Video.Width > 0
                        ? checked((int)video.Data.Video.Width)
                        : null;
                    videoHeight = video.Data.Video.Height > 0
                        ? checked((int)video.Data.Video.Height)
                        : null;
                    declaredFramesPerSecond = video.Data.Video.FrameRateDen > 0 &&
                        video.Data.Video.FrameRateNum > 0
                            ? (double)video.Data.Video.FrameRateNum /
                              video.Data.Video.FrameRateDen
                            : null;
                }

                if (audioTrack is { } audio)
                {
                    audioCodec = SafeCodecDescription(media, audio);
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (VLCException)
            {
            }
        }

        double? bufferedPercentage;
        long rebufferCount;
        bool? hardwareDecodingActive;
        string? decoder;
        string? graphicsDevice;
        string? videoRenderer;
        lock (_diagnosticsSync)
        {
            bufferedPercentage = _bufferedPercentage;
            rebufferCount = _rebufferCount;
            hardwareDecodingActive = _hardwareDecodingActive;
            decoder = _decoder;
            graphicsDevice = _graphicsDevice;
            videoRenderer = _videoRenderer;
        }

        return new LibVlcDiagnosticSnapshot(
            sampledAtUtc,
            hasStatistics,
            statistics,
            videoCodec,
            audioCodec,
            videoWidth,
            videoHeight,
            declaredFramesPerSecond,
            bufferedPercentage,
            rebufferCount,
            hardwareDecodingActive,
            decoder,
            graphicsDevice,
            videoRenderer,
            SessionDuration,
            MediaPosition,
            StartupLatency);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DetachEvents();
        _libVlc.Log -= LibVlc_Log;

        try
        {
            _mediaPlayer.Stop();
        }
        catch (ObjectDisposedException)
        {
        }

        _sessionClock.Stop();
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
        _mediaPlayer.Buffering += MediaPlayer_Buffering;
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
        _mediaPlayer.Buffering -= MediaPlayer_Buffering;
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

    private void MediaPlayer_Buffering(
        object? sender,
        MediaPlayerBufferingEventArgs e)
    {
        var bufferedPercentage = Math.Clamp((double)e.Cache, 0, 100);
        lock (_diagnosticsSync)
        {
            if (_hasPlayed && bufferedPercentage < 99.9 && !_bufferingActive)
            {
                _rebufferCount++;
            }

            _bufferingActive = bufferedPercentage < 99.9;
            _bufferedPercentage = bufferedPercentage;
        }
    }

    private void MediaPlayer_Playing(object? sender, EventArgs e)
    {
        _startupLatency ??= _sessionClock.Elapsed;
        lock (_diagnosticsSync)
        {
            _hasPlayed = true;
            _bufferingActive = false;
            _bufferedPercentage = 100;
        }

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

        _sessionClock.Stop();
        Publish(Snapshot with
        {
            State = PlaybackState.Stopped,
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
            ErrorMessage = null,
        });
    }

    private void MediaPlayer_EndReached(object? sender, EventArgs e)
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

    private void MediaPlayer_EncounteredError(object? sender, EventArgs e)
    {
        _sessionClock.Stop();
        Publish(Snapshot with
        {
            State = PlaybackState.Failed,
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
            ErrorMessage = "LibVLC encountered a playback error.",
        });
    }

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

    private void LibVlc_Log(object? sender, LogEventArgs e)
    {
        var combined = $"{e.Module} {e.Message}";
        lock (_diagnosticsSync)
        {
            if (ContainsPositiveEvidence(combined, "d3d11va"))
            {
                _hardwareDecodingActive = true;
                _decoder = "D3D11VA (LibVLC log-confirmed)";
            }
            else if (ContainsPositiveEvidence(combined, "dxva2"))
            {
                _hardwareDecodingActive = true;
                _decoder = "DXVA2 (LibVLC log-confirmed)";
            }
            else if (combined.Contains(
                         "hardware acceleration disabled",
                         StringComparison.OrdinalIgnoreCase) ||
                     combined.Contains(
                         "hardware decoding disabled",
                         StringComparison.OrdinalIgnoreCase))
            {
                _hardwareDecodingActive = false;
            }

            if (combined.Contains("direct3d11", StringComparison.OrdinalIgnoreCase))
            {
                _videoRenderer = "Direct3D 11 (LibVLC log evidence)";
            }
            else if (combined.Contains("direct3d9", StringComparison.OrdinalIgnoreCase))
            {
                _videoRenderer = "Direct3D 9 (LibVLC log evidence)";
            }

            if ((combined.Contains("adapter", StringComparison.OrdinalIgnoreCase) ||
                 combined.Contains("device", StringComparison.OrdinalIgnoreCase)) &&
                (combined.Contains("d3d11", StringComparison.OrdinalIgnoreCase) ||
                 combined.Contains("direct3d", StringComparison.OrdinalIgnoreCase) ||
                 combined.Contains("dxva", StringComparison.OrdinalIgnoreCase)))
            {
                _graphicsDevice = TruncateDiagnosticText(e.Message, 300);
            }
        }
    }

    private void ResetDiagnosticState()
    {
        lock (_diagnosticsSync)
        {
            _hasPlayed = false;
            _bufferingActive = false;
            _bufferedPercentage = null;
            _rebufferCount = 0;
            _hardwareDecodingActive = Profile == LibVlcPlaybackProfile.Software
                ? false
                : null;
            _decoder = Profile == LibVlcPlaybackProfile.Software
                ? "Software decoding forced"
                : null;
            _graphicsDevice = null;
            _videoRenderer = Profile == LibVlcPlaybackProfile.D3D11Va
                ? "Direct3D 11 requested"
                : null;
        }
    }

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

    private static string? SafeCodecDescription(Media media, MediaTrack track)
    {
        try
        {
            var description = media.CodecDescription(track.TrackType, track.Codec);
            return string.IsNullOrWhiteSpace(description)
                ? FourCcToString(track.Codec)
                : description;
        }
        catch (VLCException)
        {
            return FourCcToString(track.Codec);
        }
    }

    private static string? FourCcToString(uint codec)
    {
        Span<char> chars = stackalloc char[4];
        chars[0] = (char)(codec & 0xFF);
        chars[1] = (char)((codec >> 8) & 0xFF);
        chars[2] = (char)((codec >> 16) & 0xFF);
        chars[3] = (char)((codec >> 24) & 0xFF);
        var value = new string(chars).Trim('\0', ' ');
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool ContainsPositiveEvidence(string text, string token) =>
        text.Contains(token, StringComparison.OrdinalIgnoreCase) &&
        (text.Contains("using", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("enabled", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("initialized", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("created", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("selected", StringComparison.OrdinalIgnoreCase));

    private static string TruncateDiagnosticText(string text, int maximumLength) =>
        text.Length <= maximumLength
            ? text
            : text[..maximumLength];

    private static IReadOnlyList<string> GetProfileOptions(
        LibVlcPlaybackProfile profile) =>
        profile switch
        {
            LibVlcPlaybackProfile.D3D11Va =>
            [
                "--avcodec-hw=d3d11va",
                "--vout=direct3d11",
            ],
            LibVlcPlaybackProfile.Dxva2 =>
            [
                "--avcodec-hw=dxva2",
            ],
            LibVlcPlaybackProfile.Software =>
            [
                "--avcodec-hw=none",
            ],
            _ => Array.Empty<string>(),
        };

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
