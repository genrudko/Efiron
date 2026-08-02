using Efiron.Domain.Playback;

namespace Efiron.Application.Playback;

public interface IPlaybackSession : IDisposable
{
    event EventHandler<PlaybackSnapshotChangedEventArgs>? SnapshotChanged;

    PlaybackSnapshot Snapshot { get; }

    ValueTask PlayAsync(
        PlaybackRequest request,
        CancellationToken cancellationToken = default);

    void Pause();

    void Resume();

    void Stop();

    void SetMuted(bool isMuted);

    void SetVolume(int volume);
}
