using Efiron.Application.Playback;
using Efiron.Desktop.Diagnostics;
using Efiron.Infrastructure.Playback;
using Microsoft.UI.Xaml;

namespace Efiron.Desktop;

public sealed partial class App : Microsoft.UI.Xaml.Application
{
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
    }

    internal IPlaybackPreferencesStore PlaybackPreferencesStore { get; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception exception)
        {
            StartupDiagnostics.RecordCrash("App.OnLaunched", exception);
            throw;
        }
    }

    private static void App_UnhandledException(
        object sender,
        Microsoft.UI.Xaml.UnhandledExceptionEventArgs e) =>
        StartupDiagnostics.RecordCrash("Application.UnhandledException", e.Exception);
}
