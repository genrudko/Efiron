namespace Efiron.Domain.Playlists;

public sealed record PlaylistParseWarning(
    int LineNumber,
    string Message);
