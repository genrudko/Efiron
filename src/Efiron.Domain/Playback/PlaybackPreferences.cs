namespace Efiron.Domain.Playback;

public sealed record PlaybackPreferences
{
    public PlaybackPreferences(
        string? selectedChannelStableId,
        int volume,
        bool isMuted)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(volume, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(volume, 100);

        SelectedChannelStableId = string.IsNullOrWhiteSpace(selectedChannelStableId)
            ? null
            : selectedChannelStableId.Trim();
        Volume = volume;
        IsMuted = isMuted;
    }

    public static PlaybackPreferences Default { get; } = new(
        selectedChannelStableId: null,
        volume: 100,
        isMuted: false);

    public string? SelectedChannelStableId { get; }

    public int Volume { get; }

    public bool IsMuted { get; }
}
