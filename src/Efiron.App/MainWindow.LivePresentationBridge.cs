using LibVLCSharp.Platforms.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Efiron.App;

public sealed partial class MainWindow
{
    private Grid LiveView => LiveScreen.RootGridControl;
    private ColumnDefinition LiveSidebarColumn => LiveScreen.CategoryColumnDefinition;
    private Border LiveSidebar => LiveScreen.BrowserSurface;
    private Grid LivePlayerGrid => LiveScreen.PlayerGrid;
    private Border PlayerSurfaceBorder => LiveScreen.PlayerSurface;
    private Grid LivePlayerHost => LiveScreen.PlayerHost;
    private VideoView VideoView => LiveScreen.Video;
    private Grid PlayerInputSurface => LiveScreen.InputSurface;
    private TextBlock PlayerEmptyState => LiveScreen.PlayerEmpty;
    private ProgressRing PlayerOpeningIndicator => LiveScreen.OpeningIndicator;
    private Border PlayerControlOverlay => LiveScreen.PlayerControls;
    private Button PlayerPlayPauseButton => LiveScreen.PlayPauseButton;
    private FontIcon PlayerPlayPauseIcon => LiveScreen.PlayPauseIcon;
    private Button PlayerStopControlButton => LiveScreen.StopButton;
    private Button PlayerMuteButton => LiveScreen.MuteButton;
    private FontIcon PlayerMuteIcon => LiveScreen.MuteIcon;
    private Slider VolumeSlider => LiveScreen.Volume;
    private TextBlock VolumeValueText => LiveScreen.VolumeText;
    private Button PlayerFullscreenButton => LiveScreen.FullscreenButton;
    private FontIcon PlayerFullscreenIcon => LiveScreen.FullscreenIcon;
    private TextBlock SelectedChannelText => LiveScreen.SelectedChannel;
    private TextBox ChannelSearchTextBox => LiveScreen.SearchBox;
    private ListView ChannelListView => LiveScreen.Channels;
    private FrameworkElement ChannelEmptyState => LiveScreen.EmptyChannels;
    private TextBlock PlaylistSummaryText => LiveScreen.ChannelSummary;
}
