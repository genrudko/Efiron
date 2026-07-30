using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Efiron.Desktop;

public sealed partial class MainWindow
{
    private DispatcherQueueTimer? _shellClockTimer;
    private long _shellVisibilityToken;

    private void ShellRoot_Loaded(object sender, RoutedEventArgs e)
    {
        ShellRoot.Loaded -= ShellRoot_Loaded;
        _shellVisibilityToken = LiveTvWorkspace.RegisterPropertyChangedCallback(
            UIElement.VisibilityProperty,
            LiveWorkspace_VisibilityChanged);

        _shellClockTimer = DispatcherQueue.CreateTimer();
        _shellClockTimer.Interval = TimeSpan.FromSeconds(15);
        _shellClockTimer.Tick += ShellClockTimer_Tick;
        _shellClockTimer.Start();
        UpdateShellClock();
        UpdateShellNavigation();
    }

    private async void LiveNavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_catalog is { Channels.Count: > 0 })
        {
            await ShowLiveWorkspaceAsync();
        }
        else
        {
            ShowSourcesWorkspace();
            PlaylistLocationTextBox.Focus(FocusState.Programmatic);
        }

        UpdateShellNavigation();
    }

    private void SettingsNavigationButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSourcesWorkspace();
        UpdateShellNavigation();
    }

    private void GlobalSearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (LiveTvWorkspace.Visibility == Visibility.Visible)
        {
            LiveTvWorkspace.FocusSearch();
        }
        else
        {
            PlaylistLocationTextBox.Focus(FocusState.Programmatic);
        }
    }

    private void SettingsSourcesAnchorButton_Click(object sender, RoutedEventArgs e) =>
        PlaylistLocationTextBox.Focus(FocusState.Programmatic);

    private void SettingsInterfaceAnchorButton_Click(object sender, RoutedEventArgs e) =>
        ThemeComboBox.Focus(FocusState.Programmatic);

    private void LiveWorkspace_VisibilityChanged(
        DependencyObject sender,
        DependencyProperty property) =>
        UpdateShellNavigation();

    private void UpdateShellNavigation()
    {
        var liveVisible = LiveTvWorkspace.Visibility == Visibility.Visible;
        LiveNavigationButton.IsChecked = liveVisible;
        SettingsNavigationButton.IsChecked = !liveVisible;
        WindowContextTitle.Text = _resources.GetString(
            liveVisible
                ? "WindowContextLiveMessage"
                : "WindowContextSourcesMessage");
    }

    private void ShellClockTimer_Tick(
        DispatcherQueueTimer sender,
        object args) =>
        UpdateShellClock();

    private void UpdateShellClock() =>
        AppClockText.Text = DateTimeOffset.Now.ToString("HH:mm");
}