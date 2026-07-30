using System.Text.Json;
using Efiron.Application.Appearance;
using Efiron.Domain.Appearance;

namespace Efiron.Infrastructure.Appearance;

public sealed class JsonAppearancePreferencesStore(string path)
    : IAppearancePreferencesStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public async ValueTask<AppearancePreferences> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(path))
        {
            return AppearancePreferences.Default;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            useAsync: true);

        try
        {
            var document = await JsonSerializer.DeserializeAsync<AppearancePreferencesDocument>(
                stream,
                SerializerOptions,
                cancellationToken);
            if (document is null || document.Version != 1)
            {
                throw new InvalidDataException(
                    "The appearance preferences document version is unsupported.");
            }

            if (!Enum.IsDefined(document.Theme) || !Enum.IsDefined(document.Accent))
            {
                throw new InvalidDataException(
                    "The appearance preferences document contains unsupported values.");
            }

            return new AppearancePreferences(document.Theme, document.Accent);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The appearance preferences store is not valid JSON.",
                exception);
        }
    }

    public async ValueTask SaveAsync(
        AppearancePreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        var document = new AppearancePreferencesDocument(
            Version: 1,
            Theme: preferences.Theme,
            Accent: preferences.Accent);

        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 16 * 1024,
                             useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    SerializerOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private sealed record AppearancePreferencesDocument(
        int Version,
        AppearanceTheme Theme,
        AccentPalette Accent);
}
