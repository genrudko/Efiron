namespace Efiron.Domain.Playback;

public static class PlaybackDiagnosticsMath
{
    private const double MicrosecondsPerSecond = 1_000_000d;
    private const double BitsPerByte = 8d;

    public static double? BytesPerMicrosecondToBitsPerSecond(double value)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            return null;
        }

        return value * MicrosecondsPerSecond * BitsPerByte;
    }

    public static double? CalculateCounterRate(
        long previousValue,
        DateTimeOffset previousSampledAtUtc,
        long currentValue,
        DateTimeOffset currentSampledAtUtc)
    {
        var elapsed = currentSampledAtUtc - previousSampledAtUtc;
        if (currentValue < previousValue || elapsed <= TimeSpan.Zero)
        {
            return null;
        }

        return (currentValue - previousValue) / elapsed.TotalSeconds;
    }
}
