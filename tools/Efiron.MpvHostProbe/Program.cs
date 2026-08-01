using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using Efiron.Application.Playback;
using Efiron.Domain.Playback;
using Efiron.Playback;

if (args.Length != 2)
{
    Console.Error.WriteLine(
        "Usage: Efiron.MpvHostProbe <media-url> <evidence-json>");
    return 2;
}

var mediaUri = new Uri(args[0], UriKind.Absolute);
var evidencePath = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);

using var hostWindow = new ProbeParentWindow();
var backend = new MpvProcessPlaybackBackend(
    hostWindow.Handle,
    MpvPlaybackProfile.Auto);
var session = backend.Session;
var snapshots = Channel.CreateUnbounded<PlaybackSnapshot>();

void SnapshotChanged(
    object? sender,
    PlaybackSnapshotChangedEventArgs eventArgs)
{
    snapshots.Writer.TryWrite(eventArgs.Snapshot);
}

async Task<PlaybackSnapshot> WaitForPlayingAsync(TimeSpan timeout)
{
    using var timeoutSource = new CancellationTokenSource(timeout);
    while (await snapshots.Reader.WaitToReadAsync(timeoutSource.Token))
    {
        while (snapshots.Reader.TryRead(out var snapshot))
        {
            if (snapshot.State == PlaybackState.Playing)
            {
                return snapshot;
            }

            if (snapshot.State == PlaybackState.Failed)
            {
                throw new InvalidOperationException(
                    snapshot.ErrorMessage ?? "mpv host playback failed.");
            }
        }
    }

    throw new TimeoutException("mpv host did not reach Playing.");
}

async Task<PlaybackBackendDiagnostics> WaitForDiagnosticsAsync(
    TimeSpan timeout)
{
    var deadline = DateTimeOffset.UtcNow + timeout;
    PlaybackBackendDiagnostics? latest = null;
    while (DateTimeOffset.UtcNow < deadline)
    {
        latest = backend.CaptureDiagnostics();
        if (latest.BackendId == PlaybackBackendId.MpvHost &&
            latest.PlaybackState == PlaybackState.Playing &&
            !string.IsNullOrWhiteSpace(latest.BackendVersion) &&
            !string.IsNullOrWhiteSpace(latest.VideoCodec) &&
            latest.VideoWidth is > 0 &&
            latest.VideoHeight is > 0 &&
            !string.IsNullOrWhiteSpace(latest.VideoRenderer) &&
            latest.PresentationMode?.Contains(
                "process=out-of-process",
                StringComparison.Ordinal) == true &&
            latest.PresentationMode?.Contains(
                "d3d11-output=native-window",
                StringComparison.Ordinal) == true)
        {
            return latest;
        }

        await Task.Delay(250);
    }

    throw new InvalidOperationException(
        "mpv host diagnostic properties did not settle: " +
        JsonSerializer.Serialize(latest));
}

session.SnapshotChanged += SnapshotChanged;
int? firstProcessId = null;
int? secondProcessId = null;
PlaybackBackendDiagnostics? firstDiagnostics = null;
PlaybackBackendDiagnostics? secondDiagnostics = null;
bool firstProcessExitedAfterSwitch = false;
bool secondProcessExitedAfterDispose = false;
var backendDisposed = false;

