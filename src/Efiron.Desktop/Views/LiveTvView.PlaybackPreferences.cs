using Efiron.Application.Playback;
using Efiron.Domain.Playback;
using Microsoft.UI.Xaml;

namespace Efiron.Desktop.Views;

public sealed partial class LiveTvView
{
    private readonly object _playbackPreferencesSync = new();

    private PlaybackPreferences _playbackPreferences = PlaybackPreferences.Default;
    private CancellationTokenSource? _playbackPreferencesSave;
    private bool _playbackPreferencesLoadStarted;
    private bool _playbackPreferencesLoaded;
    private bool _playbackPreferencesApplyRunning;
    private bool _playbackPreferencesAppliedToSession;

    private IPlaybackPreferencesStore? PlaybackPreferencesStore =>
        (Microsoft.UI.Xaml.Application.Current as App)?.PlaybackPreferencesStore;

    private void EnsurePlaybackPreferencesHooks()
    {
        if (_playbackPreferencesLoadStarted)
        {
            return;
        }

        _playbackPreferencesLoadStarted = true;
        _ = LoadPlaybackPreferencesAsync();
    }

    private async Task LoadPlaybackPreferencesAsync()
    {
        var store = PlaybackPreferencesStore;
        if (store is null)
        {
            _playbackPreferencesLoaded = true;
            return;
        }

        try
        {
            _playbackPreferences = await store.LoadAsync();
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _playbackPreferences = PlaybackPreferences.Default;
        }
        finally
        {
            _playbackPreferencesLoaded = true;
        }

        await ApplyPlaybackPreferencesAsync();
    }

    private async Task ApplyPlaybackPreferencesAsync()
    {
        if (!_playbackPreferencesLoaded ||
            _playbackPreferencesApplyRunning)
        {
            return;
        }

        _playbackPreferencesApplyRunning = true;
        try
        {
            var preferredStableId =
                _playbackPreferences.SelectedChannelStableId;
            var preferredItem = !string.IsNullOrWhiteSpace(preferredStableId)
                ? _allItems.FirstOrDefault(item => string.Equals(
                    item.Snapshot.Channel.StableId,
                    preferredStableId,
                    StringComparison.Ordinal))
                : null;

            if (preferredItem is not null &&
                !ReferenceEquals(preferredItem, _selectedItem))
            {
                _selectedItem = preferredItem;
                ChannelListView.SelectedItem = preferredItem;
                SelectedChannelText.Text = preferredItem.Name;
                SelectedProgrammeText.Text = preferredItem.CurrentProgramme;
                UpdateSelectedProgramme(preferredItem);

                var activeSnapshot = _playbackSession?.Snapshot;
                if (Visibility == Visibility.Visible &&
                    activeSnapshot?.State is PlaybackState.Opening or
                        PlaybackState.Playing or
                        PlaybackState.Paused &&
                    !string.Equals(
                        activeSnapshot.ChannelStableId,
                        preferredStableId,
                        StringComparison.Ordinal))
                {
                    await SelectChannelAsync(preferredItem);
                }
            }

            ApplyPlaybackPreferencesToSession();
        }
        finally
        {
            _playbackPreferencesApplyRunning = false;
        }
    }

    private void ApplyPlaybackPreferencesToSession()
    {
        if (!_playbackPreferencesLoaded ||
            _playbackSession is null ||
            _playbackPreferencesAppliedToSession)
        {
            return;
        }

        _playbackPreferencesAppliedToSession = true;
        _playbackPreferencesApplyRunning = true;
        try
        {
            _playbackSession.SetVolume(_playbackPreferences.Volume);
            _playbackSession.SetMuted(_playbackPreferences.IsMuted);
        }
        finally
        {
            _playbackPreferencesApplyRunning = false;
        }
    }

    private void HandlePlaybackPreferencesSnapshot(
        PlaybackSnapshot snapshot)
    {
        if (!_playbackPreferencesLoaded ||
            _playbackPreferencesApplyRunning ||
            snapshot.State is PlaybackState.Idle or
                PlaybackState.Disposed or
                PlaybackState.Failed)
        {
            return;
        }

        var next = new PlaybackPreferences(
            snapshot.ChannelStableId ??
                _playbackPreferences.SelectedChannelStableId,
            snapshot.Volume,
            snapshot.IsMuted);
        if (next == _playbackPreferences)
        {
            return;
        }

        _playbackPreferences = next;
        SchedulePlaybackPreferencesSave(next);
    }

    private void SchedulePlaybackPreferencesSave(
        PlaybackPreferences preferences)
    {
        CancellationTokenSource cancellation;
        lock (_playbackPreferencesSync)
        {
            _playbackPreferencesSave?.Cancel();
            _playbackPreferencesSave?.Dispose();
            _playbackPreferencesSave = new CancellationTokenSource();
            cancellation = _playbackPreferencesSave;
        }

        _ = SavePlaybackPreferencesAsync(
            preferences,
            cancellation.Token);
    }

    private async Task SavePlaybackPreferencesAsync(
        PlaybackPreferences preferences,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(350),
                cancellationToken);
            var store = PlaybackPreferencesStore;
            if (store is not null)
            {
                await store.SaveAsync(preferences, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void DisposePlaybackPreferencesHooks()
    {
        lock (_playbackPreferencesSync)
        {
            _playbackPreferencesSave?.Cancel();
            _playbackPreferencesSave?.Dispose();
            _playbackPreferencesSave = null;
        }
    }
}
