using System.Globalization;
using Efiron.App.Playlists;
using Efiron.Core.Epg;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Efiron.App;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer _liveProgrammeRefreshTimer = new()
    {
        Interval = TimeSpan.FromSeconds(15),
    };

    private bool _liveProgrammeWorkspaceInitialized;
    private string? _activeLiveChannelStableId;
    private XmlTvDocument? _indexedLiveEpgDocument;
    private EpgScheduleIndex? _liveScheduleIndex;

    internal void InitializeLiveProgrammeWorkspace()
    {
        if (_liveProgrammeWorkspaceInitialized)
        {
            return;
        }

        _liveProgrammeWorkspaceInitialized = true;
        _liveProgrammeRefreshTimer.Tick += LiveProgrammeRefreshTimer_Tick;
        _liveProgrammeRefreshTimer.Start();

        ChannelListView.ItemClick += LiveProgrammeChannelListView_ItemClick;
        LoadPlaylistButton.Click += LiveProgrammeLoadPlaylistButton_Click;
        LoadEpgButton.Click += LiveProgrammeLoadEpgButton_Click;
        SourceTextBox.TextChanged += LiveProgrammeSourceTextBox_TextChanged;
        RootNavigation.SelectionChanged += LiveProgrammeRootNavigation_SelectionChanged;
        AppWindow.Changed += LiveProgrammeAppWindow_Changed;
        Closed += LiveProgrammeWindow_Closed;

        ShowLiveProgrammeState(_resources.GetString("LiveGuideSelectChannel"));
    }

    private void LiveProgrammeRefreshTimer_Tick(object? sender, object e)
    {
        if (LiveView.Visibility == Visibility.Visible)
        {
            RefreshLiveProgrammePanel();
        }
    }

    private void LiveProgrammeChannelListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ChannelListItem item)
        {
            return;
        }

        _activeLiveChannelStableId = item.Channel.StableId;
        RefreshLiveProgrammePanel();
        ApplyChannelLibraryFilter(item.Channel.StableId);
    }

    private void LiveProgrammeSourceTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _activeLiveChannelStableId = null;
        RefreshLiveProgrammePanel();
    }

    private async void LiveProgrammeLoadPlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        await WaitForButtonOperationAsync(LoadPlaylistButton);
        RefreshLiveProgrammePanel();
    }

    private async void LiveProgrammeLoadEpgButton_Click(object sender, RoutedEventArgs e)
    {
        await WaitForButtonOperationAsync(LoadEpgButton);
        EnsureLiveScheduleIndex();
        RefreshLiveProgrammePanel();
        ApplyChannelLibraryFilter();
    }

    private async Task WaitForButtonOperationAsync(Button button)
    {
        await Task.Yield();
        while (!_isClosing && !button.IsEnabled)
        {
            await Task.Delay(100);
        }
    }

    private void LiveProgrammeRootNavigation_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string tag && tag == "live")
        {
            RefreshLiveProgrammePanel();
        }
    }

    private void LiveProgrammeAppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args) =>
        UpdateLiveProgrammePanelVisibility();

    private void EnsureLiveScheduleIndex()
    {
        if (ReferenceEquals(_indexedLiveEpgDocument, _epgDocument))
        {
            return;
        }

        _indexedLiveEpgDocument = _epgDocument;
        _liveScheduleIndex = _epgDocument is null
            ? null
            : new EpgScheduleIndex(_epgDocument.Programmes);
    }

    private void RefreshLiveProgrammePanel()
    {
        if (!_liveProgrammeWorkspaceInitialized)
        {
            return;
        }

        EnsureLiveScheduleIndex();

        if (string.IsNullOrWhiteSpace(_activeLiveChannelStableId))
        {
            ShowLiveProgrammeState(_resources.GetString("LiveGuideSelectChannel"));
            return;
        }

        if (_epgDocument is null || _liveScheduleIndex is null)
        {
            ShowLiveProgrammeState(_resources.GetString("LiveGuideNotLoaded"));
            return;
        }

        if (_epgMatchResult is null ||
            !_epgMatchResult.PlaylistChannelMatches.TryGetValue(
                _activeLiveChannelStableId,
                out var xmlTvChannelId))
        {
            ShowLiveProgrammeState(_resources.GetString("LiveGuideNoMatch"));
            return;
        }

        var nowNext = _liveScheduleIndex.Find(xmlTvChannelId, DateTimeOffset.Now);
        UpdateCurrentProgramme(nowNext);
        UpdateNextProgramme(nowNext.Next);
    }

    private void UpdateCurrentProgramme(EpgNowNext nowNext)
    {
        if (nowNext.Current is null)
        {
            LiveScreen.NowTitle.Text = _livePresentationResources.GetString("LiveNoCurrent");
            LiveScreen.NowTime.Text = string.Empty;
            LiveScreen.NowCategories.Text = string.Empty;
            LiveScreen.NowProgress.IsIndeterminate = false;
            LiveScreen.NowProgress.Value = 0;
            LiveScreen.NowProgress.Visibility = Visibility.Collapsed;
            LiveScreen.SetSelectedChannelHeader(SelectedChannelText.Text, LiveScreen.NowTitle.Text);
            return;
        }

        var title = GetProgrammeTitle(nowNext.Current);
        LiveScreen.NowTitle.Text = title;
        LiveScreen.NowTime.Text = FormatProgrammeRange(
            nowNext.Current.Start,
            nowNext.EffectiveCurrentStop);
        SetCategories(LiveScreen.NowCategories, nowNext.Current.Categories);
        LiveScreen.NowProgress.Visibility = Visibility.Visible;
        LiveScreen.NowProgress.IsIndeterminate = !nowNext.IsProgressKnown;
        LiveScreen.NowProgress.Value = nowNext.IsProgressKnown
            ? Math.Clamp(nowNext.ProgressPercent, 0, 100)
            : 0;
        LiveScreen.SetSelectedChannelHeader(SelectedChannelText.Text, title);
    }

    private void UpdateNextProgramme(XmlTvProgramme? programme)
    {
        if (programme is null)
        {
            LiveScreen.NextTitle.Text = _livePresentationResources.GetString("LiveNoNext");
            LiveScreen.NextTime.Text = string.Empty;
            LiveScreen.NextCategories.Text = string.Empty;
            return;
        }

        LiveScreen.NextTitle.Text = GetProgrammeTitle(programme);
        LiveScreen.NextTime.Text = FormatProgrammeRange(programme.Start, programme.Stop);
        SetCategories(LiveScreen.NextCategories, programme.Categories);
    }

    private void ShowLiveProgrammeState(string message)
    {
        if (_livePresentationResources is null)
        {
            return;
        }

        LiveScreen.NowTitle.Text = message;
        LiveScreen.NowTime.Text = string.Empty;
        LiveScreen.NowCategories.Text = string.Empty;
        LiveScreen.NowProgress.IsIndeterminate = false;
        LiveScreen.NowProgress.Value = 0;
        LiveScreen.NowProgress.Visibility = Visibility.Collapsed;
        LiveScreen.NextTitle.Text = _livePresentationResources.GetString("LiveNoNext");
        LiveScreen.NextTime.Text = string.Empty;
        LiveScreen.NextCategories.Text = string.Empty;
        LiveScreen.SetSelectedChannelHeader(SelectedChannelText.Text, message);
    }

    private string GetProgrammeTitle(XmlTvProgramme programme) =>
        string.IsNullOrWhiteSpace(programme.Title)
            ? _resources.GetString("ProgrammeUntitled")
            : programme.Title;

    private static string FormatProgrammeRange(
        DateTimeOffset start,
        DateTimeOffset? stop)
    {
        var localStart = start.ToLocalTime();
        return stop is null
            ? string.Format(CultureInfo.CurrentCulture, "{0:t}–…", localStart)
            : string.Format(
                CultureInfo.CurrentCulture,
                "{0:t}–{1:t}",
                localStart,
                stop.Value.ToLocalTime());
    }

    private static void SetCategories(TextBlock target, IReadOnlyList<string> categories)
    {
        target.Text = string.Join(" • ", categories);
        target.Visibility = categories.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void UpdateLiveProgrammePanelVisibility()
    {
        if (!_liveProgrammeWorkspaceInitialized)
        {
            return;
        }

        LiveScreen.SetFullscreenLayout(_isFullscreen);
    }

    private void LiveProgrammeWindow_Closed(object sender, WindowEventArgs args)
    {
        _liveProgrammeRefreshTimer.Stop();
        _liveProgrammeRefreshTimer.Tick -= LiveProgrammeRefreshTimer_Tick;
        ChannelListView.ItemClick -= LiveProgrammeChannelListView_ItemClick;
        LoadPlaylistButton.Click -= LiveProgrammeLoadPlaylistButton_Click;
        LoadEpgButton.Click -= LiveProgrammeLoadEpgButton_Click;
        SourceTextBox.TextChanged -= LiveProgrammeSourceTextBox_TextChanged;
        RootNavigation.SelectionChanged -= LiveProgrammeRootNavigation_SelectionChanged;
        AppWindow.Changed -= LiveProgrammeAppWindow_Changed;
        Closed -= LiveProgrammeWindow_Closed;
    }
}
