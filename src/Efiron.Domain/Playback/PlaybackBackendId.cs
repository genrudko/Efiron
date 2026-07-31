namespace Efiron.Domain.Playback;

public enum PlaybackBackendId
{
    Auto,
    LibVlc,
    Mpv,
    WindowsMedia,
}

public enum LibVlcPlaybackProfile
{
    Auto,
    D3D11Va,
    Dxva2,
    Software,
}

public enum MpvPlaybackProfile
{
    Auto,
    SmoothMotion,
}
