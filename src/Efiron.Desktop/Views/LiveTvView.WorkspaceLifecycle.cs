using System.Text.Json;
using Efiron.Domain.Playback;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Efiron.Desktop.Views;

public sealed partial class LiveTvView
{
    private long _workspaceVisibilityCallbackToken;
    private bool _workspaceLifecycleEnabled;
    private bool _suspendedForProgrammeGuide;

    internal void EnableWorkspaceLifecycle()
    {
        if (_workspaceLifecycleEnabled)
        {
            return;
        }

        _workspaceLifecycleEnabled = true;
        _workspaceVisibilityCallbackToken = RegisterPropertyChangedCallback(
            VisibilityProperty,
            static (sender, _) =>
            {
                if (sender is LiveTvView view &&
                    view.Visibility == Visibility.Visible)
                {
                    view.DispatcherQueue.TryEnqueue(
                        DispatcherQueuePriority.Low,
                        view.RestoreLiveWorkspaceAsync);
                }
            });
    }

    internal async Task SuspendForProgrammeGuideAsync()
    {
        if (_playbackBackendControllerDisposed)
        {
            return;
        }

        await _playbackBackendSwitchLock.WaitAsync();
        try
        {
            if (_playbackBackendControllerDisposed)
            {
                return;
            }

            _suspendedForProgrammeGuide = true;
            _pendingPlaybackRequest = _currentPlaybackRequest ?? _pendingPlaybackRequest;
            await _playbackDiagnosticsWriter.DetachAsync();
            HideNativePlaybackHost();
            HideFlyleafSurface();
            ReleaseCurrentPlaybackBackend();
            UpdatePlaybackBackendStatus("Playback освобождён для EPG");
            await RecordWorkspaceLifecycleEvidenceAsync(
                "suspended-for-epg",
                backendReleased: _playbackBackend is null && _playbackSession is null);
        }
        finally
        {
            _playbackBackendSwitchLock.Release();
        }
    }

    private async void RestoreLiveWorkspaceAsync()
    {
        if (_playbackBackendControllerDisposed || Visibility != Visibility.Visible)
        {
            return;
        }

        RestoreSelectedChannelPosition();

        if (!_suspendedForProgrammeGuide)
        {
            return;
        }

        _suspendedForProgrammeGuide = false;
        try
        {
            await ActivateAsync();
            RestoreSelectedChannelPosition();
            await RecordWorkspaceLifecycleEvidenceAsync(
                "resumed-from-epg",
                backendReleased: false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            UpdatePlaybackBackendStatus($"Ошибка возврата из EPG: {exception.Message}");
            UpdatePlaybackStatus(
                PlaybackState.Failed,
                _resources.GetString("PlaybackStatusFailedMessage"));
        }
    }

    private void RestoreSelectedChannelPosition()
    {
        var selected = _selectedItem;
        if (selected is null || !_visibleItems.Contains(selected))
        {
            return;
        }

        ChannelListView.SelectedItem = selected;
        ChannelListView.ScrollIntoView(
            selected,
            ScrollIntoViewAlignment.Leading);
        DispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Low,
            () =>
            {
                if (Visibility == Visibility.Visible &&
                    _selectedItem is { } current &&
                    _visibleItems.Contains(current))
                {
                    ChannelListView.SelectedItem = current;
                    ChannelListView.ScrollIntoView(
                        current,
                        ScrollIntoViewAlignment.Leading);
                }
            });
    }

    private static async Task RecordWorkspaceLifecycleEvidenceAsync(
        string state,
        bool backendReleased)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Efiron",
                "diagnostics");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "workspace-playback-lifecycle.json"),
                JsonSerializer.Serialize(new
                {
                    State = state,
                    BackendReleased = backendReleased,
                    ProcessId = Environment.ProcessId,
                    RecordedAtUtc = DateTimeOffset.UtcNow,
                }));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
