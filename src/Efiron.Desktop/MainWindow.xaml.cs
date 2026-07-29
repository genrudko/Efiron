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
using Efiron.Infrastructure.Playlists;
using Efiron.Infrastructure.ProgrammeGuide;
using Efiron.Infrastructure.Sources;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Efiron.Desktop;

public sealed partial class MainWindow : Window
{
    private static readonly Stopwatch StartupClock = Stopwatch.StartNew();

    private readonly CancellationTokenSource _lifetime = new();
    private readonly ResourceLoader _resources;
    private readonly SourceConfigurationService _sourceConfigurationService;
    private readonly IFavoriteChannelStore _favoriteChannelStore;
    private readonly LiveCatalogRefreshService _liveCatalogRefreshService;
    private readonly HttpClient _httpClient;
    private readonly HashSet<string> _favoriteStableIds = new(StringComparer.Ordinal);
    private readonly string _configurationPath;
    private readonly string _readinessPath;
    private readonly string _liveReadinessPath;

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
        _sourceConfigurationService = new SourceConfigurationService(
            new JsonSourceConfigurationStore(_configurationPath));
        _favoriteChannelStore = new JsonFavoriteChannelStore(
            Path.Combine(localDataDirectory, "favorites.json"));
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
        LiveTvWorkspace.BackRequested += LiveTvWorkspace_BackRequested;
        LiveTvWorkspace.FullscreenToggleRequested +=
            LiveTvWorkspace_FullscreenToggleRequested;
        LiveTvWorkspace.FavoriteChanged += LiveTvWorkspace_FavoriteChanged;
        WindowRoot.Loaded += WindowRoot_Loaded;
        Closed += MainWindow_Closed;
    }

    private async void WindowRoot_Loaded(object sender, RoutedEventArgs e)
    {
        WindowRoot.Loaded -= WindowRoot_Loaded;
        await RecordFirstUsefulPaintAsync();
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
                await RefreshCatalogAsync(
                    configuration,
                    showSuccessMessage: false);
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
            _catalog = catalog;
            LiveTvWorkspace.SetCatalog(catalog, _favoriteStableIds);
            OpenLiveButton.IsEnabled = catalog.Channels.Count > 0;
            UpdateLoadedStatus(catalog.Channels.Count);

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

    private void ClearCatalog()
    {
        _catalog = null;
        OpenLiveButton.IsEnabled = false;
        if (LiveTvWorkspace.Visibility == Visibility.Visible)
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

        SourcesWorkspace.Visibility = Visibility.Collapsed;
        LiveTvWorkspace.Visibility = Visibility.Visible;
        WindowContextTitle.Text = _resources.GetString("WindowContextLiveMessage");

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

        LiveTvWorkspace.Visibility = Visibility.Collapsed;
        SourcesWorkspace.Visibility = Visibility.Visible;
        WindowContextTitle.Text = _resources.GetString("WindowContextSourcesMessage");
    }

    private void LiveTvWorkspace_BackRequested(object? sender, EventArgs e) =>
        ShowSourcesWorkspace();

    private void LiveTvWorkspace_FullscreenToggleRequested(
        object? sender,
        EventArgs e)
    {
        if (LiveTvWorkspace.Visibility == Visibility.Visible)
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

            if (_catalog is not null)
            {
                LiveTvWorkspace.SetCatalog(_catalog, _favoriteStableIds);
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
        AppWindow.SetPresenter(isFullscreen
            ? AppWindowPresenterKind.FullScreen
            : AppWindowPresenterKind.Default);
        TitleBarDragRegion.Visibility = isFullscreen
            ? Visibility.Collapsed
            : Visibility.Visible;
        WindowRoot.RowDefinitions[0].Height = isFullscreen
            ? new GridLength(0)
            : new GridLength(44);
        LiveTvWorkspace.SetFullscreen(isFullscreen);
    }

    private void FullscreenKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (LiveTvWorkspace.Visibility != Visibility.Visible)
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
            var directory = Path.GetDirectoryName(_readinessPath)!;
            Directory.CreateDirectory(directory);
            var evidence = new StartupEvidence(
                StartupClock.Elapsed.TotalMilliseconds,
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
            var directory = Path.GetDirectoryName(_liveReadinessPath)!;
            Directory.CreateDirectory(directory);
            var evidence = new LiveReadinessEvidence(
                catalog.Channels.Count,
                catalog.Categories.Count,
                catalog.MatchedChannelCount,
                LiveTvWorkspace.Visibility == Visibility.Visible,
                DateTimeOffset.UtcNow);
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

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        Closed -= MainWindow_Closed;
        LiveTvWorkspace.BackRequested -= LiveTvWorkspace_BackRequested;
        LiveTvWorkspace.FullscreenToggleRequested -=
            LiveTvWorkspace_FullscreenToggleRequested;
        LiveTvWorkspace.FavoriteChanged -= LiveTvWorkspace_FavoriteChanged;
        LiveTvWorkspace.DisposePlayback();
        _lifetime.Cancel();
        _httpClient.Dispose();
        _lifetime.Dispose();
    }

    private sealed record StartupEvidence(
        double FirstUsefulPaintMilliseconds,
        DateTimeOffset RecordedAtUtc);

    private sealed record LiveReadinessEvidence(
        int ChannelCount,
        int CategoryCount,
        int ProgrammeGuideMatchCount,
        bool LiveWorkspaceVisible,
        DateTimeOffset RecordedAtUtc);
}
