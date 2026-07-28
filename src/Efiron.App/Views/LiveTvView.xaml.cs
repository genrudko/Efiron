using LibVLCSharp.Platforms.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Efiron.App.Views;

public sealed partial class LiveTvView : UserControl
{
    private GridLength _categoryWidth = new(176);
    private GridLength _channelWidth = new(288);

    public LiveTvView()
    {
        InitializeComponent();
        PlayerVideoView.Visibility = Visibility.Collapsed;
        Loaded += LiveTvView_Loaded;
    }

    internal Grid RootGridControl => RootGrid;
    internal ColumnDefinition CategoryColumnDefinition => CategoryColumn;
    internal ColumnDefinition ChannelColumnDefinition => ChannelColumn;
    internal Border BrowserSurface => CategorySurface;
    internal Border ChannelBrowserSurface => ChannelSurface;
    internal ListView Categories => CategoryListView;
    internal TextBox SearchBox => ChannelSearchTextBox;
    internal ListView Channels => ChannelListView;
    internal TextBlock ChannelSummary => ChannelSummaryText;
    internal StackPanel EmptyChannels => ChannelEmptyState;
    internal Grid PlayerGrid => LivePlayerGrid;
    internal Border PlayerSurface => PlayerSurfaceBorder;
    internal Grid PlayerHost => LivePlayerHost;
    internal VideoView Video => PlayerVideoView;
    internal Grid InputSurface => PlayerInputSurface;
    internal TextBlock PlayerEmpty => PlayerEmptyState;
    internal ProgressRing OpeningIndicator => PlayerOpeningIndicator;
    internal Border PlayerControls => PlayerControlOverlay;
    internal Button PlayPauseButton => PlayerPlayPauseButton;
    internal FontIcon PlayPauseIcon => PlayerPlayPauseIcon;
    internal Button StopButton => PlayerStopControlButton;
    internal Button MuteButton => PlayerMuteButton;
    internal FontIcon MuteIcon => PlayerMuteIcon;
    internal Slider Volume => VolumeSlider;
    internal TextBlock VolumeText => VolumeValueText;
    internal Button FullscreenButton => PlayerFullscreenButton;
    internal FontIcon FullscreenIcon => PlayerFullscreenIcon;
    internal TextBlock SelectedChannel => SelectedChannelText;
    internal TextBlock PlayerProgramme => PlayerProgrammeText;
    internal Border HeaderOverlay => PlayerHeaderOverlay;
    internal TextBlock NowTitle => NowTitleText;
    internal TextBlock NowTime => NowTimeText;
    internal TextBlock NowCategories => NowCategoriesText;
    internal ProgressBar NowProgress => NowProgressBar;
    internal TextBlock NextTitle => NextTitleText;
    internal TextBlock NextTime => NextTimeText;
    internal TextBlock NextCategories => NextCategoriesText;
    internal InfoBar MessageBar => LiveInfoBar;
    internal Button ConfigureSourcesButton => SourceSetupButton;
    internal Button WelcomeConfigureButton => WelcomeSourceButton;
    internal Button FocusChannelsAction => FocusChannelsButton;
    internal Button FavoriteAction => FavoriteActionButton;
    internal Button ProgrammeAction => ProgrammeInfoButton;
    internal Button ArchiveAction => ArchiveActionButton;
    internal Button MoreAction => MoreActionButton;

    internal void SetHasChannels(bool hasChannels)
    {
        WelcomePanel.Visibility = hasChannels ? Visibility.Collapsed : Visibility.Visible;
        PlayerEmptyState.Visibility = hasChannels ? Visibility.Visible : Visibility.Collapsed;
        ChannelEmptyState.Visibility = hasChannels ? Visibility.Collapsed : Visibility.Visible;
        PlayerHeaderOverlay.Visibility = Visibility.Collapsed;
    }

    internal void SetSourceLoading(bool isLoading)
    {
        SourceLoadingRing.IsActive = isLoading;
        SourceLoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        SourceSetupButton.IsEnabled = !isLoading;
        WelcomeSourceButton.IsEnabled = !isLoading;
    }

    internal void SetSelectedChannelHeader(string? channelName, string? programme)
    {
        SelectedChannelText.Text = channelName ?? string.Empty;
        PlayerStatusText.Text = channelName ?? string.Empty;
        PlayerProgrammeText.Text = programme ?? string.Empty;
        PlayerHeaderOverlay.Visibility = string.IsNullOrWhiteSpace(channelName)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    internal void SetFavoriteAction(bool isFavorite, string addText, string removeText)
    {
        FavoriteActionIcon.Glyph = isFavorite ? "\uE735" : "\uE734";
        FavoriteActionText.Text = isFavorite ? removeText : addText;
        FavoriteActionButton.IsEnabled = ChannelListView.SelectedItem is not null;
    }

    internal void ShowMessage(InfoBarSeverity severity, string title, string message)
    {
        LiveInfoBar.Severity = severity;
        LiveInfoBar.Title = title;
        LiveInfoBar.Message = message;
        LiveInfoBar.IsOpen = true;
    }

    internal void CloseMessage() => LiveInfoBar.IsOpen = false;

    internal void SetFullscreenLayout(bool fullscreen)
    {
        if (fullscreen)
        {
            _categoryWidth = CategoryColumn.Width;
            _channelWidth = ChannelColumn.Width;
            CategoryColumn.Width = new GridLength(0);
            ChannelColumn.Width = new GridLength(0);
            CategorySurface.Visibility = Visibility.Collapsed;
            ChannelSurface.Visibility = Visibility.Collapsed;
            RootGrid.ColumnSpacing = 0;
            NowNextPanel.Visibility = Visibility.Collapsed;
            LiveInfoBar.Visibility = Visibility.Collapsed;
            ActionsRow.Height = new GridLength(0);
            NowNextRow.Height = new GridLength(0);
            MessageRow.Height = new GridLength(0);
            PlayerSurfaceBorder.CornerRadius = new CornerRadius(0);
            PlayerSurfaceBorder.BorderThickness = new Thickness(0);
            PlayerControlOverlay.Margin = new Thickness(16);
        }
        else
        {
            CategoryColumn.Width = _categoryWidth.Value > 0 ? _categoryWidth : new GridLength(176);
            ChannelColumn.Width = _channelWidth.Value > 0 ? _channelWidth : new GridLength(288);
            CategorySurface.Visibility = Visibility.Visible;
            ChannelSurface.Visibility = Visibility.Visible;
            RootGrid.ColumnSpacing = 8;
            NowNextPanel.Visibility = Visibility.Visible;
            LiveInfoBar.Visibility = Visibility.Visible;
            ActionsRow.Height = GridLength.Auto;
            NowNextRow.Height = GridLength.Auto;
            MessageRow.Height = GridLength.Auto;
            PlayerSurfaceBorder.CornerRadius = new CornerRadius(10);
            PlayerSurfaceBorder.BorderThickness = new Thickness(1);
            PlayerControlOverlay.Margin = new Thickness(12);
        }
    }

    internal void ApplySurfaceBrushes(Brush surface, Brush stroke)
    {
        CategorySurface.Background = surface;
        CategorySurface.BorderBrush = stroke;
        ChannelSurface.Background = surface;
        ChannelSurface.BorderBrush = stroke;
        NowNextPanel.Background = surface;
        NowNextPanel.BorderBrush = stroke;
    }

    private void LiveTvView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= LiveTvView_Loaded;
        DispatcherQueue.TryEnqueue(() => PlayerVideoView.Visibility = Visibility.Visible);
    }
}
