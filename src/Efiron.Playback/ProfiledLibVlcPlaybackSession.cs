using Efiron.Application.Playback;
using Efiron.Domain.Playback;

namespace Efiron.Playback;

internal sealed class ProfiledLibVlcPlaybackSession : IPlaybackSession
{
    private readonly LibVlcPlaybackSession _inner;
    private readonly LibVlcPlaybackProfile _profile;

    public ProfiledLibVlcPlaybackSession(
        LibVlcPlaybackSession inner,
        LibVlcPlaybackProfile profile)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _profile = profile;
    }

    public event EventHandler<PlaybackSnapshotChangedEventArgs>? SnapshotChanged
    {
        add => _inner.SnapshotChanged += value;
        remove => _inner.SnapshotChanged -= value;
    }

    public PlaybackSnapshot Snapshot => _inner.Snapshot;

    public ValueTask PlayAsync(
        PlaybackRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var profileValue = _profile switch
        {
            LibVlcPlaybackProfile.D3D11Va => "d3d11va",
            LibVlcPlaybackProfile.Dxva2 => "dxva2",
            LibVlcPlaybackProfile.Software => "none",
            _ => null,
        };
        if (profileValue is null)
        {
            return _inner.PlayAsync(request, cancellationToken);
        }

        var directives = new Dictionary<string, string>(
            request.Directives,
            StringComparer.OrdinalIgnoreCase)
        {
            ["extvlcopt:avcodec-hw"] = profileValue,
        };
        var profiledRequest = new PlaybackRequest(
            request.Source,
            request.ChannelStableId,
            request.DisplayName,
            directives);
        return _inner.PlayAsync(profiledRequest, cancellationToken);
    }

    public void Pause() => _inner.Pause();

    public void Resume() => _inner.Resume();

    public void Stop() => _inner.Stop();

    public void SetMuted(bool isMuted) => _inner.SetMuted(isMuted);

    public void SetVolume(int volume) => _inner.SetVolume(volume);

    public void Dispose() => _inner.Dispose();
}
