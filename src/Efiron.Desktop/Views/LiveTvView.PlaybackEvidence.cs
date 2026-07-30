using System.Text;
using System.Text.Json;
using Efiron.Application.Playback;
using Efiron.Domain.Playback;
using LibVLCSharp.Platforms.Windows;
using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace Efiron.Desktop.Views;

public sealed partial class LiveTvView
{
    private const string PlaybackSequenceEnvironmentVariable =
        "EFIRON_CI_PLAYBACK_SEQUENCE";

    private readonly SemaphoreSlim _playbackTraceGate = new(1, 1);

    private long _visibilityCallbackToken;
    private bool _playbackEvidenceWritten;
    private bool _playbackEvidenceHooksAttached;
    private bool _visibleActivationQueued;
    private bool _controlSequenceStarted;

    private static bool PlaybackControlSequenceEnabled =>
        string.Equals(
            Environment.GetEnvironmentVariable(
                PlaybackSequenceEnvironmentVariable),
            "1",
            StringComparison.Ordinal);

    private static bool PlaybackDiagnosticsEnabled =>
        PlaybackControlSequenceEnabled ||
        string.Equals(
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        EnsurePlaybackEvidenceHooks();

        _ = TracePlaybackStageAsync(
            $"template-applied visibility={Visibility}",
            snapshot: null);

        _ = ApplyPlaybackPreferencesAsync();
        QueueVisibleActivation("template-visible");
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsurePlaybackEvidenceHooks();
        var measured = base.MeasureOverride(availableSize);

        _ = TracePlaybackStageAsync(
            $"measure visibility={Visibility} available={availableSize.Width}x{availableSize.Height}",
            _playbackSession?.Snapshot);
        _ = ApplyPlaybackPreferencesAsync();
        QueueVisibleActivation("measure-visible");

        return measured;
    }

    private void EnsurePlaybackEvidenceHooks()
    {
        if (_playbackEvidenceHooksAttached)
        {
            return;
        }

        _playbackEvidenceHooksAttached = true;
        EnsurePlaybackPreferencesHooks();
        PlaybackSnapshotChanged += LiveTvView_PlaybackSnapshotChanged;
        VideoView.Initialized += PlaybackEvidence_VideoViewInitialized;
        Loaded += LiveTvView_Loaded;
        _visibilityCallbackToken = RegisterPropertyChangedCallback(
            VisibilityProperty,
            LiveTvView_VisibilityChanged);
        Unloaded += LiveTvView_Unloaded;
    }

    private void QueueVisibleActivation(string trigger)
    {
        if (Visibility != Visibility.Visible || _visibleActivationQueued)
        {
            return;
        }

        _visibleActivationQueued = true;
        if (!DispatcherQueue.TryEnqueue(async () =>
            {
                await ApplyPlaybackPreferencesAsync();
                await ActivateAndTraceAsync(trigger);
            }))
        {
            _visibleActivationQueued = false;
        }
    }

    private void LiveTvView_Loaded(object sender, RoutedEventArgs e)
    {
        _ = TracePlaybackStageAsync(
            $"loaded visibility={Visibility}",
            snapshot: null);
        _ = ApplyPlaybackPreferencesAsync();
        QueueVisibleActivation("loaded-visible");
    }

    private async void PlaybackEvidence_VideoViewInitialized(
        object? sender,
        InitializedEventArgs e)
    {
        ApplyPlaybackPreferencesToSession();
        await ApplyPlaybackPreferencesAsync();
        await TracePlaybackStageAsync(
            $"video-view-initialized visibility={Visibility}",
            _playbackSession?.Snapshot);
        QueueVisibleActivation("video-view-initialized");
    }

    private void LiveTvView_VisibilityChanged(
        DependencyObject sender,
        DependencyProperty property)
    {
        _ = TracePlaybackStageAsync(
            $"visibility-changed visibility={Visibility}",
            snapshot: null);

        if (Visibility != Visibility.Visible)
        {
            _visibleActivationQueued = false;
            return;
        }

        _ = ApplyPlaybackPreferencesAsync();
        QueueVisibleActivation("visibility-visible");
    }

    private async Task ActivateAndTraceAsync(string trigger)
    {
        await TracePlaybackStageAsync(
            $"activate-start trigger={trigger}",
            _playbackSession?.Snapshot);

        try
        {
            await ActivateAsync();
            await TracePlaybackStageAsync(
                $"activate-complete trigger={trigger}",
                _playbackSession?.Snapshot);
        }
        catch (Exception exception)
        {
            _visibleActivationQueued = false;
            await TracePlaybackStageAsync(
                $"activate-failed trigger={trigger} type={exception.GetType().FullName} message={exception.Message}",
                _playbackSession?.Snapshot);
        }
    }

