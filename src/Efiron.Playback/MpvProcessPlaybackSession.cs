using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Efiron.Application.Playback;
using Efiron.Domain.Playback;

namespace Efiron.Playback;

public sealed class MpvProcessPlaybackSession : IPlaybackSession
{
    private static readonly string[] ObservedProperties =
    [
        "mpv-version",
        "file-format",
        "video-codec",
        "audio-codec-name",
        "width",
        "height",
        "container-fps",
        "estimated-vf-fps",
        "vo-drop-frame-count",
        "decoder-frame-drop-count",
        "demuxer-cache-duration",
        "cache-buffering-state",
        "hwdec-current",
        "vo",
        "avsync",
        "time-pos",
        "display-fps",
        "estimated-display-fps",
        "video-speed-correction",
        "audio-speed-correction",
        "vsync-ratio",
        "mistimed-frame-count",
        "vo-delayed-frame-count",
        "video-params/pixelformat",
        "interpolation",
        "video-sync",
        "pause",
        "volume",
        "mute",
    ];

    private static readonly TimeSpan GracefulExitDeadline =
        TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan ForcedExitDeadline =
        TimeSpan.FromSeconds(2);

    private readonly object _sync = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly Dictionary<string, JsonElement> _properties =
        new(StringComparer.Ordinal);
    private readonly Stopwatch _sessionClock = new();
    private readonly string _executablePath;
    private readonly nint _hostWindowHandle;
    private readonly MpvPlaybackProfile _profile;

    private PlaybackSnapshot _snapshot = PlaybackSnapshot.Idle;
    private Process? _process;
    private NamedPipeClientStream? _pipe;
    private Channel<string>? _commands;
    private CancellationTokenSource? _processLifetime;
    private Task? _readerTask;
    private Task? _writerTask;
    private int _generation;
    private int _requestedVolume = 100;
    private bool _requestedMuted;
    private TimeSpan? _startupLatency;
    private bool _disposed;

