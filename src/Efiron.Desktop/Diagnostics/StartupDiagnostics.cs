using System.Text;

namespace Efiron.Desktop.Diagnostics;

internal static class StartupDiagnostics
{
    private static readonly string DiagnosticsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Efiron",
        "diagnostics");

    internal static string CrashPath { get; } = Path.Combine(
        DiagnosticsDirectory,
        "startup-crash.log");

    internal static void ResetCrashEvidence()
    {
        try
        {
            if (File.Exists(CrashPath))
            {
                File.Delete(CrashPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    internal static void RecordCrash(string stage, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(DiagnosticsDirectory);
            var text = new StringBuilder()
                .AppendLine($"Stage: {stage}")
                .AppendLine($"UTC: {DateTimeOffset.UtcNow:O}")
                .AppendLine($"Exception: {exception.GetType().FullName}")
                .AppendLine($"HRESULT: 0x{exception.HResult:X8}")
                .AppendLine(exception.ToString())
                .ToString();
            File.WriteAllText(CrashPath, text);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
