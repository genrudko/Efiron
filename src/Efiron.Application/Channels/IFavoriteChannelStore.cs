namespace Efiron.Application.Channels;

public interface IFavoriteChannelStore
{
    ValueTask<IReadOnlySet<string>> LoadAsync(
        CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        IReadOnlySet<string> stableIds,
        CancellationToken cancellationToken = default);
}
