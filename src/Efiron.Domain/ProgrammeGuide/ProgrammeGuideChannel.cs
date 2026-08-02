namespace Efiron.Domain.ProgrammeGuide;

public sealed record ProgrammeGuideChannel(
    string Id,
    IReadOnlyList<string> DisplayNames,
    Uri? IconUri);
