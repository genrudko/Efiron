using Efiron.Domain.Appearance;

namespace Efiron.Application.Appearance;

public interface IAppearancePreferencesStore
{
    ValueTask<AppearancePreferences> LoadAsync(
        CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        AppearancePreferences preferences,
        CancellationToken cancellationToken = default);
}
