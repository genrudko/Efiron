using Efiron.Domain.Playback;

namespace Efiron.Application.Playback;

public interface IPlaybackPreferencesStore
{
    ValueTask<PlaybackPreferences> LoadAsync(
        CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        PlaybackPreferences preferences,
        CancellationToken cancellationToken = default);
}
