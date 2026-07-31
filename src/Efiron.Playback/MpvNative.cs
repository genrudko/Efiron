using System.Reflection;
using System.Runtime.InteropServices;

namespace Efiron.Playback;

internal static class MpvNative
{
    private const string LibraryName = "libmpv-2.dll";

    static MpvNative()
    {
        NativeLibrary.SetDllImportResolver(
            typeof(MpvNative).Assembly,
            ResolveLibrary);
    }

    internal enum Format
    {
        None = 0,
        String = 1,
        OsdString = 2,
        Flag = 3,
        Int64 = 4,
        Double = 5,
    }

    internal enum EventId
    {
        None = 0,
        Shutdown = 1,
        LogMessage = 2,
        GetPropertyReply = 3,
        SetPropertyReply = 4,
        CommandReply = 5,
        StartFile = 6,
        EndFile = 7,
        FileLoaded = 8,
        Idle = 11,
        Tick = 14,
        ClientMessage = 16,
        VideoReconfig = 17,
        AudioReconfig = 18,
        Seek = 20,
        PlaybackRestart = 21,
        PropertyChange = 22,
        QueueOverflow = 24,
        Hook = 25,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct Event
    {
        public readonly EventId EventId;
        public readonly int Error;
        public readonly ulong ReplyUserdata;
        public readonly nint Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct EventEndFile
    {
        public readonly int Reason;
        public readonly int Error;
        public readonly long PlaylistEntryId;
        public readonly long PlaylistInsertId;
        public readonly int PlaylistInsertNumEntries;
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint mpv_create();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mpv_initialize(nint context);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void mpv_terminate_destroy(nint context);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mpv_set_option_string(
        nint context,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mpv_set_property_string(
        nint context,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_get_property(
        nint context,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        Format format,
        nint data);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint mpv_get_property_string(
        nint context,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_command(nint context, nint arguments);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint mpv_wait_event(nint context, double timeoutSeconds);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void mpv_wakeup(nint context);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint mpv_error_string(int error);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void mpv_free(nint data);

    internal static void SetOption(nint context, string name, string value)
    {
        ThrowIfError(mpv_set_option_string(context, name, value), $"option {name}");
    }

    internal static void SetProperty(nint context, string name, string value)
    {
        ThrowIfError(mpv_set_property_string(context, name, value), $"property {name}");
    }

    internal static string? GetString(nint context, string name)
    {
        var pointer = mpv_get_property_string(context, name);
        if (pointer == 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUTF8(pointer);
        }
        finally
        {
            mpv_free(pointer);
        }
    }

    internal static long? GetInt64(nint context, string name)
    {
        var data = Marshal.AllocHGlobal(sizeof(long));
        try
        {
            return mpv_get_property(context, name, Format.Int64, data) < 0
                ? null
                : Marshal.ReadInt64(data);
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
    }

    internal static double? GetDouble(nint context, string name)
    {
        var data = Marshal.AllocHGlobal(sizeof(double));
        try
        {
            if (mpv_get_property(context, name, Format.Double, data) < 0)
            {
                return null;
            }

            var bytes = new byte[sizeof(double)];
            Marshal.Copy(data, bytes, 0, bytes.Length);
            return BitConverter.ToDouble(bytes);
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
    }

    internal static void Command(nint context, params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Length == 0)
        {
            throw new ArgumentException("An mpv command requires at least one argument.", nameof(arguments));
        }

        var nativeStrings = new nint[arguments.Length];
        var nativeArray = Marshal.AllocHGlobal((arguments.Length + 1) * nint.Size);
        try
        {
            for (var index = 0; index < arguments.Length; index++)
            {
                nativeStrings[index] = Marshal.StringToCoTaskMemUTF8(arguments[index]);
                Marshal.WriteIntPtr(nativeArray, index * nint.Size, nativeStrings[index]);
            }

            Marshal.WriteIntPtr(nativeArray, arguments.Length * nint.Size, 0);
            ThrowIfError(mpv_command(context, nativeArray), arguments[0]);
        }
        finally
        {
            foreach (var nativeString in nativeStrings)
            {
                if (nativeString != 0)
                {
                    Marshal.FreeCoTaskMem(nativeString);
                }
            }

            Marshal.FreeHGlobal(nativeArray);
        }
    }

    internal static string DescribeError(int error)
    {
        var pointer = mpv_error_string(error);
        return pointer == 0
            ? $"libmpv error {error}"
            : Marshal.PtrToStringUTF8(pointer) ?? $"libmpv error {error}";
    }

    internal static void ThrowIfError(int error, string operation)
    {
        if (error < 0)
        {
            throw new InvalidOperationException(
                $"libmpv failed to apply {operation}: {DescribeError(error)} ({error}).");
        }
    }

    private static nint ResolveLibrary(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        if (!string.Equals(
                libraryName,
                LibraryName,
                StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "libmpv-2.dll"),
            Path.Combine(AppContext.BaseDirectory, "mpv-2.dll"),
            Path.Combine(AppContext.BaseDirectory, "libmpv.dll"),
            Path.Combine(
                AppContext.BaseDirectory,
                "libmpv",
                "win-x64",
                "libmpv-2.dll"),
            Path.Combine(
                AppContext.BaseDirectory,
                "runtimes",
                "win-x64",
                "native",
                "libmpv-2.dll"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate) &&
                NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }
        }

        return 0;
    }
}
