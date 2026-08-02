namespace Efiron.Application.Sources;

public interface ISourceConfigurationStore
{
    ValueTask<SourceConfiguration> LoadAsync(CancellationToken cancellationToken);

    ValueTask SaveAsync(
        SourceConfiguration configuration,
        CancellationToken cancellationToken);
}
