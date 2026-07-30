using Efiron.Application.Playback;
using Efiron.Domain.Playback;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Efiron.Desktop.Views;

public sealed partial class LiveTvView
{
    private bool _presentationPolishEnabled;

    internal void EnablePresentationPolish()
    {
        if (_presentationPolishEnabled)
        {
            return;
        }

        _presentationPolishEnabled = true;
        PlaybackSnapshotChanged += PresentationPolish_PlaybackSnapshotChanged;
        LiveRoot.SizeChanged += PresentationPolish_LiveRootSizeChanged;
        LiveRoot.LayoutUpdated += PresentationPolish_LiveRootLayoutUpdated;
        ApplyCompactChannelWidth(LiveRoot.ActualWidth);
    }

    private void PresentationPolish_PlaybackSnapshotChanged(
        object? sender,
        PlaybackSnapshotChangedEventArgs e)
    {
        if (e.Snapshot.State != PlaybackState.Opening)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Low,
            () =>
            {
                if (_playbackSession?.Snapshot.State == PlaybackState.Opening)
                {
                    PlayerEmptyState.Visibility = Visibility.Collapsed;
                }
            });
    }

    private void PresentationPolish_LiveRootSizeChanged(
        object sender,
        SizeChangedEventArgs e) =>
        ApplyCompactChannelWidth(e.NewSize.Width);

    private void PresentationPolish_LiveRootLayoutUpdated(
        object? sender,
        object e) =>
        ApplyCompactChannelWidth(LiveRoot.ActualWidth);

    private void ApplyCompactChannelWidth(double width)
    {
        if (!_isFullscreen && width is >= 620 and < 760)
        {
            CategoryRailColumn.Width = new GridLength(292);
            ChannelBrowserColumn.Width = new GridLength(1, GridUnitType.Star);
            PlayerColumn.Width = new GridLength(0);
        }
    }
}