try
{
    session.SetVolume(37);
    session.SetMuted(false);
    await session.PlayAsync(new PlaybackRequest(
        mediaUri,
        "mpv.host.first",
        "mpv native-window host first"));
    var firstPlaying = await WaitForPlayingAsync(TimeSpan.FromSeconds(30));
    firstProcessId = backend.HostProcessId ?? throw new InvalidOperationException(
        "mpv host reached Playing without a child process ID.");

    session.Pause();
    if (session.Snapshot.State != PlaybackState.Paused)
    {
        throw new InvalidOperationException("mpv host pause was not published.");
    }

    session.Resume();
    if (session.Snapshot.State != PlaybackState.Playing)
    {
        throw new InvalidOperationException("mpv host resume was not published.");
    }

    session.SetMuted(true);
    session.SetMuted(false);
    if (session.Snapshot.Volume != 37 || session.Snapshot.IsMuted)
    {
        throw new InvalidOperationException(
            "mpv host audio state did not remain synchronized.");
    }

    firstDiagnostics = await WaitForDiagnosticsAsync(TimeSpan.FromSeconds(10));
    if (!string.Equals(
            firstDiagnostics.VideoRenderer,
            "gpu-next",
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "The first mpv host renderer is unexpected: " +
            JsonSerializer.Serialize(firstDiagnostics));
    }

    await session.PlayAsync(new PlaybackRequest(
        mediaUri,
        "mpv.host.second",
        "mpv native-window host second"));
    var secondPlaying = await WaitForPlayingAsync(TimeSpan.FromSeconds(30));
    secondProcessId = backend.HostProcessId ?? throw new InvalidOperationException(
        "The restarted mpv host has no child process ID.");
    if (secondProcessId == firstProcessId)
    {
        throw new InvalidOperationException(
            "A channel restart reused the previous mpv process.");
    }

    firstProcessExitedAfterSwitch = await WaitForProcessExitAsync(
        firstProcessId.Value,
        TimeSpan.FromSeconds(6));
    if (!firstProcessExitedAfterSwitch)
    {
        throw new InvalidOperationException(
            "The previous mpv process remained alive after channel restart.");
    }

    secondDiagnostics = await WaitForDiagnosticsAsync(TimeSpan.FromSeconds(10));
    if (secondPlaying.ChannelStableId != "mpv.host.second")
    {
        throw new InvalidOperationException(
            "The restarted mpv host did not publish the second channel state.");
    }

    session.SnapshotChanged -= SnapshotChanged;
    backend.Dispose();
    backendDisposed = true;
    secondProcessExitedAfterDispose = await WaitForProcessExitAsync(
        secondProcessId.Value,
        TimeSpan.FromSeconds(6));
    if (!secondProcessExitedAfterDispose)
    {
        throw new InvalidOperationException(
            "The active mpv process remained alive after backend disposal.");
    }

    var evidence = new
    {
        Backend = PlaybackBackendId.MpvHost.ToString(),
        Profile = MpvPlaybackProfile.Auto.ToString(),
        HostWindow = $"0x{hostWindow.Handle:X}",
        First = new
        {
            ProcessId = firstProcessId,
            firstPlaying.State,
            firstPlaying.ChannelStableId,
            firstDiagnostics.BackendVersion,
            firstDiagnostics.VideoCodec,
            firstDiagnostics.VideoWidth,
            firstDiagnostics.VideoHeight,
            firstDiagnostics.DeclaredFramesPerSecond,
            firstDiagnostics.RenderedFramesPerSecond,
            firstDiagnostics.DisplayFramesPerSecond,
            firstDiagnostics.EstimatedDisplayFramesPerSecond,
            firstDiagnostics.DroppedFrames,
            firstDiagnostics.Decoder,
            firstDiagnostics.VideoRenderer,
            firstDiagnostics.PresentationMode,
        },
        Second = new
        {
            ProcessId = secondProcessId,
            secondPlaying.State,
            secondPlaying.ChannelStableId,
            secondDiagnostics.BackendVersion,
            secondDiagnostics.VideoCodec,
            secondDiagnostics.VideoWidth,
            secondDiagnostics.VideoHeight,
            secondDiagnostics.DeclaredFramesPerSecond,
            secondDiagnostics.RenderedFramesPerSecond,
            secondDiagnostics.DisplayFramesPerSecond,
            secondDiagnostics.EstimatedDisplayFramesPerSecond,
            secondDiagnostics.DroppedFrames,
            secondDiagnostics.Decoder,
            secondDiagnostics.VideoRenderer,
            secondDiagnostics.PresentationMode,
        },
        PidChanged = firstProcessId != secondProcessId,
        FirstProcessExitedAfterSwitch = firstProcessExitedAfterSwitch,
        SecondProcessExitedAfterDispose = secondProcessExitedAfterDispose,
        RecordedAtUtc = DateTimeOffset.UtcNow,
    };

    await File.WriteAllTextAsync(
        evidencePath,
        JsonSerializer.Serialize(
            evidence,
            new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(evidence));
    return 0;
}
finally
{
    session.SnapshotChanged -= SnapshotChanged;
    if (!backendDisposed)
    {
        backend.Dispose();
    }
}

static async Task<bool> WaitForProcessExitAsync(
    int processId,
    TimeSpan timeout)
{
    var deadline = DateTimeOffset.UtcNow + timeout;
    while (DateTimeOffset.UtcNow < deadline)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return true;
            }
        }
        catch (ArgumentException)
        {
            return true;
        }

        await Task.Delay(100);
    }

    return false;
}

internal sealed class ProbeParentWindow : IDisposable
{
    private const uint WsOverlappedWindow = 0x00CF0000;
    private const uint WsVisible = 0x10000000;
    private const uint WmClose = 0x0010;
    private const uint WmQuit = 0x0012;

    private readonly TaskCompletionSource<nint> _windowReady = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private uint _threadId;
    private bool _disposed;

    public ProbeParentWindow()
    {
        _thread = new Thread(RunWindowThread)
        {
            IsBackground = true,
            Name = "Efiron mpv host probe window",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        Handle = _windowReady.Task.WaitAsync(TimeSpan.FromSeconds(10))
            .GetAwaiter()
            .GetResult();
    }

    public nint Handle { get; private set; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var handle = Handle;
        Handle = 0;
        if (handle != 0)
        {
            PostMessageW(handle, WmClose, 0, 0);
        }
        if (_threadId != 0)
        {
            PostThreadMessageW(_threadId, WmQuit, 0, 0);
        }
        _thread.Join(TimeSpan.FromSeconds(3));
    }

    private void RunWindowThread()
    {
        _threadId = GetCurrentThreadId();
        var handle = CreateWindowExW(
            0,
            "STATIC",
            "Efiron mpv native-window probe",
            WsOverlappedWindow | WsVisible,
            100,
            100,
            1280,
            720,
            0,
            0,
            0,
            0);
        if (handle == 0)
        {
            _windowReady.TrySetException(
                new Win32Exception(Marshal.GetLastWin32Error()));
            return;
        }

        _windowReady.TrySetResult(handle);
        while (GetMessageW(out var message, 0, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }

        DestroyWindow(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Id;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint extendedStyle,
        [MarshalAs(UnmanagedType.LPWStr)] string className,
        [MarshalAs(UnmanagedType.LPWStr)] string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parentWindow,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll", EntryPoint = "GetMessageW")]
    private static extern int GetMessageW(
        out Message message,
        nint window,
        uint minimum,
        uint maximum);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static extern nint DispatchMessageW(ref Message message);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessageW(
        uint threadId,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);
}
