using Efiron.Domain.Sources;

namespace Efiron.Application.Sources;

public sealed class SourceConfigurationService(
    ISourceConfigurationStore store)
{
    public ValueTask<SourceConfiguration> LoadAsync(
        CancellationToken cancellationToken = default) =>
        store.LoadAsync(cancellationToken);

    public async ValueTask<SourceConfiguration> SaveAsync(
        string playlistLocation,
        string? programmeGuideLocation,
        CancellationToken cancellationToken = default)
    {
        var playlist = SourceDefinition.Create(
            SourceKind.Playlist,
            playlistLocation);

        var programmeGuide = string.IsNullOrWhiteSpace(programmeGuideLocation)
            ? null
            : SourceDefinition.Create(
                SourceKind.ProgrammeGuide,
                programmeGuideLocation);

        var configuration = new SourceConfiguration(
            playlist,
            programmeGuide);

        await store.SaveAsync(configuration, cancellationToken);
        return configuration;
    }
}
