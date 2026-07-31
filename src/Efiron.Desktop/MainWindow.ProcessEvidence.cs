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
            var startedAtUtc = new DateTimeOffset(
                process.StartTime.ToUniversalTime(),
                TimeSpan.Zero);
            var recordedAtUtc = DateTimeOffset.UtcNow;
            var evidence = new ProcessStartupEvidence(
                ProcessToShellMilliseconds:
                    (recordedAtUtc - startedAtUtc).TotalMilliseconds,
                WorkingSetBytes: process.WorkingSet64,
                PrivateMemoryBytes: process.PrivateMemorySize64,
                ManagedHeapBytes: GC.GetTotalMemory(forceFullCollection: false),
                ThreadCount: process.Threads.Count,
                HandleCount: process.HandleCount,
                LiveViewCreated: IsLiveWorkspaceCreated,
                ProgrammeGuideCreated: IsProgrammeGuideWorkspaceCreated,
                ProcessStartedAtUtc: startedAtUtc,
                RecordedAtUtc: recordedAtUtc);

            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Efiron",
                "diagnostics",
                "startup-process.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(evidence),
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