    private void LiveTvView_PlaybackSnapshotChanged(
        object? sender,
        PlaybackSnapshotChangedEventArgs e)
    {
        HandlePlaybackPreferencesSnapshot(e.Snapshot);
        _ = TracePlaybackStageAsync(
            "snapshot",
            e.Snapshot);

        if (e.Snapshot.State != PlaybackState.Playing)
        {
            return;
        }

        if (!_playbackEvidenceWritten)
        {
            _playbackEvidenceWritten = true;
            _ = RecordPlaybackEvidenceAsync(e.Snapshot);
        }

        if (PlaybackControlSequenceEnabled && !_controlSequenceStarted)
        {
            _controlSequenceStarted = true;
            DispatcherQueue.TryEnqueue(async () =>
            {
                await RunPlaybackControlSequenceAsync(e.Snapshot);
            });
        }
    }

    private async Task RunPlaybackControlSequenceAsync(
        PlaybackSnapshot initialPlayingSnapshot)
    {
        var firstChannelStableId = initialPlayingSnapshot.ChannelStableId;
        var secondChannelStableId = string.Empty;
        var paused = false;
        var resumed = false;
        var volumeSet = false;
        var muted = false;
        var unmuted = false;
        var stopped = false;
        var switched = false;
        string? error = null;

        try
        {
            if (_playbackSession is null ||
                string.IsNullOrWhiteSpace(firstChannelStableId))
            {
                throw new InvalidOperationException(
                    "The playback session or first channel identity is unavailable.");
            }

            await TracePlaybackStageAsync(
                "control-sequence-start",
                _playbackSession.Snapshot);

            _playbackSession.Pause();
            await WaitForSnapshotAsync(
                snapshot => snapshot.State == PlaybackState.Paused &&
                    string.Equals(
                        snapshot.ChannelStableId,
                        firstChannelStableId,
                        StringComparison.Ordinal),
                TimeSpan.FromSeconds(10));
            paused = true;

            _playbackSession.Resume();
            await WaitForSnapshotAsync(
                snapshot => snapshot.State == PlaybackState.Playing &&
                    string.Equals(
                        snapshot.ChannelStableId,
                        firstChannelStableId,
                        StringComparison.Ordinal),
                TimeSpan.FromSeconds(10));
            resumed = true;

            _playbackSession.SetVolume(37);
            await WaitForSnapshotAsync(
                static snapshot => snapshot.Volume == 37,
                TimeSpan.FromSeconds(5));
            volumeSet = true;

            _playbackSession.SetMuted(true);
            await WaitForSnapshotAsync(
                static snapshot => snapshot.IsMuted,
                TimeSpan.FromSeconds(5));
            muted = true;

            _playbackSession.SetMuted(false);
            await WaitForSnapshotAsync(
                static snapshot => !snapshot.IsMuted && snapshot.Volume == 37,
                TimeSpan.FromSeconds(5));
            unmuted = true;

            _playbackSession.Stop();
            await WaitForSnapshotAsync(
                static snapshot => snapshot.State == PlaybackState.Stopped,
                TimeSpan.FromSeconds(5));
            stopped = true;

            var secondItem = _allItems.FirstOrDefault(item =>
                !string.Equals(
                    item.Snapshot.Channel.StableId,
                    firstChannelStableId,
                    StringComparison.Ordinal));
            if (secondItem is null)
            {
                throw new InvalidOperationException(
                    "A second channel is required for the switch validation.");
            }

            secondChannelStableId = secondItem.Snapshot.Channel.StableId;
            await SelectChannelAsync(secondItem);
            await WaitForSnapshotAsync(
                snapshot => snapshot.State == PlaybackState.Playing &&
                    string.Equals(
                        snapshot.ChannelStableId,
                        secondChannelStableId,
                        StringComparison.Ordinal),
                TimeSpan.FromSeconds(15));
            switched = true;

            await TracePlaybackStageAsync(
                "control-sequence-complete",
                _playbackSession.Snapshot);
        }
        catch (Exception exception)
        {
            error = $"{exception.GetType().FullName}: {exception.Message}";
            await TracePlaybackStageAsync(
                $"control-sequence-failed error={error}",
                _playbackSession?.Snapshot);
        }

        var evidence = new PlaybackControlEvidence(
            FirstChannelStableId: firstChannelStableId,
            SecondChannelStableId: secondChannelStableId,
            Paused: paused,
            Resumed: resumed,
            VolumeSetTo37: volumeSet,
            Muted: muted,
            Unmuted: unmuted,
            Stopped: stopped,
            SwitchedToSecondChannel: switched,
            Error: error,
            RecordedAtUtc: DateTimeOffset.UtcNow);
        await RecordPlaybackControlEvidenceAsync(evidence);
    }

