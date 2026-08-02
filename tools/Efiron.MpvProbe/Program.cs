using System.Text.Json;
using Efiron.Domain.Playback;
using Efiron.Playback;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: Efiron.MpvProbe <media-url> <evidence-json>");
    return 2;
}

var mediaUri = new Uri(args[0], UriKind.Absolute);
var evidencePath = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);

using var backend = new MpvPlaybackBackend(MpvPlaybackProfile.Auto);
backend.SetCompositionSize(1280, 720);
var session = backend.Session;
var playing = new TaskCompletionSource<PlaybackSnapshot>(
    TaskCreationOptions.RunContinuationsAsynchronously);

void SnapshotChanged(
    object? sender,
    Efiron.Application.Playback.PlaybackSnapshotChangedEventArgs eventArgs)
{
    if (eventArgs.Snapshot.State == PlaybackState.Playing)
    {
        playing.TrySetResult(eventArgs.Snapshot);
    }
    else if (eventArgs.Snapshot.State == PlaybackState.Failed)
    {
        playing.TrySetException(new InvalidOperationException(
            eventArgs.Snapshot.ErrorMessage ?? "mpv playback failed."));
    }
}

session.SnapshotChanged += SnapshotChanged;
try
{
    await session.PlayAsync(new PlaybackRequest(
        mediaUri,
        "mpv.probe",
        "mpv composition probe"));

    var playingSnapshot = await playing.Task.WaitAsync(TimeSpan.FromSeconds(25));
    var swapChainDeadline = DateTimeOffset.UtcNow.AddSeconds(12);
    while (backend.DisplaySwapChain == 0 &&
           DateTimeOffset.UtcNow < swapChainDeadline)
    {
        await Task.Delay(100);
    }

    if (backend.DisplaySwapChain == 0)
    {
        throw new InvalidOperationException(
            "mpv reached Playing but did not expose display-swapchain.");
    }

    session.Pause();
    if (session.Snapshot.State != PlaybackState.Paused)
    {
        throw new InvalidOperationException("mpv pause state was not published.");
    }

    session.Resume();
    if (session.Snapshot.State != PlaybackState.Playing)
    {
        throw new InvalidOperationException("mpv resume state was not published.");
    }

    session.SetVolume(37);
    session.SetMuted(true);
    session.SetMuted(false);
    if (session.Snapshot.Volume != 37 || session.Snapshot.IsMuted)
    {
        throw new InvalidOperationException(
            "mpv audio state did not remain synchronized.");
    }

    await Task.Delay(1200);
    var diagnostics = backend.CaptureDiagnostics();
    var evidence = new
    {
        Backend = backend.Id.ToString(),
        Profile = backend.SelectedProfile,
        Version = backend.Version,
        State = playingSnapshot.State.ToString(),
        SwapChainAvailable = backend.DisplaySwapChain != 0,
        SwapChainPointer = $"0x{backend.DisplaySwapChain:X}",
        diagnostics.Container,
        diagnostics.VideoCodec,
        diagnostics.AudioCodec,
        diagnostics.VideoWidth,
        diagnostics.VideoHeight,
        diagnostics.DeclaredFramesPerSecond,
        diagnostics.RenderedFramesPerSecond,
        diagnostics.DroppedFrames,
        diagnostics.HardwareDecodingActive,
        diagnostics.Decoder,
        diagnostics.VideoRenderer,
        diagnostics.StartupLatency,
        diagnostics.MediaPosition,
        Volume = session.Snapshot.Volume,
        Muted = session.Snapshot.IsMuted,
        RecordedAtUtc = DateTimeOffset.UtcNow,
    };

    await File.WriteAllTextAsync(
        evidencePath,
        JsonSerializer.Serialize(
            evidence,
            new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(evidence));
    session.Stop();
    return 0;
}
finally
{
    session.SnapshotChanged -= SnapshotChanged;
}
