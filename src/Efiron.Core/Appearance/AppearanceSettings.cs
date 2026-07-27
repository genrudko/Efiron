namespace Efiron.Core.Appearance;

public sealed record AppearanceSettings(
    int Version,
    AppThemeMode ThemeMode,
    AppAccentMode AccentMode,
    string? CustomAccentHex)
{
    public const int CurrentVersion = 1;

    public static AppearanceSettings Default { get; } = new(
        CurrentVersion,
        AppThemeMode.System,
        AppAccentMode.Windows,
        null);

    public AppearanceSettings Normalize()
    {
        var theme = Enum.IsDefined(ThemeMode) ? ThemeMode : AppThemeMode.System;
        var accent = Enum.IsDefined(AccentMode) ? AccentMode : AppAccentMode.Windows;
        var customAccent = AccentPalette.NormalizeCustomHex(CustomAccentHex);

        if (accent == AppAccentMode.Custom && customAccent is null)
        {
            accent = AppAccentMode.Windows;
        }

        return new AppearanceSettings(CurrentVersion, theme, accent, customAccent);
    }
}
