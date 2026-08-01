using System.Text.Json;
using System.Xml;
using Efiron.Application.Sources;

namespace Efiron.Desktop;

public sealed partial class MainWindow
{
    private readonly object _backgroundCatalogRefreshGate = new();
    private Task<bool>? _backgroundCatalogRefreshTask;
    private string? _backgroundCatalogRefreshKey;

    private Task<bool> StartOrGetBackgroundCatalogRefresh(
        SourceConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
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

            var task = RefreshCatalogInBackgroundCoreAsync(configuration);
            _backgroundCatalogRefreshTask = task;
            _backgroundCatalogRefreshKey = key;
            _ = ReleaseBackgroundCatalogRefreshTaskAsync(task, key);
            return task;
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
                _lifetime.Token);
            await TrySaveProgrammeGuideCatalogCacheAsync(configuration, catalog);
            await TrySaveCatalogCacheAsync(configuration, catalog);
            ApplyCatalog(catalog);
            await RecordBackgroundCatalogReadyAsync(catalog);
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
            await RecordBackgroundCatalogErrorAsync(exception);
            return false;
        }
    }

    private async Task ReleaseBackgroundCatalogRefreshTaskAsync(
        Task<bool> task,
        string key)
    {
        try
        {
            await task;
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

    private async Task RecordBackgroundCatalogErrorAsync(Exception exception)
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
                _lifetime.Token);
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
