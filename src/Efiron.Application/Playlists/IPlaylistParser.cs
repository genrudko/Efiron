using Efiron.Domain.Playlists;

namespace Efiron.Application.Playlists;

public interface IPlaylistParser
{
    PlaylistDocument Parse(
        string content,
        Uri? sourceUri = null);
}
