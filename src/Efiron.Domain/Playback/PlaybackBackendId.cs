namespace Efiron.Domain.Playback;

public enum PlaybackBackendId
{
    Auto,
    LibVlc,
    WindowsMedia,
}

public enum LibVlcPlaybackProfile
{
    Auto,
    D3D11Va,
    Dxva2,
    Software,
}
