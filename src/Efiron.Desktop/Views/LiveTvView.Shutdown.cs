namespace Efiron.Desktop.Views;

public sealed partial class LiveTvView
{
    public async Task DisposePlaybackAsync(
        CancellationToken cancellationToken = default)
    {
        if (_playbackBackendControllerDisposed)
        {
            return;
        }

        _playbackBackendControllerDisposed = true;
        await _playbackBackendSwitchLock.WaitAsync(cancellationToken);
        try
        {
            if (_mpvSurface is not null)
            {
                _mpvSurface.SizeChanged -= MpvSurface_SizeChanged;
            }

            try
            {
                await _playbackDiagnosticsWriter.DetachAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }

            ReleaseCurrentPlaybackBackend();

            try
            {
                await _playbackDiagnosticsWriter.DisposeAsync();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
        finally
        {
            _playbackBackendSwitchLock.Release();
            _playbackBackendSwitchLock.Dispose();
        }
    }
}