namespace Efiron.Domain.Channels;

public sealed record ChannelDefinition(
    string StableId,
    string Name,
    Uri StreamUri,
    string? ProgrammeGuideId,
    string? ProviderName,
    Uri? LogoUri,
    string? Category,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyDictionary<string, string> PlaybackDirectives,
    int SourceLine);
