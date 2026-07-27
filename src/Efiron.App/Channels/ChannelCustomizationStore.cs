using System.Text.Json;
using System.Text.Json.Serialization;
using Efiron.Core.Channels;

namespace Efiron.App.Channels;

internal static class ChannelCustomizationStore
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Efiron");

    private static readonly string StoreFilePath = Path.Combine(
        SettingsDirectory,
        "channel-customizations.json");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static ChannelLibrarySnapshot Load(out bool invalidStoreRecovered)
    {
        invalidStoreRecovered = false;
        try
        {
            if (!File.Exists(StoreFilePath))
            {
                return ChannelLibrarySnapshot.Empty;
            }

            var json = File.ReadAllText(StoreFilePath);
            var snapshot = JsonSerializer.Deserialize<ChannelLibrarySnapshot>(json, SerializerOptions);
            if (snapshot is null || snapshot.Version > ChannelLibrarySnapshot.CurrentVersion)
            {
                invalidStoreRecovered = true;
                return ChannelLibrarySnapshot.Empty;
            }

            return snapshot.Normalize();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            invalidStoreRecovered = true;
            return ChannelLibrarySnapshot.Empty;
        }
    }

    public static bool TrySave(ChannelLibrarySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            temporaryPath = Path.Combine(
                SettingsDirectory,
                $"channel-customizations.{Guid.NewGuid():N}.tmp");
            var json = JsonSerializer.Serialize(snapshot.Normalize(), SerializerOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, StoreFilePath, overwrite: true);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return false;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