    public MpvProcessPlaybackSession(
        nint hostWindowHandle,
        MpvPlaybackProfile profile)
    {
        if (hostWindowHandle == 0)
        {
            throw new ArgumentException(
                "A native playback host HWND is required.",
                nameof(hostWindowHandle));
        }

        _hostWindowHandle = hostWindowHandle;
        _profile = profile;
        _executablePath = Path.Combine(
            AppContext.BaseDirectory,
            "mpv-host",
            "mpv.exe");
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
                return TryReadProcessMetric(static process => process.Id);
            }
        }
    }

    public TimeSpan? SessionDuration =>
        _sessionClock.IsRunning || _sessionClock.Elapsed > TimeSpan.Zero
            ? _sessionClock.Elapsed
            : null;

    public TimeSpan? StartupLatency => _startupLatency;

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
            await StopHostProcessAsync(requestGracefulExit: true);
            await StartHostProcessAsync(cancellationToken);

            _startupLatency = null;
            _sessionClock.Restart();
            QueueCommand("loadfile", request.Source.AbsoluteUri, "replace");
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
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
        finally
        {
            _lifecycle.Release();
        }
    }

    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        QueueCommandIfRunning("set_property", "pause", true);
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
        QueueCommandIfRunning("set_property", "pause", false);
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
        QueueCommandIfRunning("stop");
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
        QueueCommandIfRunning("set_property", "mute", isMuted);
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
        QueueCommandIfRunning("set_property", "volume", volume);
        Publish(Snapshot with
        {
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
        });
    }

    public MpvProcessDiagnosticSnapshot CaptureDiagnosticSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Process? process;
        Dictionary<string, JsonElement> properties;
        lock (_sync)
        {
            process = _process;
            properties = _properties.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.Clone(),
                StringComparer.Ordinal);
        }

        long? droppedByVo = GetInt64(properties, "vo-drop-frame-count");
        long? droppedByDecoder = GetInt64(
            properties,
            "decoder-frame-drop-count");
        long? dropped = droppedByVo is null && droppedByDecoder is null
            ? null
            : Math.Max(0, droppedByVo ?? 0) +
              Math.Max(0, droppedByDecoder ?? 0);
        var avSyncSeconds = GetDouble(properties, "avsync");

        int? processId = null;
        long? workingSet = null;
        long? privateMemory = null;
        int? handleCount = null;
        if (process is not null)
        {
            try
            {
                process.Refresh();
                if (!process.HasExited)
                {
                    processId = process.Id;
                    workingSet = process.WorkingSet64;
                    privateMemory = process.PrivateMemorySize64;
                    handleCount = process.HandleCount;
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        return new MpvProcessDiagnosticSnapshot(
            DateTimeOffset.UtcNow,
            GetString(properties, "mpv-version"),
            GetString(properties, "file-format"),
            GetString(properties, "video-codec"),
            GetString(properties, "audio-codec-name"),
            ToNullableInt(GetInt64(properties, "width")),
            ToNullableInt(GetInt64(properties, "height")),
            PositiveOrNull(GetDouble(properties, "container-fps")),
            PositiveOrNull(GetDouble(properties, "estimated-vf-fps")),
            dropped,
            PositiveOrNull(GetDouble(properties, "demuxer-cache-duration")),
            PercentOrNull(GetDouble(properties, "cache-buffering-state")),
            GetString(properties, "hwdec-current"),
            GetString(properties, "vo"),
            avSyncSeconds is null || double.IsNaN(avSyncSeconds.Value)
                ? null
                : TimeSpan.FromSeconds(avSyncSeconds.Value),
            _startupLatency,
            SessionDuration,
            SecondsOrNull(GetDouble(properties, "time-pos")),
            Snapshot,
            PositiveOrNull(GetDouble(properties, "display-fps")),
            PositiveOrNull(GetDouble(properties, "estimated-display-fps")),
            FiniteOrNull(GetDouble(properties, "video-speed-correction")),
            FiniteOrNull(GetDouble(properties, "audio-speed-correction")),
            PositiveOrNull(GetDouble(properties, "vsync-ratio")),
            NonNegativeOrNull(GetInt64(properties, "mistimed-frame-count")),
            NonNegativeOrNull(GetInt64(properties, "vo-delayed-frame-count")),
            GetString(properties, "video-params/pixelformat"),
            ParseFlag(properties, "interpolation"),
            GetString(properties, "video-sync"),
            processId,
            workingSet,
            privateMemory,
            handleCount);
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
            StopHostProcessAsync(requestGracefulExit: true)
                .GetAwaiter()
                .GetResult();
        }
        finally
        {
            _sessionClock.Stop();
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

    private async Task StartHostProcessAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_executablePath))
        {
            throw new FileNotFoundException(
                "Pinned mpv host runtime was not packaged.",
                _executablePath);
        }

        var generation = Interlocked.Increment(ref _generation);
        var pipeName = $"efiron-mpv-{Environment.ProcessId}-{generation}-{Guid.NewGuid():N}";
        var processLifetime = new CancellationTokenSource();
        var commands = Channel.CreateUnbounded<string>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });
        var process = new Process
        {
            StartInfo = CreateStartInfo(pipeName),
            EnableRaisingEvents = true,
        };
        process.Exited += (_, _) => HandleProcessExit(process, generation);

        if (!process.Start())
        {
            process.Dispose();
            processLifetime.Dispose();
            throw new InvalidOperationException("mpv host process could not start.");
        }

        var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(10000, cancellationToken);
        }
        catch
        {
            TryKillProcess(process);
            process.Dispose();
            pipe.Dispose();
            processLifetime.Dispose();
            throw;
        }

        lock (_sync)
        {
            _properties.Clear();
            _process = process;
            _pipe = pipe;
            _commands = commands;
            _processLifetime = processLifetime;
        }

        _writerTask = WriteCommandsAsync(
            pipe,
            commands.Reader,
            processLifetime.Token);
        _readerTask = ReadEventsAsync(
            pipe,
            generation,
            processLifetime.Token);

        for (var index = 0; index < ObservedProperties.Length; index++)
        {
            QueueCommand(
                "observe_property",
                1000 + index,
                ObservedProperties[index]);
        }

        QueueCommand("set_property", "volume", _requestedVolume);
        QueueCommand("set_property", "mute", _requestedMuted);
    }

    private ProcessStartInfo CreateStartInfo(string pipeName)
    {
        var info = new ProcessStartInfo
        {
            FileName = _executablePath,
            WorkingDirectory = Path.GetDirectoryName(_executablePath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var parentWindowId = unchecked((uint)_hostWindowHandle.ToInt64());
        info.ArgumentList.Add("--no-config");
        info.ArgumentList.Add("--no-terminal");
        info.ArgumentList.Add("--input-default-bindings=no");
        info.ArgumentList.Add("--input-vo-keyboard=no");
        info.ArgumentList.Add("--osc=no");
        info.ArgumentList.Add("--osd-level=0");
        info.ArgumentList.Add("--idle=yes");
        info.ArgumentList.Add("--force-window=immediate");
        info.ArgumentList.Add("--border=no");
        info.ArgumentList.Add("--keep-open=no");
        info.ArgumentList.Add("--vo=gpu-next");
        info.ArgumentList.Add("--gpu-api=d3d11");
        info.ArgumentList.Add("--gpu-context=d3d11");
        info.ArgumentList.Add("--d3d11-flip=yes");
        info.ArgumentList.Add($"--wid={parentWindowId}");
        info.ArgumentList.Add($"--input-ipc-server={pipeName}");

        if (_profile == MpvPlaybackProfile.SmoothMotion)
        {
            info.ArgumentList.Add("--hwdec=d3d11va");
            info.ArgumentList.Add("--d3d11va-zero-copy=yes");
            info.ArgumentList.Add("--swapchain-depth=3");
            info.ArgumentList.Add("--video-sync=display-resample");
            info.ArgumentList.Add("--interpolation=yes");
            info.ArgumentList.Add("--interpolation-threshold=-1");
            info.ArgumentList.Add("--tscale=linear");
        }
        else
        {
            info.ArgumentList.Add("--hwdec=auto-safe");
            info.ArgumentList.Add("--swapchain-depth=2");
            info.ArgumentList.Add("--video-sync=audio");
            info.ArgumentList.Add("--interpolation=no");
        }

        return info;
    }

    private async Task StopHostProcessAsync(bool requestGracefulExit)
    {
        Process? process;
        NamedPipeClientStream? pipe;
        Channel<string>? commands;
        CancellationTokenSource? processLifetime;
        Task? readerTask;
        Task? writerTask;

        lock (_sync)
        {
            process = _process;
            pipe = _pipe;
            commands = _commands;
            processLifetime = _processLifetime;
            readerTask = _readerTask;
            writerTask = _writerTask;
            _process = null;
            _pipe = null;
            _commands = null;
            _processLifetime = null;
            _readerTask = null;
            _writerTask = null;
            _properties.Clear();
        }

        if (process is null)
        {
            pipe?.Dispose();
            processLifetime?.Dispose();
            return;
        }

        try
        {
            if (requestGracefulExit && !process.HasExited && commands is not null)
            {
                commands.Writer.TryWrite(SerializeCommand("quit"));
                commands.Writer.TryComplete();
                try
                {
                    await process.WaitForExitAsync()
                        .WaitAsync(GracefulExitDeadline);
                }
                catch (TimeoutException)
                {
                }
            }

            if (!process.HasExited)
            {
                TryKillProcess(process);
                try
                {
                    await process.WaitForExitAsync()
                        .WaitAsync(ForcedExitDeadline);
                }
                catch (TimeoutException)
                {
                }
            }
        }
        finally
        {
            processLifetime?.Cancel();
            commands?.Writer.TryComplete();
            pipe?.Dispose();
            await ObserveBackgroundTaskAsync(readerTask);
            await ObserveBackgroundTaskAsync(writerTask);
            processLifetime?.Dispose();
            process.Dispose();
        }
    }

    private async Task WriteCommandsAsync(
        Stream stream,
        ChannelReader<string> commands,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            leaveOpen: true)
        {
            AutoFlush = true,
        };

        try
        {
            await foreach (var command in commands.ReadAllAsync(cancellationToken))
            {
                await writer.WriteLineAsync(command.AsMemory(), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ReadEventsAsync(
        Stream stream,
        int generation,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    return;
                }

                using var document = JsonDocument.Parse(line);
                HandleMessage(document.RootElement, generation);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (!_disposed)
        {
            Publish(Snapshot with
            {
                State = PlaybackState.Failed,
                Volume = _requestedVolume,
                IsMuted = _requestedMuted,
                ErrorMessage = $"mpv host IPC failed: {exception.Message}",
            });
        }
    }

    private void HandleMessage(JsonElement root, int generation)
    {
        if (generation != Volatile.Read(ref _generation) ||
            !root.TryGetProperty("event", out var eventElement))
        {
            return;
        }

        var eventName = eventElement.GetString();
        switch (eventName)
        {
            case "property-change":
                if (root.TryGetProperty("name", out var nameElement) &&
                    nameElement.GetString() is { Length: > 0 } name &&
                    root.TryGetProperty("data", out var dataElement))
                {
                    lock (_sync)
                    {
                        _properties[name] = dataElement.Clone();
                    }
                }
                break;
            case "start-file":
                Publish(Snapshot with
                {
                    State = PlaybackState.Opening,
                    Volume = _requestedVolume,
                    IsMuted = _requestedMuted,
                    ErrorMessage = null,
                });
                break;
            case "playback-restart":
                _startupLatency ??= _sessionClock.Elapsed;
                Publish(Snapshot with
                {
                    State = PlaybackState.Playing,
                    Volume = _requestedVolume,
                    IsMuted = _requestedMuted,
                    ErrorMessage = null,
                });
                break;
            case "end-file":
                HandleEndFile(root);
                break;
            case "shutdown":
                _sessionClock.Stop();
                break;
        }
    }

    private void HandleEndFile(JsonElement root)
    {
        _sessionClock.Stop();
        var reason = root.TryGetProperty("reason", out var reasonElement)
            ? reasonElement.GetString()
            : null;
        var failed = string.Equals(reason, "error", StringComparison.Ordinal);
        var stopped = string.Equals(reason, "stop", StringComparison.Ordinal) ||
            string.Equals(reason, "quit", StringComparison.Ordinal);
        var error = root.TryGetProperty("error", out var errorElement)
            ? errorElement.GetString()
            : null;

        Publish(Snapshot with
        {
            State = failed
                ? PlaybackState.Failed
                : stopped
                    ? PlaybackState.Stopped
                    : PlaybackState.Ended,
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
            ErrorMessage = failed
                ? $"mpv host could not play the stream: {error ?? "unknown error"}."
                : null,
        });
    }

    private void HandleProcessExit(Process process, int generation)
    {
        if (_disposed || generation != Volatile.Read(ref _generation))
        {
            return;
        }

        int? exitCode = null;
        try
        {
            exitCode = process.ExitCode;
        }
        catch (InvalidOperationException)
        {
        }

        var state = Snapshot.State;
        if (state is PlaybackState.Stopped or PlaybackState.Ended or
            PlaybackState.Disposed)
        {
            return;
        }

        _sessionClock.Stop();
        Publish(Snapshot with
        {
            State = PlaybackState.Failed,
            Volume = _requestedVolume,
            IsMuted = _requestedMuted,
            ErrorMessage = $"mpv host exited unexpectedly{(exitCode is null ? "." : $" with code {exitCode}.")}",
        });
    }

    private void QueueCommand(params object?[] command)
    {
        Channel<string>? commands;
        lock (_sync)
        {
            commands = _commands;
        }

        if (commands is null || !commands.Writer.TryWrite(SerializeCommand(command)))
        {
            throw new InvalidOperationException("mpv host IPC is not connected.");
        }
    }

    private void QueueCommandIfRunning(params object?[] command)
    {
        Channel<string>? commands;
        lock (_sync)
        {
            commands = _commands;
        }

        commands?.Writer.TryWrite(SerializeCommand(command));
    }

    private static string SerializeCommand(params object?[] command) =>
        JsonSerializer.Serialize(new { command });

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

    private int? TryReadProcessMetric(Func<Process, int> selector)
    {
        if (_process is null)
        {
            return null;
        }

        try
        {
            return _process.HasExited ? null : selector(_process);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task ObserveBackgroundTaskAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or TimeoutException or
                IOException)
        {
        }
    }

    private static string? GetString(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name)
    {
        if (!properties.TryGetValue(name, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ToString();
    }

    private static double? GetDouble(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name)
    {
        if (!properties.TryGetValue(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out var number))
        {
            return number;
        }

        return double.TryParse(
            value.ToString(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out number)
            ? number
            : null;
    }

    private static long? GetInt64(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name)
    {
        if (!properties.TryGetValue(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt64(out var number))
        {
            return number;
        }

        return long.TryParse(
            value.ToString(),
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out number)
            ? number
            : null;
    }

    private static bool? ParseFlag(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name)
    {
        if (!properties.TryGetValue(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when string.Equals(
                value.GetString(),
                "yes",
                StringComparison.OrdinalIgnoreCase) => true,
            JsonValueKind.String when string.Equals(
                value.GetString(),
                "no",
                StringComparison.OrdinalIgnoreCase) => false,
            _ => null,
        };
    }

    private static int? ToNullableInt(long? value) =>
        value is >= int.MinValue and <= int.MaxValue
            ? (int)value.Value
            : null;

    private static double? PositiveOrNull(double? value) =>
        value is > 0 && double.IsFinite(value.Value)
            ? value
            : null;

    private static double? FiniteOrNull(double? value) =>
        value is not null && double.IsFinite(value.Value)
            ? value
            : null;

    private static double? PercentOrNull(double? value) =>
        value is >= 0 and <= 100 && double.IsFinite(value.Value)
            ? value
            : null;

    private static TimeSpan? SecondsOrNull(double? value) =>
        value is >= 0 && double.IsFinite(value.Value)
            ? TimeSpan.FromSeconds(value.Value)
            : null;

    private static long? NonNegativeOrNull(long? value) =>
        value is >= 0 ? value : null;
}

public sealed record MpvProcessDiagnosticSnapshot(
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
    PlaybackSnapshot Snapshot,
    double? DisplayFramesPerSecond,
    double? EstimatedDisplayFramesPerSecond,
    double? VideoSpeedCorrection,
    double? AudioSpeedCorrection,
    double? VSyncRatio,
    long? MistimedFrames,
    long? DelayedFrames,
    string? PixelFormat,
    bool? InterpolationActive,
    string? VideoSync,
    int? HostProcessId,
    long? HostWorkingSetBytes,
    long? HostPrivateMemoryBytes,
    int? HostHandleCount);
