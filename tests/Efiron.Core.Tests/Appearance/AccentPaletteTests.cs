using Efiron.Core.Appearance;
using Xunit;

namespace Efiron.Core.Tests.Appearance;

public sealed class AccentPaletteTests
{
    [Fact]
    public void ResolveArgb_UsesWindowsAccentByDefault()
    {
        var result = AccentPalette.ResolveArgb(AppearanceSettings.Default, 0xFF123456);

        Assert.Equal(0xFF123456u, result);
    }

    [Fact]
    public void Normalize_UsesCanonicalCustomHex()
    {
        var settings = new AppearanceSettings(
            AppearanceSettings.CurrentVersion,
            AppThemeMode.Dark,
            AppAccentMode.Custom,
            "#80a1b2c3").Normalize();

        Assert.Equal("#A1B2C3", settings.CustomAccentHex);
        Assert.Equal(0xFFA1B2C3u, AccentPalette.ResolveArgb(settings, 0xFF000000));
    }

    [Fact]
    public void Normalize_InvalidCustomAccentFallsBackToWindows()
    {
        var settings = new AppearanceSettings(
            AppearanceSettings.CurrentVersion,
            AppThemeMode.Light,
            AppAccentMode.Custom,
            "not-a-color").Normalize();

        Assert.Equal(AppAccentMode.Windows, settings.AccentMode);
        Assert.Null(settings.CustomAccentHex);
    }

    [Theory]
    [InlineData(0xFF102040u, 0xFFFFFFFFu)]
    [InlineData(0xFFFFC340u, 0xFF111111u)]
    public void GetReadableForegroundArgb_SelectsContrast(uint background, uint expected)
    {
        Assert.Equal(expected, AccentPalette.GetReadableForegroundArgb(background));
    }

    [Fact]
    public void EnsureReadableSelectionArgb_ClampsExtremeCustomColor()
    {
        var result = AccentPalette.EnsureReadableSelectionArgb(0xFFFFFFFF);

        Assert.Equal(0xFF000000u, result & 0xFF000000u);
        Assert.NotEqual(0xFFFFFFFFu, result);
    }
}
