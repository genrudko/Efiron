namespace Efiron.Domain.Playback;

public enum PlaybackState
{
    Idle = 0,
    Opening = 1,
    Playing = 2,
    Paused = 3,
    Stopped = 4,
    Ended = 5,
    Failed = 6,
    Disposed = 7,
}
