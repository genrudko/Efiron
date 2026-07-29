using Efiron.Domain.Sources;

namespace Efiron.Application.Sources;

public interface ISourceContentLoader
{
    ValueTask<LoadedSourceContent> LoadAsync(
        SourceDefinition source,
        CancellationToken cancellationToken = default);
}
