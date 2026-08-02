using Efiron.Domain.Playback;

namespace Efiron.Application.Playback;

public sealed class PlaybackSnapshotChangedEventArgs(
    PlaybackSnapshot snapshot) : EventArgs
{
    public PlaybackSnapshot Snapshot { get; } =
        snapshot ?? throw new ArgumentNullException(nameof(snapshot));
}
