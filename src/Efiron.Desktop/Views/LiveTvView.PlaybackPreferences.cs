using Efiron.Application.Playback;
using Efiron.Domain.Playback;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Efiron.Desktop.Views;

public sealed partial class LiveTvView
{
    private readonly object _playbackPreferencesSync = new();
    private readonly SemaphoreSlim _playbackPreferencesSaveGate = new(1, 1);

    private PlaybackPreferences _playbackPreferences = PlaybackPreferences.Default;
    private PlaybackPreferences? _lastPersistedPlaybackPreferences;
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
        ChannelListView.ItemClick += PlaybackPreferences_ChannelListView_ItemClick;
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
            _lastPersistedPlaybackPreferences = _playbackPreferences;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _playbackPreferences = PlaybackPreferences.Default;
            _lastPersistedPlaybackPreferences = null;
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

    private void PlaybackPreferences_ChannelListView_ItemClick(
        object sender,
        ItemClickEventArgs e)
    {
        if (e.ClickedItem is not LiveChannelItem item ||
            !_playbackPreferencesLoaded)
        {
            return;
        }

        UpdatePlaybackPreferences(
            new PlaybackPreferences(
                item.Snapshot.Channel.StableId,
                _playbackPreferences.Volume,
                _playbackPreferences.IsMuted),
            saveImmediately: true);
    }

    private void HandlePlaybackPreferencesSnapshot(
        PlaybackSnapshot snapshot)
    {
        if (!_playbackPreferencesLoaded ||
            _playbackPreferencesApplyRunning ||
            snapshot.State is PlaybackState.Idle or
                PlaybackState.Disposed)
        {
            return;
        }

        UpdatePlaybackPreferences(
            new PlaybackPreferences(
                snapshot.ChannelStableId ??
                    _playbackPreferences.SelectedChannelStableId,
                snapshot.Volume,
                snapshot.IsMuted),
            saveImmediately: false);
    }

    private void UpdatePlaybackPreferences(
        PlaybackPreferences next,
        bool saveImmediately)
    {
        if (next == _playbackPreferences)
        {
            return;
        }

        _playbackPreferences = next;
        SchedulePlaybackPreferencesSave(next, saveImmediately);
    }

    private void SchedulePlaybackPreferencesSave(
        PlaybackPreferences preferences,
        bool saveImmediately)
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
            saveImmediately,
            cancellation.Token);
    }

    private async Task SavePlaybackPreferencesAsync(
        PlaybackPreferences preferences,
        bool saveImmediately,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!saveImmediately)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(350),
                    cancellationToken);
            }

            await SavePlaybackPreferencesCoreAsync(
                preferences,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task SavePlaybackPreferencesCoreAsync(
        PlaybackPreferences preferences,
        CancellationToken cancellationToken)
    {
        var store = PlaybackPreferencesStore;
        if (store is null)
        {
            return;
        }

        await _playbackPreferencesSaveGate.WaitAsync(cancellationToken);
        try
        {
            if (preferences == _lastPersistedPlaybackPreferences)
            {
                return;
            }

            await store.SaveAsync(preferences, cancellationToken);
            _lastPersistedPlaybackPreferences = preferences;
        }
        finally
        {
            _playbackPreferencesSaveGate.Release();
        }
    }

    public async Task FlushPlaybackPreferencesAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_playbackPreferencesLoaded)
        {
            return;
        }

        PlaybackPreferences latest;
        lock (_playbackPreferencesSync)
        {
            _playbackPreferencesSave?.Cancel();
            _playbackPreferencesSave?.Dispose();
            _playbackPreferencesSave = null;
            latest = _playbackPreferences;
        }

        try
        {
            await SavePlaybackPreferencesCoreAsync(latest, cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void FlushPlaybackPreferencesOnShutdown()
    {
        try
        {
            Task.Run(() => FlushPlaybackPreferencesAsync())
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void DisposePlaybackPreferencesHooks()
    {
        ChannelListView.ItemClick -= PlaybackPreferences_ChannelListView_ItemClick;
        FlushPlaybackPreferencesOnShutdown();

        lock (_playbackPreferencesSync)
        {
            _playbackPreferencesSave?.Cancel();
            _playbackPreferencesSave?.Dispose();
            _playbackPreferencesSave = null;
        }
    }
}
