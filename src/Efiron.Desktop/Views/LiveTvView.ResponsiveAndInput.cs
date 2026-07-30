using System.Text.Json;
using Efiron.Desktop.Presentation;
using Efiron.Domain.Playback;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Efiron.Desktop.Views;

public sealed partial class LiveTvView
{
    private const string LayoutVerificationEnvironmentVariable =
        "EFIRON_CI_LAYOUT_VERIFICATION";

    private LiveLayoutKind? _appliedLayout;
    private bool? _appliedFullscreen;
    private bool _initialFocusApplied;
    private bool _layoutEvidenceStarted;
    private bool _categoryRailSyncing;

    public void FocusSearch() =>
        ChannelSearchTextBox.Focus(FocusState.Programmatic);

    private void LiveRoot_PresentationLoaded(object sender, RoutedEventArgs e)
    {
        SyncCategoryRail();
        ApplyResponsiveLayout(LiveRoot.ActualWidth, force: true);
    }

    private void LiveRoot_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyResponsiveLayout(e.NewSize.Width, force: false);

    private void LiveRoot_LayoutUpdated(object? sender, object e)
    {
        SyncCategoryRail();

        if (_appliedFullscreen != _isFullscreen)
        {
            ApplyResponsiveLayout(LiveRoot.ActualWidth, force: true);
        }

        if (!_initialFocusApplied &&
            Visibility == Visibility.Visible &&
            ChannelListView.SelectedItem is not null)
        {
            _initialFocusApplied = ChannelListView.Focus(FocusState.Programmatic);
        }

        if (!_layoutEvidenceStarted &&
            string.Equals(
                Environment.GetEnvironmentVariable(LayoutVerificationEnvironmentVariable),
                "1",
                StringComparison.Ordinal) &&
            Visibility == Visibility.Visible &&
            LiveRoot.ActualWidth > 0)
        {
            _layoutEvidenceStarted = true;
            _ = RecordLayoutEvidenceAsync();
        }
    }

    private void SyncCategoryRail()
    {
        if (_categoryRailSyncing)
        {
            return;
        }

        var labels = CategoryComboBox.Items
            .Select(static item => item is ComboBoxItem comboBoxItem
                ? comboBoxItem.Content?.ToString() ?? string.Empty
                : item?.ToString() ?? string.Empty)
            .ToArray();
        var requiresRebuild = CategoryRailListView.Items.Count != labels.Length;
        if (!requiresRebuild)
        {
            for (var index = 0; index < labels.Length; index++)
            {
                if (!string.Equals(
                        CategoryRailListView.Items[index]?.ToString(),
                        labels[index],
                        StringComparison.CurrentCulture))
                {
                    requiresRebuild = true;
                    break;
                }
            }
        }

        _categoryRailSyncing = true;
        try
        {
            if (requiresRebuild)
            {
                CategoryRailListView.Items.Clear();
                foreach (var label in labels)
                {
                    CategoryRailListView.Items.Add(label);
                }
            }

            var selectedIndex = labels.Length == 0
                ? -1
                : Math.Clamp(CategoryComboBox.SelectedIndex, 0, labels.Length - 1);
            if (CategoryRailListView.SelectedIndex != selectedIndex)
            {
                CategoryRailListView.SelectedIndex = selectedIndex;
            }
        }
        finally
        {
            _categoryRailSyncing = false;
        }
    }

