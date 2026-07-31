using Efiron.Domain.Playback;

namespace Efiron.Application.Playback;

public interface IPlaybackBackend : IDisposable
{
    PlaybackBackendId Id { get; }

    string? Version { get; }

    string SelectedProfile { get; }

    PlaybackBackendCapabilities Capabilities { get; }

    IPlaybackSession Session { get; }

    PlaybackBackendDiagnostics CaptureDiagnostics();
}
