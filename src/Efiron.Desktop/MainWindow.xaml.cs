using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Xml;
using Efiron.Application.Channels;
using Efiron.Application.Live;
using Efiron.Application.ProgrammeGuide;
using Efiron.Application.Sources;
using Efiron.Desktop.Views;
using Efiron.Infrastructure.Channels;
using Efiron.Infrastructure.Live;
using Efiron.Infrastructure.Playlists;
using Efiron.Infrastructure.ProgrammeGuide;
using Efiron.Infrastructure.Sources;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Efiron.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ResourceLoader _resources;
    private readonly SourceConfigurationService _sourceConfigurationService;
    private readonly IFavoriteChannelStore _favoriteChannelStore;
    private readonly LiveCatalogRefreshService _liveCatalogRefreshService;
    private readonly JsonLiveCatalogCache _liveCatalogCache;
    private readonly HttpClient _httpClient;
    private readonly HashSet<string> _favoriteStableIds = new(StringComparer.Ordinal);
    private readonly string _configurationPath;
    private readonly string _readinessPath;
    private readonly string _liveReadinessPath;
    private readonly string _backgroundCatalogReadinessPath;

    private LiveCatalogSnapshot? _catalog;
    private bool _isFullscreen;

    public MainWindow()
    {
        InitializeComponent();

        _resources = new ResourceLoader(
            ResourceLoader.GetDefaultResourceFilePath(),
            "Resources");
        var localDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Efiron");
        _configurationPath = Path.Combine(localDataDirectory, "sources.json");
        var diagnosticsDirectory = Path.Combine(localDataDirectory, "diagnostics");
        _readinessPath = Path.Combine(
            diagnosticsDirectory,
            "first-useful-paint.json");
        _liveReadinessPath = Path.Combine(
            diagnosticsDirectory,
            "live-vertical-slice.json");
        _backgroundCatalogReadinessPath = Path.Combine(
            diagnosticsDirectory,
            "background-catalog-ready.json");
        _sourceConfigurationService = new SourceConfigurationService(
            new JsonSourceConfigurationStore(_configurationPath));
        _favoriteChannelStore = new JsonFavoriteChannelStore(
            Path.Combine(localDataDirectory, "favorites.json"));
        _liveCatalogCache = new JsonLiveCatalogCache(
            Path.Combine(localDataDirectory, "live-catalog.json.gz"));
        _httpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression =
                DecompressionMethods.GZip |
                DecompressionMethods.Deflate |
                DecompressionMethods.Brotli,
        })
        {
            Timeout = TimeSpan.FromSeconds(45),
        };
        _liveCatalogRefreshService = new LiveCatalogRefreshService(
            new BoundedSourceContentLoader(_httpClient),
            new M3uPlaylistParser(),
            new XmlTvProgrammeGuideParser(),
            new ProgrammeGuideChannelMatcher());

        AppWindow.Title = _resources.GetString("WindowTitle");
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);

        ConfigurationPathText.Text = _configurationPath;
        WindowRoot.Loaded += WindowRoot_Loaded;
        Closed += MainWindow_Closed;
    }

    private async void WindowRoot_Loaded(object sender, RoutedEventArgs e)
    {
        WindowRoot.Loaded -= WindowRoot_Loaded;
        await RecordFirstUsefulPaintAsync();
        await Task.Yield();
        await LoadFavoritesAsync();
        await LoadConfigurationAsync();
    }

    private async Task LoadFavoritesAsync()
    {
        try
        {
            var favoriteStableIds = await _favoriteChannelStore.LoadAsync(
                _lifetime.Token);
            _favoriteStableIds.Clear();
            _favoriteStableIds.UnionWith(favoriteStableIds);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _favoriteStableIds.Clear();
        }
    }

    private async Task LoadConfigurationAsync()
    {
        SetBusy(true);
        UpdateStatus(
            _resources.GetString("StatusLoadingMessage"),
            ColorHelper.FromArgb(255, 37, 134, 255));

        try
        {
            var configuration = await _sourceConfigurationService.LoadAsync(
                _lifetime.Token);
            PlaylistLocationTextBox.Text = configuration.Playlist?.Location ?? string.Empty;
            GuideLocationTextBox.Text = configuration.ProgrammeGuide?.Location ?? string.Empty;

            if (configuration.IsReadyForLiveTv)
            {
                UpdateConfiguredStatus();
                var cachedCatalog = await _liveCatalogCache.LoadAsync(
                    configuration,
                    _lifetime.Token);
                if (cachedCatalog is { Channels.Count: > 0 })
                {
                    ApplyCatalog(cachedCatalog);
                    await ShowLiveWorkspaceAsync();
                    _ = RefreshCatalogInBackgroundAsync(configuration);
                }
                else
                {
                    await LoadPlaylistFirstAsync(configuration);
                }
            }
            else
            {
                UpdateMissingStatus();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (InvalidDataException)
        {
            UpdateMissingStatus();
            ShowMessage(
                InfoBarSeverity.Error,
                "LoadErrorTitle",
                "LoadInvalidMessage");
        }
        catch (IOException)
        {
            UpdateMissingStatus();
            ShowMessage(
                InfoBarSeverity.Error,
                "LoadErrorTitle",
                "LoadIoMessage");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void SaveSourcesButton_Click(object sender, RoutedEventArgs e)
    {
        var playlistLocation = PlaylistLocationTextBox.Text.Trim();
        if (playlistLocation.Length == 0)
        {
            PlaylistLocationTextBox.Focus(FocusState.Programmatic);
            ShowMessage(
                InfoBarSeverity.Warning,
                "ValidationTitle",
                "PlaylistRequiredMessage");
            return;
        }

        SetBusy(true);
        PageMessage.IsOpen = false;

        try
        {
            var configuration = await _sourceConfigurationService.SaveAsync(
                playlistLocation,
                GuideLocationTextBox.Text,
                _lifetime.Token);
            PlaylistLocationTextBox.Text = configuration.Playlist?.Location ?? string.Empty;
            GuideLocationTextBox.Text = configuration.ProgrammeGuide?.Location ?? string.Empty;
            UpdateConfiguredStatus();
            await RefreshCatalogAsync(
                configuration,
                showSuccessMessage: true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (UnauthorizedAccessException)
        {
            ShowMessage(
                InfoBarSeverity.Error,
                "SaveErrorTitle",
                "SaveAccessMessage");
        }
        catch (IOException)
        {
            ShowMessage(
                InfoBarSeverity.Error,
                "SaveErrorTitle",
                "SaveIoMessage");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<bool> LoadPlaylistFirstAsync(
        SourceConfiguration configuration)
    {
        ClearCatalog();
        UpdateRefreshingStatus();

        try
        {
            var playlistCatalog = await _liveCatalogRefreshService.RefreshPlaylistAsync(
                configuration,
                _lifetime.Token);
            await TrySaveCatalogCacheAsync(configuration, playlistCatalog);
            ApplyCatalog(playlistCatalog);
            if (playlistCatalog.Channels.Count > 0)
            {
                await ShowLiveWorkspaceAsync();
                _ = RefreshCatalogInBackgroundAsync(configuration);
            }

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
                NotSupportedException or
                IOException)
        {
            return await RefreshCatalogAsync(
                configuration,
                showSuccessMessage: false);
        }
    }

    private async Task<bool> RefreshCatalogAsync(
        SourceConfiguration configuration,
        bool showSuccessMessage)
    {
        ClearCatalog();
        UpdateRefreshingStatus();

        try
        {
            var catalog = await _liveCatalogRefreshService.RefreshAsync(
                configuration,
                DateTimeOffset.Now,
                _lifetime.Token);
            await TrySaveCatalogCacheAsync(configuration, catalog);
            ApplyCatalog(catalog);

            if (showSuccessMessage)
            {
                var message = string.Format(
                    CultureInfo.CurrentCulture,
                    _resources.GetString("RefreshSuccessFormat"),
                    catalog.Channels.Count,
                    catalog.Categories.Count,
                    catalog.MatchedChannelCount,
                    catalog.PlaylistWarnings.Count + catalog.ProgrammeGuideWarnings.Count);
                ShowMessageText(
                    InfoBarSeverity.Success,
                    _resources.GetString("RefreshSuccessTitle"),
                    message);
            }

            if (catalog.Channels.Count > 0)
            {
                await ShowLiveWorkspaceAsync();
            }

            return true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            UpdateConfiguredStatus();
            ShowMessage(
                InfoBarSeverity.Error,
                "RefreshErrorTitle",
                "RefreshTimeoutMessage");
        }
        catch (HttpRequestException)
        {
            UpdateConfiguredStatus();
            ShowMessage(
                InfoBarSeverity.Error,
                "RefreshErrorTitle",
                "RefreshNetworkMessage");
        }
        catch (FileNotFoundException)
        {
            UpdateConfiguredStatus();
            ShowMessage(
                InfoBarSeverity.Error,
                "RefreshErrorTitle",
                "RefreshFileMessage");
        }
        catch (DirectoryNotFoundException)
        {
            UpdateConfiguredStatus();
            ShowMessage(
                InfoBarSeverity.Error,
                "RefreshErrorTitle",
                "RefreshFileMessage");
        }
        catch (UnauthorizedAccessException)
        {
            UpdateConfiguredStatus();
            ShowMessage(
                InfoBarSeverity.Error,
                "RefreshErrorTitle",
                "RefreshAccessMessage");
        }
        catch (Exception exception) when (
            exception is InvalidDataException or XmlException)
        {
            UpdateConfiguredStatus();
            ShowMessage(
                InfoBarSeverity.Error,
                "RefreshErrorTitle",
                "RefreshInvalidMessage");
        }
        catch (NotSupportedException)
        {
            UpdateConfiguredStatus();
            ShowMessage(
                InfoBarSeverity.Error,
                "RefreshErrorTitle",
                "RefreshUnsupportedMessage");
        }
        catch (IOException)
        {
            UpdateConfiguredStatus();
            ShowMessage(
                InfoBarSeverity.Error,
                "RefreshErrorTitle",
                "RefreshIoMessage");
        }

        return false;
    }

    private async Task RefreshCatalogInBackgroundAsync(
        SourceConfiguration configuration)
    {
        try
        {
            var catalog = await _liveCatalogRefreshService.RefreshAsync(
                configuration,
                DateTimeOffset.Now,
                _lifetime.Token);
            await TrySaveCatalogCacheAsync(configuration, catalog);
            ApplyCatalog(catalog);
            await RecordBackgroundCatalogReadyAsync(catalog);
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
                XmlException or
                NotSupportedException or
                IOException)
        {
            // Keep the last-known-good catalogue visible. The source cache
            // refreshes atomically for the next non-blocking catalogue pass.
        }
    }

    private async Task TrySaveCatalogCacheAsync(
        SourceConfiguration configuration,
        LiveCatalogSnapshot catalog)
    {
        try
        {
            await _liveCatalogCache.SaveAsync(
                configuration,
                catalog,
                _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
        }
    }

    private void ApplyCatalog(LiveCatalogSnapshot catalog)
    {
        _catalog = catalog;
        _liveTvWorkspace?.SetCatalog(catalog, _favoriteStableIds);
        OpenLiveButton.IsEnabled = catalog.Channels.Count > 0;
        UpdateLoadedStatus(catalog.Channels.Count);
    }

    private void ClearCatalog()
    {
        _catalog = null;
        OpenLiveButton.IsEnabled = false;
        if (IsLiveWorkspaceVisible || IsProgrammeGuideWorkspaceVisible)
        {
            ShowSourcesWorkspace();
        }
    }

    private void SetBusy(bool isBusy)
    {
        SaveSourcesButton.IsEnabled = !isBusy;
        PlaylistLocationTextBox.IsEnabled = !isBusy;
        GuideLocationTextBox.IsEnabled = !isBusy;
        SaveProgressRing.IsActive = isBusy;
        SaveProgressRing.Visibility = isBusy
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateRefreshingStatus() =>
        UpdateStatus(
            _resources.GetString("StatusRefreshingMessage"),
            ColorHelper.FromArgb(255, 37, 134, 255));

    private void UpdateConfiguredStatus() =>
        UpdateStatus(
            _resources.GetString("StatusConfiguredMessage"),
            ColorHelper.FromArgb(255, 231, 168, 63));

    private void UpdateMissingStatus() =>
        UpdateStatus(
            _resources.GetString("StatusMissingMessage"),
            ColorHelper.FromArgb(255, 116, 129, 150));

    private void UpdateLoadedStatus(int channelCount)
    {
        var text = string.Format(
            CultureInfo.CurrentCulture,
            _resources.GetString("StatusLoadedFormat"),
            channelCount);
        UpdateStatus(
            text,
            ColorHelper.FromArgb(255, 54, 199, 139));
    }

    private void UpdateStatus(string text, Windows.UI.Color color)
    {
        ConfigurationStatusText.Text = text;
        ConfigurationStatusDot.Fill = new SolidColorBrush(color);
    }

    private void ShowMessage(
        InfoBarSeverity severity,
        string titleKey,
        string messageKey) =>
        ShowMessageText(
            severity,
            _resources.GetString(titleKey),
            _resources.GetString(messageKey));

    private void ShowMessageText(
        InfoBarSeverity severity,
        string title,
        string message)
    {
        PageMessage.Severity = severity;
        PageMessage.Title = title;
        PageMessage.Message = message;
        PageMessage.IsOpen = true;
    }

    private async void OpenLiveButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowLiveWorkspaceAsync();
    }

    private async Task ShowLiveWorkspaceAsync()
    {
        if (_catalog is null || _catalog.Channels.Count == 0)
        {
            return;
        }

        var liveWorkspace = EnsureLiveTvWorkspace();
        liveWorkspace.SetCatalog(_catalog, _favoriteStableIds);
        SourcesWorkspace.Visibility = Visibility.Collapsed;
        if (_programmeGuideWorkspace is not null)
        {
            _programmeGuideWorkspace.Visibility = Visibility.Collapsed;
        }

        liveWorkspace.Visibility = Visibility.Visible;
        WindowContextTitle.Text = _resources.GetString("WindowContextLiveMessage");
        OnLiveWorkspaceShown();

        try
        {
            await Task.Delay(250, _lifetime.Token);
            await RecordLiveReadinessAsync(_catalog);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private void ShowSourcesWorkspace()
    {
        if (_isFullscreen)
        {
            SetFullscreen(false);
        }

        if (_liveTvWorkspace is not null)
        {
            _liveTvWorkspace.Visibility = Visibility.Collapsed;
        }
        if (_programmeGuideWorkspace is not null)
        {
            _programmeGuideWorkspace.Visibility = Visibility.Collapsed;
        }

        SourcesWorkspace.Visibility = Visibility.Visible;
        WindowContextTitle.Text = _resources.GetString("WindowContextSourcesMessage");
        UpdateShellNavigation();
    }

    private void LiveTvWorkspace_BackRequested(object? sender, EventArgs e) =>
        ShowSourcesWorkspace();

    private void LiveTvWorkspace_FullscreenToggleRequested(
        object? sender,
        EventArgs e)
    {
        if (IsLiveWorkspaceVisible)
        {
            SetFullscreen(!_isFullscreen);
        }
    }

    private async void LiveTvWorkspace_FavoriteChanged(
        object? sender,
        FavoriteChangedEventArgs e)
    {
        var changed = e.IsFavorite
            ? _favoriteStableIds.Add(e.StableId)
            : _favoriteStableIds.Remove(e.StableId);
        if (!changed)
        {
            return;
        }

        try
        {
            await _favoriteChannelStore.SaveAsync(
                _favoriteStableIds,
                _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            if (e.IsFavorite)
            {
                _favoriteStableIds.Remove(e.StableId);
            }
            else
            {
                _favoriteStableIds.Add(e.StableId);
            }

            if (_catalog is not null && _liveTvWorkspace is not null)
            {
                _liveTvWorkspace.SetCatalog(_catalog, _favoriteStableIds);
            }
        }
    }

    private void SetFullscreen(bool isFullscreen)
    {
        if (_isFullscreen == isFullscreen)
        {
            return;
        }

        _isFullscreen = isFullscreen;
        TitleBarDragRegion.Visibility = isFullscreen
            ? Visibility.Collapsed
            : Visibility.Visible;
        WindowRoot.RowDefinitions[0].Height = isFullscreen
            ? new GridLength(0)
            : new GridLength(44);
        _liveTvWorkspace?.SetFullscreen(isFullscreen);
        ApplyFullscreenWindowSurfaceState(force: true);
    }

    private void FullscreenKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!IsLiveWorkspaceVisible)
        {
            return;
        }

        SetFullscreen(!_isFullscreen);
        args.Handled = true;
    }

    private void ExitFullscreenKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!_isFullscreen)
        {
            return;
        }

        SetFullscreen(false);
        args.Handled = true;
    }

    private async Task RecordFirstUsefulPaintAsync()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var startedAtUtc = new DateTimeOffset(
                process.StartTime.ToUniversalTime(),
                TimeSpan.Zero);
            var directory = Path.GetDirectoryName(_readinessPath)!;
            Directory.CreateDirectory(directory);
            var evidence = new StartupEvidence(
                (DateTimeOffset.UtcNow - startedAtUtc).TotalMilliseconds,
                DateTimeOffset.UtcNow);
            await File.WriteAllTextAsync(
                _readinessPath,
                JsonSerializer.Serialize(evidence),
                _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task RecordLiveReadinessAsync(LiveCatalogSnapshot catalog)
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var startedAtUtc = new DateTimeOffset(
                process.StartTime.ToUniversalTime(),
                TimeSpan.Zero);
            var recordedAtUtc = DateTimeOffset.UtcNow;
            var directory = Path.GetDirectoryName(_liveReadinessPath)!;
            Directory.CreateDirectory(directory);
            var evidence = new LiveReadinessEvidence(
                ProcessToLiveReadyMilliseconds:
                    (recordedAtUtc - startedAtUtc).TotalMilliseconds,
                ChannelCount: catalog.Channels.Count,
                CategoryCount: catalog.Categories.Count,
                ProgrammeGuideMatchCount: catalog.MatchedChannelCount,
                RetainedProgrammeCount: catalog.RetainedProgrammeCount,
                CatalogCacheHit: catalog.CatalogCacheHit,
                PlaylistSourceCacheHit: catalog.PlaylistSourceCacheHit,
                ProgrammeGuideSourceCacheHit: catalog.ProgrammeGuideSourceCacheHit,
                ProgrammeGuideParseCacheHit: catalog.ProgrammeGuideParseCacheHit,
                LiveWorkspaceVisible: IsLiveWorkspaceVisible,
                WorkingSetBytes: process.WorkingSet64,
                PrivateMemoryBytes: process.PrivateMemorySize64,
                ManagedHeapBytes: GC.GetTotalMemory(forceFullCollection: false),
                RecordedAtUtc: recordedAtUtc);
            await File.WriteAllTextAsync(
                _liveReadinessPath,
                JsonSerializer.Serialize(evidence),
                _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task RecordBackgroundCatalogReadyAsync(
        LiveCatalogSnapshot catalog)
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var directory = Path.GetDirectoryName(_backgroundCatalogReadinessPath)!;
            Directory.CreateDirectory(directory);
            var evidence = new BackgroundCatalogEvidence(
                catalog.Channels.Count,
                catalog.MatchedChannelCount,
                catalog.RetainedProgrammeCount,
                catalog.PlaylistSourceCacheHit,
                catalog.ProgrammeGuideSourceCacheHit,
                catalog.ProgrammeGuideParseCacheHit,
                process.WorkingSet64,
                process.PrivateMemorySize64,
                DateTimeOffset.UtcNow);
            await File.WriteAllTextAsync(
                _backgroundCatalogReadinessPath,
                JsonSerializer.Serialize(evidence),
                _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        Closed -= MainWindow_Closed;
        ReleaseWorkspaceEventHandlers();
        _lifetime.Cancel();
        _httpClient.Dispose();
        _lifetime.Dispose();
    }

    private sealed record StartupEvidence(
        double FirstUsefulPaintMilliseconds,
        DateTimeOffset RecordedAtUtc);

    private sealed record LiveReadinessEvidence(
        double ProcessToLiveReadyMilliseconds,
        int ChannelCount,
        int CategoryCount,
        int ProgrammeGuideMatchCount,
        int RetainedProgrammeCount,
        bool CatalogCacheHit,
        bool PlaylistSourceCacheHit,
        bool ProgrammeGuideSourceCacheHit,
        bool ProgrammeGuideParseCacheHit,
        bool LiveWorkspaceVisible,
        long WorkingSetBytes,
        long PrivateMemoryBytes,
        long ManagedHeapBytes,
        DateTimeOffset RecordedAtUtc);

    private sealed record BackgroundCatalogEvidence(
        int ChannelCount,
        int ProgrammeGuideMatchCount,
        int RetainedProgrammeCount,
        bool PlaylistSourceCacheHit,
        bool ProgrammeGuideSourceCacheHit,
        bool ProgrammeGuideParseCacheHit,
        long WorkingSetBytes,
        long PrivateMemoryBytes,
        DateTimeOffset RecordedAtUtc);
}
