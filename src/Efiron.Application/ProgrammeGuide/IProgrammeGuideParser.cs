using Efiron.Domain.ProgrammeGuide;

namespace Efiron.Application.ProgrammeGuide;

public interface IProgrammeGuideParser
{
    ProgrammeGuideDocument Parse(ReadOnlyMemory<byte> content);
}
