namespace Efiron.Domain.ProgrammeGuide;

public sealed record ProgrammeGuideDocument(
    IReadOnlyList<ProgrammeGuideChannel> Channels,
    IReadOnlyList<Programme> Programmes,
    IReadOnlyList<ProgrammeGuideParseWarning> Warnings)
{
    public static ProgrammeGuideDocument Empty { get; } = new(
        Array.Empty<ProgrammeGuideChannel>(),
        Array.Empty<Programme>(),
        Array.Empty<ProgrammeGuideParseWarning>());
}
