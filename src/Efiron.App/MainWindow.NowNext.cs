using System.Globalization;
using Efiron.App.Playlists;
using Efiron.Core.Epg;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

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

    private Border _liveProgrammePanel = null!;
    private TextBlock _liveNowTitleText = null!;
    private TextBlock _liveNowTimeText = null!;
    private TextBlock _liveNowCategoriesText = null!;
    private ProgressBar _liveNowProgress = null!;
    private TextBlock _liveNextTitleText = null!;
    private TextBlock _liveNextTimeText = null!;
    private TextBlock _liveNextCategoriesText = null!;

    internal void InitializeLiveProgrammeWorkspace()
    {
        if (_liveProgrammeWorkspaceInitialized)
        {
            return;
        }

        _liveProgrammeWorkspaceInitialized = true;
        CreateLiveProgrammePanel();

        _liveProgrammeRefreshTimer.Tick += LiveProgrammeRefreshTimer_Tick;
        _liveProgrammeRefreshTimer.Start();

        ChannelListView.ItemClick += LiveProgrammeChannelListView_ItemClick;
        LoadPlaylistButton.Click += LiveProgrammeLoadPlaylistButton_Click;
        LoadEpgButton.Click += LiveProgrammeLoadEpgButton_Click;
        SourceTextBox.TextChanged += LiveProgrammeSourceTextBox_TextChanged;
        RootNavigation.SelectionChanged += LiveProgrammeRootNavigation_SelectionChanged;
        AppWindow.Changed += LiveProgrammeAppWindow_Changed;
        Closed += LiveProgrammeWindow_Closed;

        RefreshLiveProgrammePanel();
    }

    private void CreateLiveProgrammePanel()
    {
        LivePlayerGrid.RowDefinitions.Insert(1, new RowDefinition
        {
            Height = GridLength.Auto,
        });
        Grid.SetRow(PlayerSurfaceBorder, 2);
        Grid.SetRow(PlayerControlOverlay, 3);

        var panelGrid = new Grid
        {
            ColumnSpacing = 16,
        };
        panelGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(2, GridUnitType.Star),
        });
        panelGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });

        var nowPanel = new StackPanel
        {
            Spacing = 4,
        };
        nowPanel.Children.Add(CreateSectionLabel(_resources.GetString("LiveNowLabel")));

        _liveNowTitleText = CreateProgrammeTitleText();
        nowPanel.Children.Add(_liveNowTitleText);

        _liveNowTimeText = CreateSecondaryText();
        nowPanel.Children.Add(_liveNowTimeText);

        _liveNowProgress = new ProgressBar
        {
            Height = 4,
            Minimum = 0,
            Maximum = 100,
            Margin = new Thickness(0, 2, 0, 1),
        };
        nowPanel.Children.Add(_liveNowProgress);

        _liveNowCategoriesText = CreateSecondaryText();
        nowPanel.Children.Add(_liveNowCategoriesText);
        panelGrid.Children.Add(nowPanel);

        var nextBorder = new Border
        {
            Padding = new Thickness(16, 0, 0, 0),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(48, 128, 128, 128)),
            BorderThickness = new Thickness(1, 0, 0, 0),
        };
        Grid.SetColumn(nextBorder, 1);

        var nextPanel = new StackPanel
        {
            Spacing = 4,
        };
        nextPanel.Children.Add(CreateSectionLabel(_resources.GetString("LiveNextLabel")));

        _liveNextTitleText = CreateProgrammeTitleText();
        nextPanel.Children.Add(_liveNextTitleText);

        _liveNextTimeText = CreateSecondaryText();
        nextPanel.Children.Add(_liveNextTimeText);

        _liveNextCategoriesText = CreateSecondaryText();
        nextPanel.Children.Add(_liveNextCategoriesText);

        nextBorder.Child = nextPanel;
        panelGrid.Children.Add(nextBorder);

        _liveProgrammePanel = new Border
        {
            Padding = new Thickness(14, 10, 14, 10),
            Background = new SolidColorBrush(ColorHelper.FromArgb(18, 128, 128, 128)),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(40, 128, 128, 128)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = panelGrid,
        };
        Grid.SetRow(_liveProgrammePanel, 1);
        LivePlayerGrid.Children.Add(_liveProgrammePanel);
    }

    private static TextBlock CreateSectionLabel(string text) =>
        new()
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.72,
        };

    private static TextBlock CreateProgrammeTitleText() =>
        new()
        {
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

    private static TextBlock CreateSecondaryText() =>
        new()
        {
            FontSize = 12,
            MaxLines = 1,
            Opacity = 0.72,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

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

        UpdateLiveProgrammePanelVisibility();
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
            _liveNowTitleText.Text = _resources.GetString("LiveGuideNoCurrent");
            _liveNowTimeText.Text = string.Empty;
            _liveNowCategoriesText.Text = string.Empty;
            _liveNowCategoriesText.Visibility = Visibility.Collapsed;
            _liveNowProgress.IsIndeterminate = false;
            _liveNowProgress.Value = 0;
            _liveNowProgress.Visibility = Visibility.Collapsed;
            return;
        }

        _liveNowTitleText.Text = GetProgrammeTitle(nowNext.Current);
        _liveNowTimeText.Text = FormatProgrammeRange(
            nowNext.Current.Start,
            nowNext.EffectiveCurrentStop);
        SetCategories(_liveNowCategoriesText, nowNext.Current.Categories);

        _liveNowProgress.Visibility = Visibility.Visible;
        _liveNowProgress.IsIndeterminate = !nowNext.IsProgressKnown;
        _liveNowProgress.Value = nowNext.IsProgressKnown
            ? Math.Clamp(nowNext.ProgressPercent, 0, 100)
            : 0;
    }

    private void UpdateNextProgramme(XmlTvProgramme? programme)
    {
        if (programme is null)
        {
            _liveNextTitleText.Text = _resources.GetString("LiveGuideNoNext");
            _liveNextTimeText.Text = string.Empty;
            _liveNextCategoriesText.Text = string.Empty;
            _liveNextCategoriesText.Visibility = Visibility.Collapsed;
            return;
        }

        _liveNextTitleText.Text = GetProgrammeTitle(programme);
        _liveNextTimeText.Text = FormatProgrammeRange(programme.Start, programme.Stop);
        SetCategories(_liveNextCategoriesText, programme.Categories);
    }

    private void ShowLiveProgrammeState(string message)
    {
        _liveNowTitleText.Text = message;
        _liveNowTimeText.Text = string.Empty;
        _liveNowCategoriesText.Text = string.Empty;
        _liveNowCategoriesText.Visibility = Visibility.Collapsed;
        _liveNowProgress.IsIndeterminate = false;
        _liveNowProgress.Value = 0;
        _liveNowProgress.Visibility = Visibility.Collapsed;

        _liveNextTitleText.Text = _resources.GetString("LiveGuideNoNext");
        _liveNextTimeText.Text = string.Empty;
        _liveNextCategoriesText.Text = string.Empty;
        _liveNextCategoriesText.Visibility = Visibility.Collapsed;
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
        if (_liveProgrammePanel is null)
        {
            return;
        }

        _liveProgrammePanel.Visibility = _isFullscreen
            ? Visibility.Collapsed
            : Visibility.Visible;
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
