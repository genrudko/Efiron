using Microsoft.UI.Xaml;

namespace Efiron.App;

public sealed partial class MainWindow
{
    private bool _channelLibraryInitializationScheduled;

    internal void ScheduleChannelLibraryWorkspaceInitialization()
    {
        if (_channelLibraryInitializationScheduled || _channelLibraryInitialized)
        {
            return;
        }

        _channelLibraryInitializationScheduled = true;
        RootNavigation.Loaded += ChannelLibraryRootNavigation_Loaded;
        Closed += ChannelLibraryStartupWindow_Closed;
    }

    private void ChannelLibraryRootNavigation_Loaded(object sender, RoutedEventArgs e)
    {
        RootNavigation.Loaded -= ChannelLibraryRootNavigation_Loaded;
        Closed -= ChannelLibraryStartupWindow_Closed;
        _channelLibraryInitializationScheduled = false;
        InitializeChannelLibraryWorkspace();
    }

    private void ChannelLibraryStartupWindow_Closed(object sender, WindowEventArgs args)
    {
        RootNavigation.Loaded -= ChannelLibraryRootNavigation_Loaded;
        Closed -= ChannelLibraryStartupWindow_Closed;
        _channelLibraryInitializationScheduled = false;
    }
}
