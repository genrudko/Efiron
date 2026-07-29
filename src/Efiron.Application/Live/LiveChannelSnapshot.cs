using Efiron.Domain.Channels;
using Efiron.Domain.ProgrammeGuide;

namespace Efiron.Application.Live;

public sealed record LiveChannelSnapshot(
    ChannelDefinition Channel,
    string? ProgrammeGuideChannelId,
    Programme? CurrentProgramme,
    Programme? NextProgramme);
