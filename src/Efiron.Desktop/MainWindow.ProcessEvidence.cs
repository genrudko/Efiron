using System.Diagnostics;
using System.Text.Json;

namespace Efiron.Desktop;

public sealed partial class MainWindow
{
    private async Task RecordProcessStartupEvidenceAsync()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var recordedAtUtc = DateTimeOffset.UtcNow;
            var processLifetime = App.ProcessLifetimeElapsed;
            var startedAtUtc = recordedAtUtc - processLifetime;
            var evidence = new ProcessStartupEvidence(
                ProcessToShellMilliseconds: processLifetime.TotalMilliseconds,
                WorkingSetBytes: process.WorkingSet64,
                PrivateMemoryBytes: process.PrivateMemorySize64,
                ManagedHeapBytes: GC.GetTotalMemory(forceFullCollection: false),
                ThreadCount: process.Threads.Count,
                HandleCount: process.HandleCount,
                LiveViewCreated: IsLiveWorkspaceCreated,
                ProgrammeGuideCreated: IsProgrammeGuideWorkspaceCreated,
                ProcessStartedAtUtc: startedAtUtc,
                RecordedAtUtc: recordedAtUtc);

            var diagnosticsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Efiron",
                "diagnostics");
            Directory.CreateDirectory(diagnosticsDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(diagnosticsDirectory, "startup-process.json"),
                JsonSerializer.Serialize(evidence),
                _lifetime.Token);

            // Overwrite the legacy wall-clock measurement with the same
            // monotonic process-lifetime boundary used by the shell evidence.
            await File.WriteAllTextAsync(
                Path.Combine(diagnosticsDirectory, "first-useful-paint.json"),
                JsonSerializer.Serialize(new
                {
                    FirstUsefulPaintMilliseconds = processLifetime.TotalMilliseconds,
                    RecordedAtUtc = recordedAtUtc,
                    Clock = "Stopwatch",
                }),
                _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
        }
    }

    private sealed record ProcessStartupEvidence(
        double ProcessToShellMilliseconds,
        long WorkingSetBytes,
        long PrivateMemoryBytes,
        long ManagedHeapBytes,
        int ThreadCount,
        int HandleCount,
        bool LiveViewCreated,
        bool ProgrammeGuideCreated,
        DateTimeOffset ProcessStartedAtUtc,
        DateTimeOffset RecordedAtUtc);
}
