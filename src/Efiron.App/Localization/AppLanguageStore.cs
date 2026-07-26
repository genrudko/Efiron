namespace Efiron.App.Localization;

internal static class AppLanguageStore
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Efiron");

    private static readonly string LanguageFilePath = Path.Combine(SettingsDirectory, "language.txt");

    public static string? Load()
    {
        try
        {
            if (!File.Exists(LanguageFilePath))
            {
                return null;
            }

            var language = File.ReadAllText(LanguageFilePath).Trim();
            return IsSupported(language) ? language : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Save(string language)
    {
        if (!IsSupported(language))
        {
            throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported interface language.");
        }

        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(LanguageFilePath, language);
    }

    private static bool IsSupported(string language) =>
        language.Equals("ru-RU", StringComparison.OrdinalIgnoreCase) ||
        language.Equals("en-US", StringComparison.OrdinalIgnoreCase);
}
