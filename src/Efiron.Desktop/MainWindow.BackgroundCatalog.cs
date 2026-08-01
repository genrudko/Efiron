using System.Text.Json;
using System.Xml;
using Efiron.Application.Live;
using Efiron.Application.Sources;

namespace Efiron.Desktop;

public sealed partial class MainWindow
{
    private static readonly TimeSpan PlaylistRefreshDelay =
        TimeSpan.FromSeconds(15);

    private readonly object _backgroundCatalogRefreshGate = new();
    private Task<bool>? _backgroundCatalogRefreshTask;
    private string? _backgroundCatalogRefreshKey;
    private Task? _playlistRefreshTask;
    private string? _playlistRefreshKey;

    private Task<bool> StartOrGetBackgroundCatalogRefresh(
        SourceConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Repair H: a full XMLTV parse must never be part of the Live startup
        // critical path. Startup callers invoke this method before the Live
        // workspace becomes visible; in that state only schedule a delayed,
        // playlist-only cache refresh and return immediately. The Programme
        // workspace is made visible before it requests a full catalogue, so an
        // explicit EPG navigation still executes the full single-flight task.
        if (!IsProgrammeGuideWorkspaceVisible)
        {
            _ = StartOrGetPlaylistRefresh(configuration);
            return Task.FromResult(true);
        }

        var key = CreateBackgroundCatalogRefreshKey(configuration);
        lock (_backgroundCatalogRefreshGate)
        {
            if (_backgroundCatalogRefreshTask is { IsCompleted: false } active &&
                string.Equals(
                    _backgroundCatalogRefreshKey,
                    key,
                    StringComparison.Ordinal))
            {
                return active;
            }

            var task = Task.Run(
                () => RefreshCatalogInBackgroundCoreAsync(configuration),
                _lifetime.Token);
            _backgroundCatalogRefreshTask = task;
            _backgroundCatalogRefreshKey = key;
            _ = ReleaseBackgroundCatalogRefreshTaskAsync(task, key);
            return task;
        }
    }

    private Task StartOrGetPlaylistRefresh(
        SourceConfiguration configuration)
    {
        var key = CreateBackgroundCatalogRefreshKey(configuration);
        lock (_backgroundCatalogRefreshGate)
        {
            if (_playlistRefreshTask is { IsCompleted: false } active &&
                string.Equals(
                    _playlistRefreshKey,
                    key,
                    StringComparison.Ordinal))
            {
                return active;
            }

            var task = Task.Run(
                () => RefreshPlaylistCacheAfterLiveStartupAsync(configuration),
                _lifetime.Token);
            _playlistRefreshTask = task;
            _playlistRefreshKey = key;
            _ = ReleasePlaylistRefreshTaskAsync(task, key);
            return task;
        }
    }

    private async Task RefreshPlaylistCacheAfterLiveStartupAsync(
        SourceConfiguration configuration)
    {
        try
        {
            await Task.Delay(PlaylistRefreshDelay, _lifetime.Token)
                .ConfigureAwait(false);

            lock (_backgroundCatalogRefreshGate)
            {
                if (_backgroundCatalogRefreshTask is { IsCompleted: false })
                {
                    return;
                }
            }

            var catalog = await _liveCatalogRefreshService.RefreshPlaylistAsync(
                    configuration,
                    _lifetime.Token)
                .ConfigureAwait(false);
            await TrySaveCatalogCacheAsync(configuration, catalog)
                .ConfigureAwait(false);
            await RecordPlaylistCatalogReadyAsync(catalog)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or
                HttpRequestException or
                FileNotFoundException or
                DirectoryNotFoundException or
                UnauthorizedAccessException or
                InvalidDataException or
                NotSupportedException or
                IOException)
        {
            await RecordBackgroundCatalogErrorAsync(
                    exception,
                    "playlist-refresh")
                .ConfigureAwait(false);
        }
    }

