using Efiron.Domain.Sources;

namespace Efiron.Application.Sources;

public sealed record LoadedSourceContent(
    SourceDefinition Source,
    ReadOnlyMemory<byte> Content,
    Uri? EffectiveUri,
    string? ContentType,
    DateTimeOffset LoadedAtUtc,
    bool IsCacheHit = false);
