using Efiron.Domain.Appearance;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Efiron.Desktop.Appearance;

internal sealed class AppearanceController(
    ResourceDictionary applicationResources,
    FrameworkElement root)
{
    public void Apply(AppearancePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        root.RequestedTheme = preferences.Theme switch
        {
            AppearanceTheme.Light => ElementTheme.Light,
            AppearanceTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        var palette = AccentColors.For(preferences.Accent);
        ApplyPalette(
            GetThemeDictionary("Default"),
            palette.Dark,
            palette.DarkHover,
            palette.DarkSubtle);
        ApplyPalette(
            GetThemeDictionary("Light"),
            palette.Light,
            palette.LightHover,
            palette.LightSubtle);
    }

    private ResourceDictionary GetThemeDictionary(string key)
    {
        if (applicationResources.ThemeDictionaries.TryGetValue(key, out var value) &&
            value is ResourceDictionary dictionary)
        {
            return dictionary;
        }

        throw new InvalidOperationException($"Theme dictionary '{key}' is missing.");
    }

    private static void ApplyPalette(
        ResourceDictionary dictionary,
        Color accent,
        Color hover,
        Color subtle)
    {
        SetBrush(dictionary, "EfironAccentBrush", accent);
        SetBrush(dictionary, "EfironAccentHoverBrush", hover);
        SetBrush(dictionary, "EfironAccentSubtleBrush", subtle);

        SetBrush(dictionary, "AccentFillColorDefaultBrush", accent);
        SetBrush(dictionary, "AccentFillColorSecondaryBrush", hover);
        SetBrush(dictionary, "AccentFillColorTertiaryBrush", WithAlpha(accent, 0xCC));
        SetBrush(dictionary, "AccentTextFillColorPrimaryBrush", accent);
        SetBrush(dictionary, "AccentTextFillColorSecondaryBrush", hover);
        SetBrush(dictionary, "ProgressBarForeground", accent);
        SetBrush(dictionary, "ProgressRingForeground", accent);
        SetBrush(dictionary, "TextControlSelectionHighlightColor", WithAlpha(accent, 0x99));
    }

    private static void SetBrush(
        ResourceDictionary dictionary,
        string key,
        Color color)
    {
        if (dictionary.TryGetValue(key, out var value) && value is SolidColorBrush brush)
        {
            brush.Color = color;
            return;
        }

        dictionary[key] = new SolidColorBrush(color);
    }

    private static Color WithAlpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);

    private sealed record AccentColors(
        Color Dark,
        Color DarkHover,
        Color DarkSubtle,
        Color Light,
        Color LightHover,
        Color LightSubtle)
    {
        public static AccentColors For(AccentPalette palette) => palette switch
        {
            AccentPalette.Violet => Create("8B5CF6", "9D78F8", "338B5CF6", "7446D8", "865BE3", "287446D8"),
            AccentPalette.Teal => Create("19B89A", "35C8AD", "3319B89A", "087F6D", "139A83", "28087F6D"),
            AccentPalette.Orange => Create("F59E42", "FFB15F", "33F59E42", "C76512", "DE791E", "28C76512"),
            AccentPalette.Rose => Create("F05D88", "F4779B", "33F05D88", "C33D68", "D55078", "28C33D68"),
            _ => Create("2586FF", "3C94FF", "332586FF", "126FDB", "2580E8", "28126FDB"),
        };

        private static AccentColors Create(
            string dark,
            string darkHover,
            string darkSubtle,
            string light,
            string lightHover,
            string lightSubtle) =>
            new(
                Parse(dark),
                Parse(darkHover),
                Parse(darkSubtle),
                Parse(light),
                Parse(lightHover),
                Parse(lightSubtle));

        private static Color Parse(string hex)
        {
            var value = Convert.ToUInt32(hex, 16);
            return hex.Length == 8
                ? Color.FromArgb(
                    (byte)(value >> 24),
                    (byte)(value >> 16),
                    (byte)(value >> 8),
                    (byte)value)
                : Color.FromArgb(
                    255,
                    (byte)(value >> 16),
                    (byte)(value >> 8),
                    (byte)value);
        }
    }
}
