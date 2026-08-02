using Efiron.Domain.Sources;

namespace Efiron.Application.Sources;

public sealed record SourceConfiguration(
    SourceDefinition? Playlist,
    SourceDefinition? ProgrammeGuide)
{
    public static SourceConfiguration Empty { get; } = new(null, null);

    public bool IsReadyForLiveTv => Playlist is { IsEnabled: true };
}
