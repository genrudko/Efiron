using LibVLCSharp.Shared;

namespace Efiron.Playback;

internal sealed record LibVlcDiagnosticSnapshot(
    DateTimeOffset SampledAtUtc,
    bool HasStatistics,
    MediaStats Statistics,
    string? VideoCodec,
    string? AudioCodec,
    int? VideoWidth,
    int? VideoHeight,
    double? DeclaredFramesPerSecond,
    double? BufferedPercentage,
    long RebufferCount,
    bool? HardwareDecodingActive,
    string? Decoder,
    string? GraphicsDevice,
    string? VideoRenderer,
    TimeSpan? SessionDuration,
    TimeSpan? MediaPosition,
    TimeSpan? StartupLatency);
