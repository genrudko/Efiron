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
                MoveProgrammeFocus(horizontalDelta: -1, verticalDelta: 0, e);
                break;
            case VirtualKey.Right:
            case VirtualKey.GamepadDPadRight:
                MoveProgrammeFocus(horizontalDelta: 1, verticalDelta: 0, e);
                break;
            case VirtualKey.Up:
            case VirtualKey.GamepadDPadUp:
                MoveProgrammeFocus(horizontalDelta: 0, verticalDelta: -1, e);
                break;
            case VirtualKey.Down:
            case VirtualKey.GamepadDPadDown:
                MoveProgrammeFocus(horizontalDelta: 0, verticalDelta: 1, e);
                break;
            case VirtualKey.GamepadLeftShoulder:
                SelectDate(_selectedDate.AddDays(-1), jumpToNow: false);
                _keyboardProgramme = null;
                e.Handled = true;
                break;
            case VirtualKey.GamepadRightShoulder:
                SelectDate(_selectedDate.AddDays(1), jumpToNow: false);
                _keyboardProgramme = null;
                e.Handled = true;
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
        if (index < 0)
        {
            index = 0;
        }

        return ordered[Math.Clamp(index + delta, 0, ordered.Length - 1)];
    }

    private EpgProgrammeBlockItem? FindVerticalProgramme(
        EpgProgrammeBlockItem current,
        int delta)
    {
        var rowIndex = _visibleRows
            .Select((row, index) => (row, index))
            .FirstOrDefault(candidate => string.Equals(
                candidate.row.StableId,
                current.ChannelStableId,
                StringComparison.Ordinal))
            .index;
        var targetRowIndex = Math.Clamp(
            rowIndex + delta,
            0,
            _visibleRows.Count - 1);
        var targetRow = _visibleRows[targetRowIndex];
        if (targetRow.Programmes.Count == 0)
        {
            return current;
        }

        var currentMiddle = current.Programme.Start +
            TimeSpan.FromTicks(
                ((current.Programme.Stop ?? current.Programme.Start.AddMinutes(30)) -
                 current.Programme.Start).Ticks / 2);
        return targetRow.Programmes
            .OrderBy(programme => Math.Abs(
                (programme.Programme.Start - currentMiddle).Ticks))
            .First();
    }

    private void FocusProgramme(EpgProgrammeBlockItem programme)
    {
        var row = _visibleRows.FirstOrDefault(candidate => string.Equals(
            candidate.StableId,
            programme.ChannelStableId,
            StringComparison.Ordinal));
        if (row is not null)
        {
            ChannelRowsListView.ScrollIntoView(row);
            TimelineRowsListView.ScrollIntoView(row);
        }

        var targetOffset = Math.Clamp(
            programme.Left - Math.Max(80, TimelineViewportGrid.ActualWidth * 0.24),
            0,
            Math.Max(0, TimelineWidth - TimelineViewportGrid.ActualWidth));
        TimelineHorizontalScrollViewer.ChangeView(
            targetOffset,
            null,
            null,
            false);

        DispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Low,
            () => FindProgrammeButton(TimelineRowsListView, programme)?
                .Focus(FocusState.Keyboard));
    }

    private static Button? FindProgrammeButton(
        DependencyObject root,
        EpgProgrammeBlockItem programme)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Button { Tag: EpgProgrammeBlockItem candidate } button &&
                SameProgramme(candidate, programme))
            {
                return button;
            }

            var nested = FindProgrammeButton(child, programme);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
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
            if (source is TextBox or ComboBox)
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