    private void CategoryRailListView_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_categoryRailSyncing || CategoryRailListView.SelectedIndex < 0)
        {
            return;
        }

        _categoryRailSyncing = true;
        try
        {
            CategoryComboBox.SelectedIndex = CategoryRailListView.SelectedIndex;
        }
        finally
        {
            _categoryRailSyncing = false;
        }
    }

    private void ApplyResponsiveLayout(double width, bool force)
    {
        var layout = width >= 1160
            ? LiveLayoutKind.Wide
            : width >= 760
                ? LiveLayoutKind.Medium
                : LiveLayoutKind.Compact;

        if (!force && _appliedLayout == layout && _appliedFullscreen == _isFullscreen)
        {
            return;
        }

        _appliedLayout = layout;
        _appliedFullscreen = _isFullscreen;

        if (_isFullscreen)
        {
            CategoryRailCard.Visibility = Visibility.Collapsed;
            ChannelBrowserCard.Visibility = Visibility.Collapsed;
            ProgrammeCard.Visibility = Visibility.Collapsed;
            PlaybackStatusBadge.Visibility = Visibility.Collapsed;
            CategoryRailColumn.Width = new GridLength(0);
            ChannelBrowserColumn.Width = new GridLength(0);
            PlayerColumn.Width = new GridLength(1, GridUnitType.Star);
            LivePrimaryRow.Height = new GridLength(1, GridUnitType.Star);
            LiveSecondaryRow.Height = new GridLength(0);
            LiveContentGrid.ColumnSpacing = 0;
            LiveContentGrid.RowSpacing = 0;
            Grid.SetColumn(PlayerWorkspace, 0);
            Grid.SetColumnSpan(PlayerWorkspace, 3);
            Grid.SetRow(PlayerWorkspace, 0);
            PlayerSurfaceBorder.MinHeight = 0;
            LiveRoot.RowDefinitions[2].Height = new GridLength(0);
            ApplyControlDensity(compact: width < 720, medium: false);
            return;
        }

        CategoryRailCard.Visibility = layout == LiveLayoutKind.Compact
            ? Visibility.Collapsed
            : Visibility.Visible;
        ChannelBrowserCard.Visibility = Visibility.Visible;
        ProgrammeCard.Visibility = Visibility.Visible;
        PlaybackStatusBadge.Visibility = Visibility.Visible;
        Grid.SetColumnSpan(PlayerWorkspace, 1);
        Grid.SetColumnSpan(ChannelBrowserCard, 1);
        LiveRoot.RowDefinitions[2].Height = GridLength.Auto;

        switch (layout)
        {
            case LiveLayoutKind.Wide:
                LiveRoot.Padding = new Thickness(18, 14, 18, 14);
                CategoryRailColumn.Width = new GridLength(164);
                ChannelBrowserColumn.Width = new GridLength(330);
                PlayerColumn.Width = new GridLength(1, GridUnitType.Star);
                LivePrimaryRow.Height = new GridLength(1, GridUnitType.Star);
                LiveSecondaryRow.Height = new GridLength(0);
                LiveContentGrid.ColumnSpacing = 10;
                LiveContentGrid.RowSpacing = 0;
                Grid.SetColumn(CategoryRailCard, 0);
                Grid.SetRow(CategoryRailCard, 0);
                Grid.SetColumn(ChannelBrowserCard, 1);
                Grid.SetRow(ChannelBrowserCard, 0);
                Grid.SetColumn(PlayerWorkspace, 2);
                Grid.SetRow(PlayerWorkspace, 0);
                PlayerSurfaceBorder.MinHeight = 350;
                SetProgrammeLayout(stacked: false);
                ApplyControlDensity(compact: false, medium: false);
                LiveSummaryText.Visibility = Visibility.Visible;
                break;

            case LiveLayoutKind.Medium:
                var narrowMedium = width < 860;
                LiveRoot.Padding = new Thickness(12, 11, 12, 11);
                CategoryRailColumn.Width = new GridLength(narrowMedium ? 110 : 132);
                ChannelBrowserColumn.Width = new GridLength(narrowMedium ? 245 : 286);
                PlayerColumn.Width = new GridLength(1, GridUnitType.Star);
                LivePrimaryRow.Height = new GridLength(1, GridUnitType.Star);
                LiveSecondaryRow.Height = new GridLength(0);
                LiveContentGrid.ColumnSpacing = 8;
                LiveContentGrid.RowSpacing = 0;
                Grid.SetColumn(CategoryRailCard, 0);
                Grid.SetRow(CategoryRailCard, 0);
                Grid.SetColumn(ChannelBrowserCard, 1);
                Grid.SetRow(ChannelBrowserCard, 0);
                Grid.SetColumn(PlayerWorkspace, 2);
                Grid.SetRow(PlayerWorkspace, 0);
                PlayerSurfaceBorder.MinHeight = 280;
                SetProgrammeLayout(stacked: width < 1040);
                ApplyControlDensity(compact: false, medium: true);
                LiveSummaryText.Visibility = Visibility.Collapsed;
                break;

            default:
                var sideBySide = width >= 620;
                LiveRoot.Padding = new Thickness(10, 10, 10, 10);
                LiveContentGrid.ColumnSpacing = sideBySide ? 8 : 0;
                LiveContentGrid.RowSpacing = sideBySide ? 0 : 9;
                SetProgrammeLayout(stacked: true);
                ApplyControlDensity(compact: true, medium: false);
                LiveSummaryText.Visibility = Visibility.Collapsed;

                if (sideBySide)
                {
                    CategoryRailColumn.Width = new GridLength(255);
                    ChannelBrowserColumn.Width = new GridLength(1, GridUnitType.Star);
                    PlayerColumn.Width = new GridLength(0);
                    LivePrimaryRow.Height = new GridLength(1, GridUnitType.Star);
                    LiveSecondaryRow.Height = new GridLength(0);
                    Grid.SetColumn(ChannelBrowserCard, 0);
                    Grid.SetColumnSpan(ChannelBrowserCard, 1);
                    Grid.SetRow(ChannelBrowserCard, 0);
                    Grid.SetColumn(PlayerWorkspace, 1);
                    Grid.SetColumnSpan(PlayerWorkspace, 2);
                    Grid.SetRow(PlayerWorkspace, 0);
                    PlayerSurfaceBorder.MinHeight = 230;
                }
                else
                {
                    CategoryRailColumn.Width = new GridLength(1, GridUnitType.Star);
                    ChannelBrowserColumn.Width = new GridLength(0);
                    PlayerColumn.Width = new GridLength(0);
                    LivePrimaryRow.Height = new GridLength(3, GridUnitType.Star);
                    LiveSecondaryRow.Height = new GridLength(2, GridUnitType.Star);
                    Grid.SetColumn(PlayerWorkspace, 0);
                    Grid.SetColumnSpan(PlayerWorkspace, 3);
                    Grid.SetRow(PlayerWorkspace, 0);
                    Grid.SetColumn(ChannelBrowserCard, 0);
                    Grid.SetColumnSpan(ChannelBrowserCard, 3);
                    Grid.SetRow(ChannelBrowserCard, 1);
                    PlayerSurfaceBorder.MinHeight = 220;
                }
                break;
        }
    }

    private void SetProgrammeLayout(bool stacked)
    {
        ProgrammeSecondColumn.Width = stacked
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);
        ProgrammeSecondRow.Height = stacked
            ? GridLength.Auto
            : new GridLength(0);
        Grid.SetColumn(NextProgrammePanel, stacked ? 0 : 1);
        Grid.SetRow(NextProgrammePanel, stacked ? 1 : 0);
    }

    private void ApplyControlDensity(bool compact, bool medium)
    {
        var hideInformation = compact && LiveRoot.ActualWidth < 650;
        SelectedChannelPanel.Visibility = hideInformation
            ? Visibility.Collapsed
            : Visibility.Visible;
        SelectedInfoColumn.Width = hideInformation
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);
        VolumeColumn.Width = compact
            ? new GridLength(72)
            : medium
                ? new GridLength(90)
                : new GridLength(120);
        PlaybackControlsGrid.ColumnSpacing = compact ? 4 : 7;
    }

    private async void LiveRoot_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var key = e.Key;
        var isTextInput = e.OriginalSource is TextBox or ComboBox;

        if (key is VirtualKey.Escape or VirtualKey.GamepadB)
        {
            if (_isFullscreen)
            {
                FullscreenToggleRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                BackRequested?.Invoke(this, EventArgs.Empty);
            }

            e.Handled = true;
            return;
        }

        if (isTextInput)
        {
            return;
        }

        switch (key)
        {
            case VirtualKey.F:
                FocusSearch();
                e.Handled = true;
                break;
            case VirtualKey.Enter:
            case VirtualKey.GamepadA:
                if (ChannelListView.SelectedItem is LiveChannelItem selected)
                {
                    await SelectChannelAsync(selected);
                    e.Handled = true;
                }
                break;
            case VirtualKey.Space:
                await TogglePlaybackAsync();
                e.Handled = true;
                break;
            case VirtualKey.M:
                if (_playbackSession is not null)
                {
                    _playbackSession.SetMuted(!_playbackSession.Snapshot.IsMuted);
                    e.Handled = true;
                }
                break;
            case VirtualKey.S:
                _playbackSession?.Stop();
                e.Handled = true;
                break;
            case VirtualKey.PageUp:
            case VirtualKey.GamepadLeftShoulder:
                await SelectAdjacentChannelAsync(-1);
                e.Handled = true;
                break;
            case VirtualKey.PageDown:
            case VirtualKey.GamepadRightShoulder:
                await SelectAdjacentChannelAsync(1);
                e.Handled = true;
                break;
        }
    }

    private async Task TogglePlaybackAsync()
    {
        if (_playbackSession?.Snapshot.State == PlaybackState.Playing)
        {
            _playbackSession.Pause();
            return;
        }

        if (_playbackSession?.Snapshot.State == PlaybackState.Paused)
        {
            _playbackSession.Resume();
            return;
        }

        if (_selectedItem is not null)
        {
            await SelectChannelAsync(_selectedItem);
        }
    }

    private async Task SelectAdjacentChannelAsync(int offset)
    {
        if (_visibleItems.Count == 0)
        {
            return;
        }

        var index = _selectedItem is null
            ? 0
            : _visibleItems.IndexOf(_selectedItem);
        if (index < 0)
        {
            index = 0;
        }

        index = (index + offset + _visibleItems.Count) % _visibleItems.Count;
        var item = _visibleItems[index];
        ChannelListView.SelectedItem = item;
        ChannelListView.ScrollIntoView(item);
        await SelectChannelAsync(item);
    }

    private async Task RecordLayoutEvidenceAsync()
    {
        try
        {
            var actualWidth = LiveRoot.ActualWidth;
            var captures = new List<LayoutCapture>();
            foreach (var width in new[] { 1400d, 1000d, 700d })
            {
                ApplyResponsiveLayout(width, force: true);
                await Task.Yield();
                captures.Add(new LayoutCapture(
                    _appliedLayout?.ToString() ?? string.Empty,
                    Grid.GetColumn(CategoryRailCard),
                    Grid.GetColumn(ChannelBrowserCard),
                    Grid.GetRow(ChannelBrowserCard),
                    Grid.GetColumn(PlayerWorkspace),
                    Grid.GetRow(PlayerWorkspace),
                    ProgrammeSecondRow.Height.IsAuto,
                    CategoryRailCard.Visibility == Visibility.Visible));
            }

            ApplyResponsiveLayout(actualWidth, force: true);
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Efiron",
                "diagnostics",
                "layout-runtime.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(new LayoutEvidence(
                    captures,
                    DateTimeOffset.UtcNow)));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private enum LiveLayoutKind
    {
        Wide,
        Medium,
        Compact,
    }

    private sealed record LayoutCapture(
        string Layout,
        int CategoryColumn,
        int ChannelColumn,
        int ChannelRow,
        int PlayerColumn,
        int PlayerRow,
        bool ProgrammeStacked,
        bool CategoryRailVisible);

    private sealed record LayoutEvidence(
        IReadOnlyList<LayoutCapture> Layouts,
        DateTimeOffset RecordedAtUtc);
}