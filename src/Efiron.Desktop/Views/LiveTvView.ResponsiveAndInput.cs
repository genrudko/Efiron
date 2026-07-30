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

    private void LiveRoot_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyResponsiveLayout(e.NewSize.Width, force: false);

    private void LiveRoot_LayoutUpdated(object? sender, object e)
    {
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

    private void ApplyResponsiveLayout(double width, bool force)
    {
        var layout = width >= 1200
            ? LiveLayoutKind.Wide
            : width >= 840
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
            ChannelBrowserCard.Visibility = Visibility.Collapsed;
            ProgrammeCard.Visibility = Visibility.Collapsed;
            PlaybackStatusBadge.Visibility = Visibility.Collapsed;
            ChannelBrowserColumn.Width = new GridLength(0);
            PlayerColumn.Width = new GridLength(1, GridUnitType.Star);
            LivePrimaryRow.Height = new GridLength(1, GridUnitType.Star);
            LiveSecondaryRow.Height = new GridLength(0);
            LiveContentGrid.ColumnSpacing = 0;
            LiveContentGrid.RowSpacing = 0;
            Grid.SetColumn(PlayerWorkspace, 0);
            Grid.SetColumnSpan(PlayerWorkspace, 2);
            Grid.SetRow(PlayerWorkspace, 0);
            PlayerSurfaceBorder.MinHeight = 0;
            PlayerControlsBorder.Margin = new Thickness(12, 0, 12, 12);
            ApplyControlDensity(compact: width < 720, medium: false);
            return;
        }

        ChannelBrowserCard.Visibility = Visibility.Visible;
        ProgrammeCard.Visibility = Visibility.Visible;
        PlaybackStatusBadge.Visibility = Visibility.Visible;
        LiveContentGrid.ColumnSpacing = layout == LiveLayoutKind.Compact ? 0 : 18;
        LiveContentGrid.RowSpacing = layout == LiveLayoutKind.Compact ? 12 : 14;
        Grid.SetColumnSpan(PlayerWorkspace, 1);
        PlayerControlsBorder.Margin = new Thickness(0);

        switch (layout)
        {
            case LiveLayoutKind.Wide:
                LiveRoot.Padding = new Thickness(30, 24, 30, 30);
                ChannelBrowserColumn.Width = new GridLength(390);
                PlayerColumn.Width = new GridLength(1, GridUnitType.Star);
                LivePrimaryRow.Height = new GridLength(1, GridUnitType.Star);
                LiveSecondaryRow.Height = new GridLength(0);
                Grid.SetColumn(ChannelBrowserCard, 0);
                Grid.SetRow(ChannelBrowserCard, 0);
                Grid.SetColumn(PlayerWorkspace, 1);
                Grid.SetRow(PlayerWorkspace, 0);
                PlayerSurfaceBorder.MinHeight = 360;
                SetProgrammeLayout(stacked: false);
                ApplyControlDensity(compact: false, medium: false);
                LiveSummaryText.Visibility = Visibility.Visible;
                break;

            case LiveLayoutKind.Medium:
                LiveRoot.Padding = new Thickness(20, 18, 20, 22);
                ChannelBrowserColumn.Width = new GridLength(320);
                PlayerColumn.Width = new GridLength(1, GridUnitType.Star);
                LivePrimaryRow.Height = new GridLength(1, GridUnitType.Star);
                LiveSecondaryRow.Height = new GridLength(0);
                Grid.SetColumn(ChannelBrowserCard, 0);
                Grid.SetRow(ChannelBrowserCard, 0);
                Grid.SetColumn(PlayerWorkspace, 1);
                Grid.SetRow(PlayerWorkspace, 0);
                PlayerSurfaceBorder.MinHeight = 300;
                SetProgrammeLayout(stacked: width < 1030);
                ApplyControlDensity(compact: false, medium: true);
                LiveSummaryText.Visibility = width < 940
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                break;

            default:
                LiveRoot.Padding = new Thickness(14, 12, 14, 16);
                ChannelBrowserColumn.Width = new GridLength(1, GridUnitType.Star);
                PlayerColumn.Width = new GridLength(0);
                LivePrimaryRow.Height = new GridLength(3, GridUnitType.Star);
                LiveSecondaryRow.Height = new GridLength(2, GridUnitType.Star);
                Grid.SetColumn(PlayerWorkspace, 0);
                Grid.SetRow(PlayerWorkspace, 0);
                Grid.SetColumn(ChannelBrowserCard, 0);
                Grid.SetRow(ChannelBrowserCard, 1);
                PlayerSurfaceBorder.MinHeight = 220;
                SetProgrammeLayout(stacked: true);
                ApplyControlDensity(compact: true, medium: false);
                LiveSummaryText.Visibility = Visibility.Collapsed;
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
            ? new GridLength(88)
            : medium
                ? new GridLength(110)
                : new GridLength(150);
        PlaybackControlsGrid.ColumnSpacing = compact ? 6 : 10;
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
                ChannelSearchTextBox.Focus(FocusState.Programmatic);
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
            case VirtualKey.MediaPlayPause:
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
            case VirtualKey.MediaStop:
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
                    Grid.GetColumn(ChannelBrowserCard),
                    Grid.GetRow(ChannelBrowserCard),
                    Grid.GetColumn(PlayerWorkspace),
                    Grid.GetRow(PlayerWorkspace),
                    ProgrammeSecondRow.Height.IsAuto,
                    SelectedChannelPanel.Visibility == Visibility.Visible));
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
        int ChannelColumn,
        int ChannelRow,
        int PlayerColumn,
        int PlayerRow,
        bool ProgrammeStacked,
        bool ChannelInformationVisible);

    private sealed record LayoutEvidence(
        IReadOnlyList<LayoutCapture> Layouts,
        DateTimeOffset RecordedAtUtc);
}
