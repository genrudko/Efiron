using Efiron.App.Localization;
using Efiron.Core.Playback;
using LibVLCSharp.Platforms.Windows;
using LibVLCSharp.Shared;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Windows.Globalization;

namespace Efiron.App;

public sealed partial class MainWindow : Window
{
    private ResourceLoader _resources = null!;
    private LibVLC? _libVlc;
    private MediaPlayer? _mediaPlayer;
    private Media? _currentMedia;
    private bool _isClosing;
    private bool _isSelectingInitialLanguage;

    public MainWindow()
    {
        InitializeComponent();

        _resources = new ResourceLoader();
        Title = _resources.GetString("WindowTitle");
        StatusText.Text = _resources.GetString("StatusReady");

        VideoView.Initialized += VideoView_Initialized;
        Closed += MainWindow_Closed;

        RootNavigation.SelectedItem = LiveNavigationItem;
        SelectConfiguredLanguage();
        ShowSection("live");
    }

    private void VideoView_Initialized(object sender, InitializedEventArgs e)
    {
        _libVlc = new LibVLC(enableDebugLogs: true, e.SwapChainOptions);
        _mediaPlayer = new MediaPlayer(_libVlc);
        _mediaPlayer.EncounteredError += MediaPlayer_EncounteredError;
        VideoView.MediaPlayer = _mediaPlayer;
        StatusText.Text = _resources.GetString("StatusMediaReady");
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_libVlc is null || _mediaPlayer is null)
        {
            StatusText.Text = _resources.GetString("StatusMediaNotReady");
            return;
        }

        if (!Uri.TryCreate(SourceTextBox.Text?.Trim(), UriKind.Absolute, out var source))
        {
            StatusText.Text = _resources.GetString("StatusInvalidSource");
            return;
        }

        try
        {
            var request = new PlaybackRequest(source);
            _currentMedia?.Dispose();
            _currentMedia = new Media(_libVlc, request.Source);
            _mediaPlayer.Play(_currentMedia);
            PlayerEmptyState.Visibility = Visibility.Collapsed;
            StatusText.Text = _resources.GetString("StatusOpeningStream");
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            StatusText.Text = exception.Message;
        }
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaPlayer is null)
        {
            return;
        }

        _mediaPlayer.Pause();
        StatusText.Text = _resources.GetString("StatusPlaybackToggled");
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaPlayer is null)
        {
            return;
        }

        _mediaPlayer.Stop();
        PlayerEmptyState.Visibility = Visibility.Visible;
        StatusText.Text = _resources.GetString("StatusStopped");
    }

    private void MediaPlayer_EncounteredError(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            StatusText.Text = _resources.GetString("StatusPlaybackError");
            PlayerEmptyState.Visibility = Visibility.Visible;
        });
    }

    private void RootNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ShowSection("settings");
            return;
        }

        if (args.SelectedItemContainer?.Tag is string tag)
        {
            ShowSection(tag);
        }
    }

    private void ShowSection(string section)
    {
        LiveView.Visibility = section == "live" ? Visibility.Visible : Visibility.Collapsed;
        GuideView.Visibility = section == "guide" ? Visibility.Visible : Visibility.Collapsed;
        ArchiveView.Visibility = section == "archive" ? Visibility.Visible : Visibility.Collapsed;
        RecordingsView.Visibility = section == "recordings" ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = section == "settings" ? Visibility.Visible : Visibility.Collapsed;

        HeaderTitle.Text = _resources.GetString(section switch
        {
            "guide" => "HeaderGuide",
            "archive" => "HeaderArchive",
            "recordings" => "HeaderRecordings",
            "settings" => "HeaderSettings",
            _ => "HeaderLive",
        });
    }

    private void SelectConfiguredLanguage()
    {
        var language = AppLanguageStore.Load();
        if (string.IsNullOrWhiteSpace(language))
        {
            language = ApplicationLanguages.Languages.FirstOrDefault() ?? "en-US";
        }

        _isSelectingInitialLanguage = true;
        LanguageComboBox.SelectedIndex = language.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        _isSelectingInitialLanguage = false;
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSelectingInitialLanguage ||
            LanguageComboBox.SelectedItem is not ComboBoxItem item ||
            item.Tag is not string language)
        {
            return;
        }

        AppLanguageStore.Save(language);
        ApplicationLanguages.PrimaryLanguageOverride = language;
        RestartInfoBar.IsOpen = true;
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;

        if (_mediaPlayer is not null)
        {
            _mediaPlayer.EncounteredError -= MediaPlayer_EncounteredError;
            _mediaPlayer.Stop();
        }

        LivePlayerHost.Children.Remove(VideoView);
        _currentMedia?.Dispose();
        _mediaPlayer?.Dispose();
        _libVlc?.Dispose();
    }
}
