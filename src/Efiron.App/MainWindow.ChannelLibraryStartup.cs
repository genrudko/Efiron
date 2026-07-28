using Efiron.App.Startup;
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
        Closed += ChannelLibraryStartupWindow_Closed;

        if (RootNavigation.IsLoaded)
        {
            DispatcherQueue.TryEnqueue(InitializeScheduledChannelLibraryWorkspace);
            return;
        }

        RootNavigation.Loaded += ChannelLibraryRootNavigation_Loaded;
    }

    private void ChannelLibraryRootNavigation_Loaded(object sender, RoutedEventArgs e)
    {
        RootNavigation.Loaded -= ChannelLibraryRootNavigation_Loaded;
        InitializeScheduledChannelLibraryWorkspace();
    }

    private void InitializeScheduledChannelLibraryWorkspace()
    {
        if (!_channelLibraryInitializationScheduled || _channelLibraryInitialized)
        {
            return;
        }

        Closed -= ChannelLibraryStartupWindow_Closed;
        _channelLibraryInitializationScheduled = false;
        InitializeChannelLibraryWorkspace();
        StartupTimeline.Mark("catalog.ready");
    }

    private void ChannelLibraryStartupWindow_Closed(object sender, WindowEventArgs args)
    {
        RootNavigation.Loaded -= ChannelLibraryRootNavigation_Loaded;
        Closed -= ChannelLibraryStartupWindow_Closed;
        _channelLibraryInitializationScheduled = false;
    }
}
