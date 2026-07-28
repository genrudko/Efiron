using Efiron.App.Localization;
using Efiron.App.Startup;
using Microsoft.UI.Xaml;
using Microsoft.Windows.Globalization;

namespace Efiron.App;

public partial class App : Application
{
    private const string StartupErrorFileName = "efiron-startup-error.log";

    private Window? _window;

    public App()
    {
        StartupTimeline.Mark("application.constructor.start");
        UnhandledException += App_UnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        var configuredLanguage = AppLanguageStore.Load();
        if (configuredLanguage is not null)
        {
            ApplicationLanguages.PrimaryLanguageOverride = configuredLanguage;
        }

        InitializeComponent();
        StartupTimeline.Mark("application.constructor.complete");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        StartupTimeline.Mark("application.launch.start");
        TryDeleteStartupErrorFile();
        try
        {
            var mainWindow = new MainWindow();
            StartupTimeline.Mark("window.constructed");
            _window = mainWindow;
            _window.Activate();
            StartupTimeline.Mark("window.activated");
            mainWindow.BeginDeferredPresentationInitialization();
        }
        catch (Exception exception)
        {
            StartupTimeline.Mark("application.launch.failed");
            TryWriteStartupError(exception);
            throw;
        }
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        StartupTimeline.Mark("application.unhandled-exception");
        TryWriteStartupError(e.Exception);
    }

    private static void CurrentDomain_UnhandledException(
        object sender,
        System.UnhandledExceptionEventArgs e)
    {
        StartupTimeline.Mark("appdomain.unhandled-exception");
        if (e.ExceptionObject is Exception exception)
        {
            TryWriteStartupError(exception);
        }
    }

    private static string StartupErrorFilePath =>
        Path.Combine(AppContext.BaseDirectory, StartupErrorFileName);

    private static void TryDeleteStartupErrorFile()
    {
        try
        {
            File.Delete(StartupErrorFilePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryWriteStartupError(Exception exception)
    {
        try
        {
            File.WriteAllText(StartupErrorFilePath, exception.ToString());
        }
        catch (Exception writeException) when (
            writeException is IOException or UnauthorizedAccessException)
        {
        }
    }
}
