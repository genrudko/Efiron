using Microsoft.UI.Xaml;

namespace Efiron.Desktop.Views;

public sealed partial class ProgrammeGuideView
{
    private bool _workspaceDisposed;

    public void DisposeWorkspace()
    {
        if (_workspaceDisposed)
        {
            return;
        }

        _workspaceDisposed = true;

        Loaded -= ProgrammeGuideView_Loaded;
        Unloaded -= ProgrammeGuideView_Unloaded;
        ProgrammeRoot.ActualThemeChanged -= ProgrammeRoot_ActualThemeChanged;

        if (_programmeNavigationAttached)
        {
            ProgrammeRoot.PreviewKeyDown -= ProgrammeRoot_PreviewKeyDown;
            _programmeNavigationAttached = false;
        }

        StopSmoothVerticalScroll();

        if (_clockTimer is not null)
        {
            _clockTimer.Stop();
            _clockTimer.Tick -= ClockTimer_Tick;
            _clockTimer = null;
        }

        CancelAndDispose(ref _filterDebounceCancellation);
        CancelAndDispose(ref _programmeProjectionCancellation);
        ReleasePersistentVerticalScrollBar();

        _catalog = null;
        _projectionCacheCatalog = null;
        _projectionCache.Clear();
        _allRows.Clear();
        _visibleRows.Clear();
        _realizedProgrammeButtons.Clear();
        _rowVisualPool.Clear();

        _selectedProgramme = null;
        _keyboardProgramme = null;
        _rowsSurfaceTranslate = null;
        _projectionBusy = false;
        _renderQueued = false;
        _realizedBandStart = -1;
        _realizedBandEnd = -1;

        EpgRowsCanvas.Children.Clear();
        EpgRowsCanvas.RenderTransform = null;
        EpgRowsViewport.Clip = null;
        TimelineHeaderCanvas.Children.Clear();
        ProgrammeCategoryComboBox.Items.Clear();
        ProgrammeDetailsCard.Visibility = Visibility.Collapsed;

        PlayChannelRequested = null;
    }

    private void ReleasePersistentVerticalScrollBar()
    {
        if (!_persistentVerticalScrollHooked)
        {
            return;
        }

        EpgVerticalScrollBar.ValueChanged -=
            PersistentVerticalScrollRange_ValueChanged;
        EpgRowsViewport.SizeChanged -=
            PersistentVerticalScrollViewport_SizeChanged;
        ProgrammeRoot.ActualThemeChanged -=
            PersistentVerticalScrollTheme_ActualThemeChanged;

        if (_persistentVerticalScrollThumb is not null)
        {
            _persistentVerticalScrollThumb.ReleasePointerCaptures();
            _persistentVerticalScrollThumb.PointerPressed -=
                PersistentVerticalScrollThumb_PointerPressed;
            _persistentVerticalScrollThumb.PointerMoved -=
                PersistentVerticalScrollThumb_PointerMoved;
            _persistentVerticalScrollThumb.PointerReleased -=
                PersistentVerticalScrollThumb_PointerReleased;
            _persistentVerticalScrollThumb.PointerCanceled -=
                PersistentVerticalScrollThumb_PointerCanceled;
            _persistentVerticalScrollThumb.PointerCaptureLost -=
                PersistentVerticalScrollThumb_PointerCaptureLost;
            _persistentVerticalScrollThumb.PointerEntered -=
                PersistentVerticalScrollThumb_PointerEntered;
            _persistentVerticalScrollThumb.PointerExited -=
                PersistentVerticalScrollThumb_PointerExited;
        }

        if (_persistentVerticalScrollRail is not null)
        {
            _persistentVerticalScrollRail.PointerPressed -=
                PersistentVerticalScrollRail_PointerPressed;
            _persistentVerticalScrollRail.PointerWheelChanged -=
                PersistentVerticalScrollRail_PointerWheelChanged;
            _persistentVerticalScrollRail.SizeChanged -=
                PersistentVerticalScrollRail_SizeChanged;
            EpgSurfaceGrid.Children.Remove(_persistentVerticalScrollRail);
        }

        _persistentVerticalScrollThumb = null;
        _persistentVerticalScrollRail = null;
        _persistentVerticalScrollHooked = false;
        _persistentVerticalScrollDragging = false;
        _persistentVerticalScrollPointerOver = false;
        _persistentVerticalScrollPointerId = 0;
    }

    private static void CancelAndDispose(
        ref CancellationTokenSource? cancellation)
    {
        var current = Interlocked.Exchange(ref cancellation, null);
        if (current is null)
        {
            return;
        }

        try
        {
            current.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            current.Dispose();
        }
    }
}
