using Efiron.Application.Playback;
using Efiron.Playback;
using Microsoft.UI.Xaml;

namespace Efiron.Desktop.Views;

public sealed partial class LiveTvView
{
    private static readonly TimeSpan NativePlaybackDisposeDeadline =
        TimeSpan.FromSeconds(2);

    public async Task DisposePlaybackAsync(
        CancellationToken cancellationToken = default)
    {
        if (_playbackBackendControllerDisposed)
        {
            return;
        }

        _playbackBackendControllerDisposed = true;
        IPlaybackBackend? backend = null;
        var lockTaken = false;

        try
        {
            await _playbackBackendSwitchLock.WaitAsync(cancellationToken);
            lockTaken = true;

            if (_mpvSurface is not null)
            {
                _mpvSurface.Loaded -= MpvSurface_Loaded;
                _mpvSurface.SizeChanged -= MpvSurface_SizeChanged;
            }

            try
            {
                await _playbackDiagnosticsWriter.DetachAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
            }

            backend = DetachPlaybackBackendForShutdown();
        }
        finally
        {
            if (lockTaken)
            {
                _playbackBackendSwitchLock.Release();
            }
        }

        if (backend is not null)
        {
            var nativeDispose = Task.Run(() =>
            {
                try
                {
                    backend.Dispose();
                }
                catch (ObjectDisposedException)
                {
                }
            });
            await ObserveWithinDeadlineAsync(nativeDispose);
        }

        var diagnosticsDispose =
            _playbackDiagnosticsWriter.DisposeAsync().AsTask();
        await ObserveWithinDeadlineAsync(diagnosticsDispose);
    }

    private IPlaybackBackend? DetachPlaybackBackendForShutdown()
    {
        var backend = _playbackBackend;

        if (_playbackSession is not null)
        {
            _playbackSession.SnapshotChanged -= PlaybackSession_SnapshotChanged;
        }

        if (backend is MpvPlaybackBackend mpv)
        {
            mpv.DisplaySwapChainChanged -= MpvBackend_DisplaySwapChainChanged;
        }

        try
        {
            ClearMpvSwapChain();
        }
        catch (Exception)
        {
            _mpvAttachedSwapChain = 0;
        }

        if (_mpvSurface is not null)
        {
            _mpvSurface.Visibility = Visibility.Collapsed;
        }

        VideoView.MediaPlayer = null;
        VideoView.Visibility = Visibility.Collapsed;

        if (_windowsMediaSurface is not null)
        {
            _windowsMediaSurface.Source = null;
            _windowsMediaSurface.SetMediaPlayer(null!);
            _windowsMediaSurface.Visibility = Visibility.Collapsed;
        }

        _playbackBackend = null;
        _playbackSession = null;
        _pendingPlaybackRequest = null;
        _currentPlaybackRequest = null;
        _fullscreenFillBackend = null;
        _fullscreenFillApplied = null;
        return backend;
    }

    private static async Task ObserveWithinDeadlineAsync(Task task)
    {
        var completed = await Task.WhenAny(
            task,
            Task.Delay(NativePlaybackDisposeDeadline));
        if (!ReferenceEquals(completed, task))
        {
            return;
        }

        try
        {
            await task;
        }
        catch (Exception) when (
            task.IsCanceled || task.IsFaulted)
        {
            // Shutdown must not be held hostage by native or diagnostic teardown.
        }
    }
}
