using Efiron.Domain.ProgrammeGuide;

namespace Efiron.Application.ProgrammeGuide;

public interface IWindowedProgrammeGuideParser : IProgrammeGuideParser
{
    ProgrammeGuideDocument Parse(
        ReadOnlyMemory<byte> content,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd);
}
