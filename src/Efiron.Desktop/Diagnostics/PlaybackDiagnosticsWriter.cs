using System.Text.Json;
using System.Text.Json.Serialization;
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

    private CancellationTokenSource? _samplingCancellation;
    private Task? _samplingTask;
    private IPlaybackBackend? _backend;
    private string? _sessionPath;
    private bool _disposed;

    public PlaybackDiagnosticsWriter(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public void Attach(IPlaybackBackend backend)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(backend);

        _samplingCancellation?.Cancel();
        _backend = backend;
        _sessionPath = Path.Combine(
            _directory,
            $"playback-session-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}.json");
        _samplingCancellation = new CancellationTokenSource();
        _samplingTask = SampleLoopAsync(
            backend,
            _samplingCancellation.Token);
        _ = RecordAsync(backend, final: false, CancellationToken.None);
    }

    public Task RecordNowAsync(CancellationToken cancellationToken = default)
    {
        var backend = _backend;
        return backend is null
            ? Task.CompletedTask
            : RecordAsync(backend, final: false, cancellationToken);
    }

    public async Task DetachAsync(CancellationToken cancellationToken = default)
    {
        var backend = _backend;
        var samplingCancellation = _samplingCancellation;
        var samplingTask = _samplingTask;

        _backend = null;
        _samplingCancellation = null;
        _samplingTask = null;
        samplingCancellation?.Cancel();

        if (samplingTask is not null)
        {
            try
            {
                await samplingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
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
        _writeLock.Dispose();
    }

    private async Task SampleLoopAsync(
        IPlaybackBackend backend,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(cancellationToken)
                   .ConfigureAwait(false))
        {
            await RecordAsync(backend, final: false, cancellationToken)
                .ConfigureAwait(false);
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

            if (sample.PlaybackState == PlaybackState.Failed)
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