    private async Task<PlaybackSnapshot> WaitForSnapshotAsync(
        Func<PlaybackSnapshot, bool> predicate,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var completion = new TaskCompletionSource<PlaybackSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(
            object? sender,
            PlaybackSnapshotChangedEventArgs eventArgs)
        {
            if (predicate(eventArgs.Snapshot))
            {
                completion.TrySetResult(eventArgs.Snapshot);
            }
        }

        PlaybackSnapshotChanged += Handler;
        try
        {
            var current = _playbackSession?.Snapshot;
            if (current is not null && predicate(current))
            {
                completion.TrySetResult(current);
            }

            return await completion.Task.WaitAsync(timeout);
        }
        finally
        {
            PlaybackSnapshotChanged -= Handler;
        }
    }

    private async Task TracePlaybackStageAsync(
        string stage,
        PlaybackSnapshot? snapshot)
    {
        if (!PlaybackDiagnosticsEnabled)
        {
            return;
        }

        try
        {
            var path = GetDiagnosticsPath("startup-crash.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var line = new StringBuilder()
                .Append(DateTimeOffset.UtcNow.ToString("O"))
                .Append(" | ")
                .Append(stage);

            if (snapshot is not null)
            {
                line
                    .Append(" | state=")
                    .Append(snapshot.State)
                    .Append(" source=")
                    .Append(snapshot.Source?.AbsoluteUri ?? "<none>")
                    .Append(" channel=")
                    .Append(snapshot.ChannelStableId ?? "<none>")
                    .Append(" volume=")
                    .Append(snapshot.Volume)
                    .Append(" muted=")
                    .Append(snapshot.IsMuted)
                    .Append(" error=")
                    .Append(snapshot.ErrorMessage ?? "<none>");
            }

            line.AppendLine();
            await _playbackTraceGate.WaitAsync();
            try
            {
                await File.AppendAllTextAsync(path, line.ToString());
            }
            finally
            {
                _playbackTraceGate.Release();
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task RecordPlaybackEvidenceAsync(PlaybackSnapshot snapshot)
    {
        try
        {
            var path = GetDiagnosticsPath("playback-playing.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var evidence = new PlaybackEvidence(
                snapshot.State.ToString(),
                snapshot.Source?.AbsoluteUri,
                snapshot.ChannelStableId,
                snapshot.DisplayName,
                snapshot.Volume,
                snapshot.IsMuted,
                DateTimeOffset.UtcNow);
            await File.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(evidence));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task RecordPlaybackControlEvidenceAsync(
        PlaybackControlEvidence evidence)
    {
        try
        {
            var path = GetDiagnosticsPath("playback-controls.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(evidence));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string GetDiagnosticsPath(string fileName) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Efiron",
            "diagnostics",
            fileName);

    private void LiveTvView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_visibilityCallbackToken != 0)
        {
            UnregisterPropertyChangedCallback(
                VisibilityProperty,
                _visibilityCallbackToken);
            _visibilityCallbackToken = 0;
        }

        PlaybackSnapshotChanged -= LiveTvView_PlaybackSnapshotChanged;
        VideoView.Initialized -= PlaybackEvidence_VideoViewInitialized;
        Loaded -= LiveTvView_Loaded;
        Unloaded -= LiveTvView_Unloaded;
        DisposePlaybackPreferencesHooks();
        _playbackEvidenceHooksAttached = false;
        _visibleActivationQueued = false;
    }

    private sealed record PlaybackEvidence(
        string State,
        string? Source,
        string? ChannelStableId,
        string? DisplayName,
        int Volume,
        bool IsMuted,
        DateTimeOffset RecordedAtUtc);

    private sealed record PlaybackControlEvidence(
        string? FirstChannelStableId,
        string SecondChannelStableId,
        bool Paused,
        bool Resumed,
        bool VolumeSetTo37,
        bool Muted,
        bool Unmuted,
        bool Stopped,
        bool SwitchedToSecondChannel,
        string? Error,
        DateTimeOffset RecordedAtUtc);
}
