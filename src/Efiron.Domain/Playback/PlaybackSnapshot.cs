namespace Efiron.Domain.Playback;

public sealed record PlaybackSnapshot(
    PlaybackState State,
    Uri? Source,
    string? ChannelStableId,
    string? DisplayName,
    int Volume,
    bool IsMuted,
    string? ErrorMessage)
{
    public static PlaybackSnapshot Idle { get; } = new(
        PlaybackState.Idle,
        Source: null,
        ChannelStableId: null,
        DisplayName: null,
        Volume: 100,
        IsMuted: false,
        ErrorMessage: null);

    public bool HasMedia => Source is not null;

    public bool IsActive =>
        State is PlaybackState.Opening or PlaybackState.Playing or PlaybackState.Paused;
}
