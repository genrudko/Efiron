using System.Diagnostics;
using System.Text.Json;
using Efiron.Application.Playback;
using Efiron.Domain.Playback;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Efiron.Desktop.Views;

public sealed partial class LiveTvView
{
    private const string LayoutActivityVerificationEnvironmentVariable =
        "EFIRON_CI_LAYOUT_ACTIVITY_VERIFICATION";

    private long _layoutUpdatedCount;
    private long _layoutActivityBaseline;
    private DateTimeOffset _layoutActivityStartedAtUtc;
    private TimeSpan _layoutActivityCpuBaseline;
    private DispatcherQueueTimer? _layoutActivityTimer;
    private bool _layoutActivityEnabled;
    private bool _layoutActivityStarted;

    private void EnableLayoutActivityEvidence()
    {
        if (_layoutActivityEnabled ||
            !string.Equals(
                Environment.GetEnvironmentVariable(
                    LayoutActivityVerificationEnvironmentVariable),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        _layoutActivityEnabled = true;
        LiveRoot.LayoutUpdated += LayoutActivity_LiveRootLayoutUpdated;
        PlaybackSnapshotChanged += LayoutActivity_PlaybackSnapshotChanged;
        Unloaded += LayoutActivity_Unloaded;
    }

    private void LayoutActivity_LiveRootLayoutUpdated(object? sender, object e) =>
        Interlocked.Increment(ref _layoutUpdatedCount);

    private void LayoutActivity_PlaybackSnapshotChanged(
        object? sender,
        PlaybackSnapshotChangedEventArgs e)
    {
        if (_layoutActivityStarted || e.Snapshot.State != PlaybackState.Playing)
        {
            return;
        }

        _layoutActivityStarted = true;
        _layoutActivityBaseline = Volatile.Read(ref _layoutUpdatedCount);
        _layoutActivityStartedAtUtc = DateTimeOffset.UtcNow;
        using (var process = Process.GetCurrentProcess())
        {
            process.Refresh();
            _layoutActivityCpuBaseline = process.TotalProcessorTime;
        }

        _layoutActivityTimer = DispatcherQueue.CreateTimer();
        _layoutActivityTimer.Interval = TimeSpan.FromSeconds(5);
        _layoutActivityTimer.IsRepeating = false;
        _layoutActivityTimer.Tick += LayoutActivityTimer_Tick;
        _layoutActivityTimer.Start();
    }

    private async void LayoutActivityTimer_Tick(
        DispatcherQueueTimer sender,
        object args)
    {
        sender.Stop();
        sender.Tick -= LayoutActivityTimer_Tick;
        _layoutActivityTimer = null;

        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var recordedAtUtc = DateTimeOffset.UtcNow;
            var evidence = new LayoutActivityEvidence(
                DurationMilliseconds:
                    (recordedAtUtc - _layoutActivityStartedAtUtc).TotalMilliseconds,
                LayoutUpdatedCount:
                    Volatile.Read(ref _layoutUpdatedCount) - _layoutActivityBaseline,
                ProcessCpuMilliseconds:
                    (process.TotalProcessorTime - _layoutActivityCpuBaseline).TotalMilliseconds,
                WorkingSetBytes: process.WorkingSet64,
                PrivateMemoryBytes: process.PrivateMemorySize64,
                PlayerWidth: PlayerSurfaceBorder.ActualWidth,
                PlayerHeight: PlayerSurfaceBorder.ActualHeight,
                RecordedAtUtc: recordedAtUtc);

            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Efiron",
                "diagnostics",
                "layout-activity.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(evidence));
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidOperationException)
        {
        }
        finally
        {
            DisableLayoutActivityEvidence();
        }
    }

    private void LayoutActivity_Unloaded(object sender, RoutedEventArgs e) =>
        DisableLayoutActivityEvidence();

    private void DisableLayoutActivityEvidence()
    {
        if (!_layoutActivityEnabled)
        {
            return;
        }

        _layoutActivityTimer?.Stop();
        if (_layoutActivityTimer is not null)
        {
            _layoutActivityTimer.Tick -= LayoutActivityTimer_Tick;
            _layoutActivityTimer = null;
        }

        LiveRoot.LayoutUpdated -= LayoutActivity_LiveRootLayoutUpdated;
        PlaybackSnapshotChanged -= LayoutActivity_PlaybackSnapshotChanged;
        Unloaded -= LayoutActivity_Unloaded;
        _layoutActivityEnabled = false;
    }

    private sealed record LayoutActivityEvidence(
        double DurationMilliseconds,
        long LayoutUpdatedCount,
        double ProcessCpuMilliseconds,
        long WorkingSetBytes,
        long PrivateMemoryBytes,
        double PlayerWidth,
        double PlayerHeight,
        DateTimeOffset RecordedAtUtc);
}
