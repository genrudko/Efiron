using Efiron.App.Localization;
using Microsoft.UI.Xaml;
using Microsoft.Windows.Globalization;

namespace Efiron.App;

public partial class App : Application
{
    private const string StartupErrorFileName = "efiron-startup-error.log";

    private Window? _window;

    public App()
    {
        var configuredLanguage = AppLanguageStore.Load();
        if (configuredLanguage is not null)
        {
            ApplicationLanguages.PrimaryLanguageOverride = configuredLanguage;
        }

        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        TryDeleteStartupErrorFile();
        try
        {
            var mainWindow = new MainWindow();
            mainWindow.InitializeLiveProgrammeWorkspace();
            mainWindow.InitializeGuideTimelineWorkspace();
            mainWindow.InitializeGuideTimelineEmptyStateTracking();
            mainWindow.InitializeGuideTimelineRefinements();
            mainWindow.InitializeChannelLibraryWorkspace();
            _window = mainWindow;
            _window.Activate();
        }
        catch (Exception exception)
        {
            TryWriteStartupError(exception);
            throw;
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
