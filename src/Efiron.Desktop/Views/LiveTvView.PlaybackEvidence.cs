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
    private readonly SemaphoreSlim _playbackTraceGate = new(1, 1);

    private long _visibilityCallbackToken;
    private bool _playbackEvidenceWritten;
    private bool _playbackEvidenceHooksAttached;
    private bool _visibleActivationQueued;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        EnsurePlaybackEvidenceHooks();

        _ = TracePlaybackStageAsync(
            $"template-applied visibility={Visibility}",
            snapshot: null);

        QueueVisibleActivation("template-visible");
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsurePlaybackEvidenceHooks();
        var measured = base.MeasureOverride(availableSize);

        _ = TracePlaybackStageAsync(
            $"measure visibility={Visibility} available={availableSize.Width}x{availableSize.Height}",
            _playbackSession?.Snapshot);
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
        QueueVisibleActivation("loaded-visible");
    }

    private async void PlaybackEvidence_VideoViewInitialized(
        object? sender,
        InitializedEventArgs e)
    {
        await TracePlaybackStageAsync(
            $"video-view-initialized visibility={Visibility}",
            snapshot: null);
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
        _ = TracePlaybackStageAsync(
            "snapshot",
            e.Snapshot);

        if (e.Snapshot.State == PlaybackState.Playing &&
            !_playbackEvidenceWritten)
        {
            _playbackEvidenceWritten = true;
            _ = RecordPlaybackEvidenceAsync(e.Snapshot);
        }
    }

    private async Task TracePlaybackStageAsync(
        string stage,
        PlaybackSnapshot? snapshot)
    {
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
}
