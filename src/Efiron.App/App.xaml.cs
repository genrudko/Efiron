using Efiron.App.Localization;
using Microsoft.UI.Xaml;
using Microsoft.Windows.Globalization;

namespace Efiron.App;

public partial class App : Application
{
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
        var mainWindow = new MainWindow();
        mainWindow.InitializeLiveProgrammeWorkspace();
        mainWindow.InitializeGuideTimelineWorkspace();
        mainWindow.InitializeGuideTimelineEmptyStateTracking();
        mainWindow.InitializeGuideTimelineRefinements();
        _window = mainWindow;
        _window.Activate();
    }
}
