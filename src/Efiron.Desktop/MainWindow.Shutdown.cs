using Microsoft.UI.Windowing;

namespace Efiron.Desktop;

public sealed partial class MainWindow
{
    private static readonly TimeSpan ShutdownCleanupDeadline =
        TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ForcedProcessExitDelay =
        TimeSpan.FromSeconds(1);

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
        _lifetime.Cancel();
        ReleaseWorkspaceEventHandlers();

        var cleanupTask = _liveTvWorkspace is null
            ? Task.CompletedTask
            : _liveTvWorkspace.DisposePlaybackAsync();
        var completed = await Task.WhenAny(
            cleanupTask,
            Task.Delay(ShutdownCleanupDeadline));

        if (ReferenceEquals(completed, cleanupTask))
        {
            try
            {
                await cleanupTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        _gracefulShutdownCompleted = true;
        AppWindow.Closing -= MainWindow_AppWindowClosing;

        // A native decoder or graphics driver can keep background threads alive
        // after the WinUI window is closed. The fail-safe is deliberately armed
        // before Close so the X button always terminates this single-user client.
        _ = ForceProcessExitAfterDelayAsync();
        Close();
    }

    private static async Task ForceProcessExitAfterDelayAsync()
    {
        await Task.Delay(ForcedProcessExitDelay).ConfigureAwait(false);
        Environment.Exit(0);
    }
}
