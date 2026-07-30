using System.Text.Json;
using Efiron.Application.Playback;
using Efiron.Domain.Playback;

namespace Efiron.Infrastructure.Playback;

public sealed class JsonPlaybackPreferencesStore(string path)
    : IPlaybackPreferencesStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public async ValueTask<PlaybackPreferences> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(path))
        {
            return PlaybackPreferences.Default;
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
            var document = await JsonSerializer.DeserializeAsync<PlaybackPreferencesDocument>(
                stream,
                SerializerOptions,
                cancellationToken);
            if (document is null || document.Version != 1)
            {
                throw new InvalidDataException(
                    "The playback preferences document version is unsupported.");
            }

            return new PlaybackPreferences(
                document.SelectedChannelStableId,
                document.Volume,
                document.IsMuted);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The playback preferences store is not valid JSON.",
                exception);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException(
                "The playback preferences store contains invalid values.",
                exception);
        }
    }

    public async ValueTask SaveAsync(
        PlaybackPreferences preferences,
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
        var document = new PlaybackPreferencesDocument(
            Version: 1,
            SelectedChannelStableId: preferences.SelectedChannelStableId,
            Volume: preferences.Volume,
            IsMuted: preferences.IsMuted);

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

    private sealed record PlaybackPreferencesDocument(
        int Version,
        string? SelectedChannelStableId,
        int Volume,
        bool IsMuted);
}
