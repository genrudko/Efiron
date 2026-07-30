using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Efiron.Desktop.Views;

public sealed partial class ProgrammeGuideView
{
    private bool _programmeNavigationAttached;
    private EpgProgrammeBlockItem? _keyboardProgramme;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_programmeNavigationAttached)
        {
            return;
        }

        _programmeNavigationAttached = true;
        ProgrammeRoot.PreviewKeyDown += ProgrammeRoot_PreviewKeyDown;
    }

    private void ProgrammeRoot_PreviewKeyDown(
        object sender,
        KeyRoutedEventArgs e)
    {
        if (IsTextInputOrigin(e.OriginalSource as DependencyObject))
        {
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Left:
            case VirtualKey.GamepadDPadLeft:
                MoveProgrammeFocus(-1, 0, e);
                break;
            case VirtualKey.Right:
            case VirtualKey.GamepadDPadRight:
                MoveProgrammeFocus(1, 0, e);
                break;
            case VirtualKey.Up:
            case VirtualKey.GamepadDPadUp:
                MoveProgrammeFocus(0, -1, e);
                break;
            case VirtualKey.Down:
            case VirtualKey.GamepadDPadDown:
                MoveProgrammeFocus(0, 1, e);
                break;
            case VirtualKey.GamepadLeftShoulder:
                _ = SelectDateAsync(_selectedDate.AddDays(-1), jumpToNow: false);
                _keyboardProgramme = null;
                e.Handled = true;
                break;
            case VirtualKey.GamepadRightShoulder:
                _ = SelectDateAsync(_selectedDate.AddDays(1), jumpToNow: false);
                _keyboardProgramme = null;
                e.Handled = true;
                break;
            case VirtualKey.Enter:
            case VirtualKey.GamepadA:
                if (_keyboardProgramme is not null)
                {
                    ShowProgrammeDetails(_keyboardProgramme);
                    e.Handled = true;
                }
                break;
            case VirtualKey.GamepadB when ProgrammeDetailsCard.Visibility == Visibility.Visible:
                ProgrammeDetailsCard.Visibility = Visibility.Collapsed;
                _selectedProgramme = null;
                e.Handled = true;
                break;
        }
    }

    private void MoveProgrammeFocus(
        int horizontalDelta,
        int verticalDelta,
        KeyRoutedEventArgs e)
    {
        if (_visibleRows.Count == 0)
        {
            return;
        }

        var current = FindProgrammeFromOrigin(e.OriginalSource as DependencyObject) ??
            _keyboardProgramme ??
            FindInitialProgramme();
        if (current is null)
        {
            return;
        }

        var target = verticalDelta == 0
            ? FindHorizontalProgramme(current, horizontalDelta)
            : FindVerticalProgramme(current, verticalDelta);
        if (target is null)
        {
            return;
        }

        _keyboardProgramme = target;
        FocusProgramme(target);
        e.Handled = true;
    }

    private EpgProgrammeBlockItem? FindInitialProgramme()
    {
        var current = _visibleRows
            .SelectMany(static row => row.Programmes)
            .FirstOrDefault(static programme => programme.IsCurrent);
        return current ?? _visibleRows
            .SelectMany(static row => row.Programmes)
            .OrderBy(static programme => programme.Programme.Start)
            .FirstOrDefault();
    }

    private EpgProgrammeBlockItem? FindHorizontalProgramme(
        EpgProgrammeBlockItem current,
        int delta)
    {
        var row = _visibleRows.FirstOrDefault(candidate => string.Equals(
            candidate.StableId,
            current.ChannelStableId,
            StringComparison.Ordinal));
        if (row is null || row.Programmes.Count == 0)
        {
            return current;
        }

        var ordered = row.Programmes
            .OrderBy(static programme => programme.Programme.Start)
            .ToArray();
        var index = Array.FindIndex(ordered, programme => SameProgramme(programme, current));
        index = index < 0 ? 0 : index;
        return ordered[Math.Clamp(index + delta, 0, ordered.Length - 1)];
    }

    private EpgProgrammeBlockItem? FindVerticalProgramme(
        EpgProgrammeBlockItem current,
        int delta)
    {
        var rowIndex = _visibleRows.FindIndex(row => string.Equals(
            row.StableId,
            current.ChannelStableId,
            StringComparison.Ordinal));
        if (rowIndex < 0)
        {
            return current;
        }

        var targetRowIndex = Math.Clamp(
            rowIndex + delta,
            0,
            _visibleRows.Count - 1);
        var targetRow = _visibleRows[targetRowIndex];
        if (targetRow.Programmes.Count == 0)
        {
            return current;
        }

        var stop = current.Programme.Stop ?? current.Programme.Start.AddMinutes(30);
        var currentMiddle = current.Programme.Start +
            TimeSpan.FromTicks((stop - current.Programme.Start).Ticks / 2);
        return targetRow.Programmes
            .OrderBy(programme => Math.Abs(
                (programme.Programme.Start - currentMiddle).Ticks))
            .First();
    }

    private void FocusProgramme(EpgProgrammeBlockItem programme)
    {
        var rowIndex = _visibleRows.FindIndex(row => string.Equals(
            row.StableId,
            programme.ChannelStableId,
            StringComparison.Ordinal));
        if (rowIndex >= 0)
        {
            SetVerticalOffset(
                rowIndex * RowHeight - Math.Max(0, EpgRowsViewport.ActualHeight * 0.32));
        }

        var scale = _pixelsPerMinute / BasePixelsPerMinute;
        var absoluteLeft = programme.Left * scale;
        SetHorizontalOffset(
            absoluteLeft - Math.Max(80, TimelineViewportWidth * 0.24));
        QueueViewportRender();

        DispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Low,
            () =>
            {
                if (_realizedProgrammeButtons.TryGetValue(
                        ProgrammeVisualKey.From(programme),
                        out var button))
                {
                    button.Focus(FocusState.Keyboard);
                }
            });
    }

    private void ShowProgrammeDetails(EpgProgrammeBlockItem programme)
    {
        _selectedProgramme = programme;
        var channel = _catalog?.Channels.FirstOrDefault(snapshot => string.Equals(
            snapshot.Channel.StableId,
            programme.ChannelStableId,
            StringComparison.Ordinal));
        DetailsTimeText.Text = programme.TimeText;
        DetailsChannelText.Text = channel?.Channel.Name ?? string.Empty;
        DetailsTitleText.Text = programme.Title;
        DetailsDescriptionText.Text = string.IsNullOrWhiteSpace(programme.Description)
            ? "Описание передачи не предоставлено"
            : programme.Description;
        ProgrammeDetailsCard.Visibility = Visibility.Visible;
    }

    private static EpgProgrammeBlockItem? FindProgrammeFromOrigin(
        DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { Tag: EpgProgrammeBlockItem programme })
            {
                return programme;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private static bool IsTextInputOrigin(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is TextBox or ComboBox or Slider)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static bool SameProgramme(
        EpgProgrammeBlockItem left,
        EpgProgrammeBlockItem right) =>
        string.Equals(
            left.ChannelStableId,
            right.ChannelStableId,
            StringComparison.Ordinal) &&
        left.Programme.Start == right.Programme.Start &&
        string.Equals(
            left.Programme.Title,
            right.Programme.Title,
            StringComparison.Ordinal);
}
