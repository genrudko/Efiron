using Microsoft.UI.Windowing;

namespace Efiron.Desktop;

public sealed partial class MainWindow
{
    private bool _gracefulShutdownEnabled;
    private bool _gracefulShutdownStarted;
    private bool _gracefulShutdownCompleted;

    private void EnableGracefulShutdown()
    {
        if (_gracefulShutdownEnabled)
        {
            return;
        }

        _gracefulShutdownEnabled = true;
        AppWindow.Closing += MainWindow_AppWindowClosing;
    }

    private async void MainWindow_AppWindowClosing(
        AppWindow sender,
        AppWindowClosingEventArgs args)
    {
        if (_gracefulShutdownCompleted)
        {
            return;
        }

        args.Cancel = true;
        if (_gracefulShutdownStarted)
        {
            return;
        }

        _gracefulShutdownStarted = true;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            await LiveTvWorkspace.DisposePlaybackAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            _gracefulShutdownCompleted = true;
            AppWindow.Closing -= MainWindow_AppWindowClosing;
            Close();
        }
    }
}