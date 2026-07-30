namespace Efiron.Domain.Appearance;

public sealed record AppearancePreferences(
    AppearanceTheme Theme,
    AccentPalette Accent)
{
    public static AppearancePreferences Default { get; } = new(
        AppearanceTheme.System,
        AccentPalette.Blue);
}
