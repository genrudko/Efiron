using System.Globalization;

namespace Efiron.Core.Appearance;

public static class AccentPalette
{
    public const uint DefaultWindowsAccentArgb = 0xFF1769E0;

    public static uint ResolveArgb(AppearanceSettings settings, uint windowsAccentArgb)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings = settings.Normalize();

        return settings.AccentMode switch
        {
            AppAccentMode.Windows => ForceOpaque(windowsAccentArgb),
            AppAccentMode.Blue => 0xFF1769E0,
            AppAccentMode.Purple => 0xFF7C3AED,
            AppAccentMode.Pink => 0xFFE5487A,
            AppAccentMode.Orange => 0xFFF59E0B,
            AppAccentMode.Green => 0xFF22A559,
            AppAccentMode.Teal => 0xFF009E9E,
            AppAccentMode.Custom => ParseHex(settings.CustomAccentHex!) ?? ForceOpaque(windowsAccentArgb),
            _ => ForceOpaque(windowsAccentArgb),
        };
    }

    public static uint EnsureReadableSelectionArgb(uint argb)
    {
        var (red, green, blue) = Components(argb);
        RgbToHsl(red / 255d, green / 255d, blue / 255d, out var hue, out var saturation, out var lightness);
        saturation = Math.Max(saturation, 0.42d);
        lightness = Math.Clamp(lightness, 0.30d, 0.68d);
        var normalized = HslToRgb(hue, saturation, lightness);
        return Compose(normalized.Red, normalized.Green, normalized.Blue);
    }

    public static uint GetReadableForegroundArgb(uint backgroundArgb)
    {
        var (red, green, blue) = Components(backgroundArgb);
        var luminance = RelativeLuminance(red, green, blue);
        var whiteContrast = 1.05d / (luminance + 0.05d);
        var blackContrast = (luminance + 0.05d) / 0.05d;
        return whiteContrast >= blackContrast ? 0xFFFFFFFF : 0xFF111111;
    }

    public static uint Blend(uint foregroundArgb, uint backgroundArgb, double foregroundAmount)
    {
        foregroundAmount = Math.Clamp(foregroundAmount, 0d, 1d);
        var foreground = Components(foregroundArgb);
        var background = Components(backgroundArgb);
        return Compose(
            BlendComponent(foreground.Red, background.Red, foregroundAmount),
            BlendComponent(foreground.Green, background.Green, foregroundAmount),
            BlendComponent(foreground.Blue, background.Blue, foregroundAmount));
    }

    public static string? NormalizeCustomHex(string? value)
    {
        var argb = ParseHex(value);
        return argb is null
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"#{(argb.Value >> 16) & 0xFF:X2}{(argb.Value >> 8) & 0xFF:X2}{argb.Value & 0xFF:X2}");
    }

    private static uint? ParseHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim().TrimStart('#');
        if (text.Length == 8)
        {
            text = text[2..];
        }

        return text.Length == 6 &&
               uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            ? 0xFF000000u | rgb
            : null;
    }

    private static uint ForceOpaque(uint argb) => 0xFF000000u | (argb & 0x00FFFFFFu);

    private static (byte Red, byte Green, byte Blue) Components(uint argb) =>
        ((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    private static uint Compose(byte red, byte green, byte blue) =>
        0xFF000000u | ((uint)red << 16) | ((uint)green << 8) | blue;

    private static byte BlendComponent(byte foreground, byte background, double foregroundAmount) =>
        (byte)Math.Round(
            (foreground * foregroundAmount) + (background * (1d - foregroundAmount)),
            MidpointRounding.AwayFromZero);

    private static double RelativeLuminance(byte red, byte green, byte blue) =>
        (0.2126d * Linearize(red)) + (0.7152d * Linearize(green)) + (0.0722d * Linearize(blue));

    private static double Linearize(byte component)
    {
        var value = component / 255d;
        return value <= 0.04045d
            ? value / 12.92d
            : Math.Pow((value + 0.055d) / 1.055d, 2.4d);
    }

    private static void RgbToHsl(
        double red,
        double green,
        double blue,
        out double hue,
        out double saturation,
        out double lightness)
    {
        var maximum = Math.Max(red, Math.Max(green, blue));
        var minimum = Math.Min(red, Math.Min(green, blue));
        lightness = (maximum + minimum) / 2d;

        if (Math.Abs(maximum - minimum) < double.Epsilon)
        {
            hue = 0d;
            saturation = 0d;
            return;
        }

        var delta = maximum - minimum;
        saturation = lightness > 0.5d
            ? delta / (2d - maximum - minimum)
            : delta / (maximum + minimum);

        hue = maximum switch
        {
            var value when Math.Abs(value - red) < double.Epsilon =>
                ((green - blue) / delta) + (green < blue ? 6d : 0d),
            var value when Math.Abs(value - green) < double.Epsilon =>
                ((blue - red) / delta) + 2d,
            _ => ((red - green) / delta) + 4d,
        };
        hue /= 6d;
    }

    private static (byte Red, byte Green, byte Blue) HslToRgb(
        double hue,
        double saturation,
        double lightness)
    {
        if (saturation <= 0d)
        {
            var neutral = (byte)Math.Round(lightness * 255d, MidpointRounding.AwayFromZero);
            return (neutral, neutral, neutral);
        }

        var q = lightness < 0.5d
            ? lightness * (1d + saturation)
            : lightness + saturation - (lightness * saturation);
        var p = (2d * lightness) - q;
        return (
            ToByte(HueToRgb(p, q, hue + (1d / 3d))),
            ToByte(HueToRgb(p, q, hue)),
            ToByte(HueToRgb(p, q, hue - (1d / 3d))));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0d)
        {
            t += 1d;
        }
        else if (t > 1d)
        {
            t -= 1d;
        }

        if (t < 1d / 6d)
        {
            return p + ((q - p) * 6d * t);
        }

        if (t < 1d / 2d)
        {
            return q;
        }

        if (t < 2d / 3d)
        {
            return p + ((q - p) * ((2d / 3d) - t) * 6d);
        }

        return p;
    }

    private static byte ToByte(double value) =>
        (byte)Math.Round(Math.Clamp(value, 0d, 1d) * 255d, MidpointRounding.AwayFromZero);
}
