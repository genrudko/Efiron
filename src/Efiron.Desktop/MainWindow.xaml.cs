using System.Diagnostics;
using System.Text.Json;
using Efiron.Application.Sources;
using Efiron.Infrastructure.Sources;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Efiron.Desktop;

public sealed partial class MainWindow : Window
{
    private static readonly Stopwatch StartupClock = Stopwatch.StartNew();

    private readonly CancellationTokenSource _lifetime = new();
    private readonly ResourceLoader _resources;
    private readonly SourceConfigurationService _sourceConfigurationService;
    private readonly string _configurationPath;
    private readonly string _readinessPath;

    public MainWindow()
    {
        InitializeComponent();

        _resources = new ResourceLoader(
            ResourceLoader.GetDefaultResourceFilePath(),
            "Resources");
        _configurationPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Efiron",
            "sources.json");
        _readinessPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Efiron",
            "diagnostics",
            "first-useful-paint.json");
        _sourceConfigurationService = new SourceConfigurationService(
            new JsonSourceConfigurationStore(_configurationPath));

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
        await LoadConfigurationAsync();
    }

    private async Task LoadConfigurationAsync()
    {
        SetBusy(true);
        try
        {
            var configuration = await _sourceConfigurationService.LoadAsync(
                _lifetime.Token);
            PlaylistLocationTextBox.Text = configuration.Playlist?.Location ?? string.Empty;
            GuideLocationTextBox.Text = configuration.ProgrammeGuide?.Location ?? string.Empty;
            UpdateConfigurationStatus(configuration.IsReadyForLiveTv);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (InvalidDataException)
        {
            UpdateConfigurationStatus(isConfigured: false);
            ShowMessage(
                InfoBarSeverity.Error,
                "LoadErrorTitle",
                "LoadInvalidMessage");
        }
        catch (IOException)
        {
            UpdateConfigurationStatus(isConfigured: false);
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
            UpdateConfigurationStatus(configuration.IsReadyForLiveTv);
            ShowMessage(
                InfoBarSeverity.Success,
                "SavedTitle",
                "SavedMessage");
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

    private void SetBusy(bool isBusy)
    {
        SaveSourcesButton.IsEnabled = !isBusy;
        PlaylistLocationTextBox.IsEnabled = !isBusy;
        GuideLocationTextBox.IsEnabled = !isBusy;
        SaveProgressRing.IsActive = isBusy;
        SaveProgressRing.Visibility = isBusy
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (isBusy)
        {
            ConfigurationStatusText.Text = _resources.GetString(
                "ConfigurationStatusLoading");
        }
    }

    private void UpdateConfigurationStatus(bool isConfigured)
    {
        ConfigurationStatusText.Text = _resources.GetString(
            isConfigured
                ? "ConfigurationStatusReady"
                : "ConfigurationStatusMissing");
        ConfigurationStatusDot.Fill = new SolidColorBrush(
            isConfigured
                ? ColorHelper.FromArgb(255, 54, 199, 139)
                : ColorHelper.FromArgb(255, 116, 129, 150));
    }

    private void ShowMessage(
        InfoBarSeverity severity,
        string titleKey,
        string messageKey)
    {
        PageMessage.Severity = severity;
        PageMessage.Title = _resources.GetString(titleKey);
        PageMessage.Message = _resources.GetString(messageKey);
        PageMessage.IsOpen = true;
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

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        Closed -= MainWindow_Closed;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private sealed record StartupEvidence(
        double FirstUsefulPaintMilliseconds,
        DateTimeOffset RecordedAtUtc);
}
