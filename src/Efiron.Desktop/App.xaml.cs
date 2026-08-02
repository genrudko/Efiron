using System.Diagnostics;
using Efiron.Application.Appearance;
using Efiron.Application.Playback;
using Efiron.Desktop.Diagnostics;
using Efiron.Domain.Appearance;
using Efiron.Infrastructure.Appearance;
using Efiron.Infrastructure.Playback;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Efiron.Desktop;

public sealed partial class App : Microsoft.UI.Xaml.Application
{
    private static readonly long ProcessLifetimeStartTimestamp =
        Stopwatch.GetTimestamp();

    private Window? _window;

    public App()
    {
        StartupDiagnostics.ResetCrashEvidence();
        UnhandledException += App_UnhandledException;
        InitializeComponent();

        var localDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Efiron");
        PlaybackPreferencesStore = new JsonPlaybackPreferencesStore(
            Path.Combine(localDataDirectory, "playback.json"));
        AppearancePreferencesStore = new JsonAppearancePreferencesStore(
            Path.Combine(localDataDirectory, "appearance.json"));
    }

    internal static TimeSpan ProcessLifetimeElapsed =>
        Stopwatch.GetElapsedTime(ProcessLifetimeStartTimestamp);

    internal IPlaybackPreferencesStore PlaybackPreferencesStore { get; }

    internal IAppearancePreferencesStore AppearancePreferencesStore { get; }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var appearance = await LoadAppearancePreferencesAsync();
            _window = new MainWindow(AppearancePreferencesStore, appearance);
            ApplyInitialWindowSize(_window);
            _window.Activate();
        }
        catch (Exception exception)
        {
            StartupDiagnostics.RecordCrash("App.OnLaunched", exception);
            throw;
        }
    }

    private static void ApplyInitialWindowSize(Window window)
    {
        var displayArea = DisplayArea.GetFromWindowId(
            window.AppWindow.Id,
            DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var width = Math.Min(1440, Math.Max(960, workArea.Width - 80));
        var height = Math.Min(900, Math.Max(640, workArea.Height - 80));
        window.AppWindow.Resize(new SizeInt32(width, height));
    }

    private async Task<AppearancePreferences> LoadAppearancePreferencesAsync()
    {
        try
        {
            return await AppearancePreferencesStore.LoadAsync();
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return AppearancePreferences.Default;
        }
    }

    private static void App_UnhandledException(
        object sender,
        Microsoft.UI.Xaml.UnhandledExceptionEventArgs e) =>
        StartupDiagnostics.RecordCrash("Application.UnhandledException", e.Exception);
}
