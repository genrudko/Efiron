using System.Text.Json;
using Efiron.Core.Appearance;

namespace Efiron.App.Appearance;

internal static class AppearanceSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Efiron");

    private static readonly string SettingsFilePath = Path.Combine(
        SettingsDirectory,
        "appearance-settings.json");

    public static AppearanceSettings Load(out bool invalidStoreRecovered)
    {
        invalidStoreRecovered = false;
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return AppearanceSettings.Default;
            }

            var json = File.ReadAllText(SettingsFilePath);
            var settings = JsonSerializer.Deserialize<AppearanceSettings>(json, SerializerOptions);
            if (settings is null || settings.Version != AppearanceSettings.CurrentVersion)
            {
                invalidStoreRecovered = true;
                return AppearanceSettings.Default;
            }

            return settings.Normalize();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            invalidStoreRecovered = true;
            return AppearanceSettings.Default;
        }
    }

    public static bool TrySave(AppearanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings = settings.Normalize();

        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var temporaryPath = SettingsFilePath + ".tmp";
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(settings, SerializerOptions));
            File.Move(temporaryPath, SettingsFilePath, overwrite: true);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}
