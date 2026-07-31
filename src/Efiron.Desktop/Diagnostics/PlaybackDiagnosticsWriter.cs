using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Efiron.Application.Playback;
using Efiron.Domain.Playback;

namespace Efiron.Desktop.Diagnostics;

public sealed class PlaybackDiagnosticsWriter : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    private static readonly JsonSerializerOptions JsonLineOptions = new()
    {
        WriteIndented = false,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    private readonly string _directory;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Channel<bool> _recordRequests = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly Task _requestLoopTask;

    private CancellationTokenSource? _samplingCancellation;
    private Task? _samplingTask;
    private IPlaybackBackend? _backend;
    private string? _sessionPath;
    private PlaybackState? _lastRecordedState;
    private bool _disposed;

    public PlaybackDiagnosticsWriter(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        Directory.CreateDirectory(directory);
        _requestLoopTask = RequestLoopAsync(_lifetimeCancellation.Token);
    }

    public void Attach(IPlaybackBackend backend)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(backend);

        _samplingCancellation?.Cancel();
        Volatile.Write(ref _backend, backend);
        _sessionPath = Path.Combine(
            _directory,
            $"playback-session-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}.json");
        _lastRecordedState = null;
        _samplingCancellation = new CancellationTokenSource();
        _samplingTask = SampleLoopAsync(
            backend,
            _samplingCancellation.Token);
        RequestRecord();
    }

    public void RequestRecord()
    {
        if (_disposed || Volatile.Read(ref _backend) is null)
        {
            return;
        }

        _recordRequests.Writer.TryWrite(true);
    }

    public Task RecordNowAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequestRecord();
        return Task.CompletedTask;
    }

    public async Task DetachAsync(CancellationToken cancellationToken = default)
    {
        var backend = Interlocked.Exchange(ref _backend, null);
        var samplingCancellation = _samplingCancellation;
        var samplingTask = _samplingTask;

        _samplingCancellation = null;
        _samplingTask = null;
        samplingCancellation?.Cancel();

        if (samplingTask is not null)
        {
            try
            {
                await samplingTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                samplingCancellation?.IsCancellationRequested == true)
            {
            }
        }

        samplingCancellation?.Dispose();
        if (backend is not null)
        {
            await RecordAsync(backend, final: true, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await DetachAsync().ConfigureAwait(false);
        _recordRequests.Writer.TryComplete();
        _lifetimeCancellation.Cancel();
        try
        {
            await _requestLoopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _lifetimeCancellation.Dispose();
        _writeLock.Dispose();
    }

    private async Task RequestLoopAsync(CancellationToken cancellationToken)
    {
        await foreach (var _ in _recordRequests.Reader.ReadAllAsync(cancellationToken)
                           .ConfigureAwait(false))
        {
            var backend = Volatile.Read(ref _backend);
            if (backend is not null)
            {
                await RecordAsync(backend, final: false, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task SampleLoopAsync(
        IPlaybackBackend backend,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(cancellationToken)
                   .ConfigureAwait(false))
        {
            if (ReferenceEquals(backend, Volatile.Read(ref _backend)))
            {
                RequestRecord();
            }
        }
    }

    private async Task RecordAsync(
        IPlaybackBackend backend,
        bool final,
        CancellationToken cancellationToken)
    {
        PlaybackBackendDiagnostics sample;
        try
        {
            sample = backend.CaptureDiagnostics();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_directory);
            var currentPath = Path.Combine(_directory, "playback-current.json");
            await File.WriteAllTextAsync(
                    currentPath,
                    JsonSerializer.Serialize(sample, JsonOptions),
                    cancellationToken)
                .ConfigureAwait(false);

            var samplesPath = Path.Combine(_directory, "playback-samples.jsonl");
            await File.AppendAllTextAsync(
                    samplesPath,
                    JsonSerializer.Serialize(sample, JsonLineOptions) +
                    Environment.NewLine,
                    cancellationToken)
                .ConfigureAwait(false);

            if (sample.PlaybackState == PlaybackState.Failed &&
                _lastRecordedState != PlaybackState.Failed)
            {
                var errorPath = Path.Combine(
                    _directory,
                    $"playback-error-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}.json");
                await File.WriteAllTextAsync(
                        errorPath,
                        JsonSerializer.Serialize(sample, JsonOptions),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            _lastRecordedState = sample.PlaybackState;

            if (final && _sessionPath is not null)
            {
                await File.WriteAllTextAsync(
                        _sessionPath,
                        JsonSerializer.Serialize(sample, JsonOptions),
                        cancellationToken)
                    .ConfigureAwait(false);
                _sessionPath = null;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        finally
        {
            _writeLock.Release();
        }
    }
}