    private async Task<bool> RefreshCatalogInBackgroundCoreAsync(
        SourceConfiguration configuration)
    {
        try
        {
            var catalog = await _liveCatalogRefreshService.RefreshAsync(
                    configuration,
                    DateTimeOffset.Now,
                    _lifetime.Token)
                .ConfigureAwait(false);
            await TrySaveProgrammeGuideCatalogCacheAsync(configuration, catalog)
                .ConfigureAwait(false);
            await TrySaveCatalogCacheAsync(configuration, catalog)
                .ConfigureAwait(false);

            // Do not apply the full catalogue to Live. The complete schedule is
            // consumed only by the Programme workspace and the local variable
            // becomes collectible after this task completes.
            await RecordBackgroundCatalogReadyAsync(catalog)
                .ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or
                HttpRequestException or
                FileNotFoundException or
                DirectoryNotFoundException or
                UnauthorizedAccessException or
                InvalidDataException or
                XmlException or
                NotSupportedException or
                IOException)
        {
            await RecordBackgroundCatalogErrorAsync(
                    exception,
                    "programme-refresh")
                .ConfigureAwait(false);
            return false;
        }
    }

    private async Task ReleaseBackgroundCatalogRefreshTaskAsync(
        Task<bool> task,
        string key)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // RefreshCatalogInBackgroundCoreAsync translates expected failures
            // into a false result. Keep cleanup defensive for unexpected faults.
        }
        finally
        {
            lock (_backgroundCatalogRefreshGate)
            {
                if (ReferenceEquals(_backgroundCatalogRefreshTask, task) &&
                    string.Equals(
                        _backgroundCatalogRefreshKey,
                        key,
                        StringComparison.Ordinal))
                {
                    _backgroundCatalogRefreshTask = null;
                    _backgroundCatalogRefreshKey = null;
                }
            }
        }
    }

    private async Task ReleasePlaylistRefreshTaskAsync(
        Task task,
        string key)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
        }
        finally
        {
            lock (_backgroundCatalogRefreshGate)
            {
                if (ReferenceEquals(_playlistRefreshTask, task) &&
                    string.Equals(
                        _playlistRefreshKey,
                        key,
                        StringComparison.Ordinal))
                {
                    _playlistRefreshTask = null;
                    _playlistRefreshKey = null;
                }
            }
        }
    }

    private async Task RecordPlaylistCatalogReadyAsync(
        LiveCatalogSnapshot catalog)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Efiron",
                "diagnostics",
                "playlist-background-ready.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var evidence = new
            {
                ProcessElapsedMilliseconds =
                    App.ProcessLifetimeElapsed.TotalMilliseconds,
                ChannelCount = catalog.Channels.Count,
                CategoryCount = catalog.Categories.Count,
                catalog.CatalogCacheHit,
                catalog.PlaylistSourceCacheHit,
                RetainedProgrammeCount = catalog.RetainedProgrammeCount,
                RecordedAtUtc = DateTimeOffset.UtcNow,
            };
            await File.WriteAllTextAsync(
                    path,
                    JsonSerializer.Serialize(evidence),
                    _lifetime.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task RecordBackgroundCatalogErrorAsync(
        Exception exception,
        string operation)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Efiron",
                "diagnostics",
                "background-catalog-error.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var evidence = new
            {
                Operation = operation,
                ExceptionType = exception.GetType().FullName,
                exception.Message,
                exception.StackTrace,
                ProcessElapsedMilliseconds =
                    App.ProcessLifetimeElapsed.TotalMilliseconds,
                RecordedAtUtc = DateTimeOffset.UtcNow,
            };
            await File.WriteAllTextAsync(
                    path,
                    JsonSerializer.Serialize(evidence),
                    _lifetime.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception writeException) when (
            writeException is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string CreateBackgroundCatalogRefreshKey(
        SourceConfiguration configuration) =>
        string.Concat(
            configuration.Playlist?.Location ?? string.Empty,
            "\n",
            configuration.ProgrammeGuide?.Location ?? string.Empty);
}
