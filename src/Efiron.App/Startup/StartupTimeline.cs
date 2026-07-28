using System.Diagnostics;
using System.Globalization;

namespace Efiron.App.Startup;

internal static class StartupTimeline
{
    private static readonly object Sync = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly string LogPath = CreateLogPath();

    static StartupTimeline()
    {
        WriteHeader();
        Mark("process.start");
    }

    internal static string FilePath => LogPath;

    internal static void Mark(string milestone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(milestone);
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{Clock.Elapsed.TotalMilliseconds,10:0.0} ms  {milestone}{Environment.NewLine}");

        lock (Sync)
        {
            try
            {
                File.AppendAllText(LogPath, line);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
            }
        }
    }

    private static string CreateLogPath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Efiron");
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            directory = AppContext.BaseDirectory;
        }

        return Path.Combine(directory, "startup-timing.log");
    }

    private static void WriteHeader()
    {
        lock (Sync)
        {
            try
            {
                File.WriteAllText(
                    LogPath,
                    $"Efiron startup trace {DateTimeOffset.Now:O}{Environment.NewLine}");
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
            }
        }
    }
}
