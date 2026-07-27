using System.Runtime.InteropServices;
using Efiron.Core.Appearance;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Efiron.App.Appearance;

internal static class AppearanceManager
{
    private static readonly UISettings UiSettings = new();

    static AppearanceManager()
    {
        UiSettings.ColorValuesChanged += (_, _) => SystemColorsChanged?.Invoke(null, EventArgs.Empty);
    }

    public static event EventHandler? SystemColorsChanged;

    public static uint WindowsAccentArgb => ToArgb(UiSettings.GetColorValue(UIColorType.Accent));

    public static uint ResolveAccentArgb(AppearanceSettings settings) =>
        AccentPalette.ResolveArgb(settings, WindowsAccentArgb);

    public static bool ResolveIsDark(AppearanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings = settings.Normalize();
        return settings.ThemeMode switch
        {
            AppThemeMode.Dark => true,
            AppThemeMode.Light => false,
            _ => IsSystemDark(),
        };
    }

    public static void Apply(FrameworkElement root, AppearanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(settings);
        settings = settings.Normalize();

        root.RequestedTheme = settings.ThemeMode switch
        {
            AppThemeMode.Light => ElementTheme.Light,
            AppThemeMode.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        var isDark = ResolveIsDark(settings);
        ApplySurfacePalette(isDark);
        ApplyAccentPalette(settings, isDark);
        ApplyRootResources(root);
    }

    public static Color ColorFromArgb(uint argb) => Color.FromArgb(
        (byte)(argb >> 24),
        (byte)(argb >> 16),
        (byte)(argb >> 8),
        (byte)argb);

    public static SolidColorBrush GetBrush(string resourceKey)
    {
        if (Application.Current.Resources[resourceKey] is not SolidColorBrush brush)
        {
            throw new InvalidOperationException($"Appearance brush '{resourceKey}' is missing.");
        }

        return brush;
    }

    private static void ApplySurfacePalette(bool isDark)
    {
        if (isDark)
        {
            SetBrush("EfironAppBackgroundBrush", 0xFF0E1116);
            SetBrush("EfironSurfaceBrush", 0xFF151A22);
            SetBrush("EfironElevatedSurfaceBrush", 0xFF1B222D);
            SetBrush("EfironSubtleSurfaceBrush", 0xFF202836);
            SetBrush("EfironStrokeBrush", 0xFF2B3442);
            SetBrush("EfironTextPrimaryBrush", 0xFFF5F7FA);
            SetBrush("EfironTextSecondaryBrush", 0xFFC2CAD6);
            SetBrush("EfironTextTertiaryBrush", 0xFF8E99AA);
        }
        else
        {
            SetBrush("EfironAppBackgroundBrush", 0xFFF7F8FA);
            SetBrush("EfironSurfaceBrush", 0xFFFFFFFF);
            SetBrush("EfironElevatedSurfaceBrush", 0xFFFFFFFF);
            SetBrush("EfironSubtleSurfaceBrush", 0xFFF0F3F8);
            SetBrush("EfironStrokeBrush", 0xFFD9DFE8);
            SetBrush("EfironTextPrimaryBrush", 0xFF111827);
            SetBrush("EfironTextSecondaryBrush", 0xFF4B5563);
            SetBrush("EfironTextTertiaryBrush", 0xFF6B7280);
        }
    }

    private static void ApplyAccentPalette(AppearanceSettings settings, bool isDark)
    {
        var accent = ResolveAccentArgb(settings);
        var selection = AccentPalette.EnsureReadableSelectionArgb(accent);
        var neutral = isDark ? 0xFFFFFFFFu : 0xFF000000u;
        var surface = isDark ? 0xFF151A22u : 0xFFFFFFFFu;
        var hover = AccentPalette.Blend(selection, neutral, 0.86d);
        var pressed = AccentPalette.Blend(selection, 0xFF000000, 0.78d);
        var selectedSurface = AccentPalette.Blend(selection, surface, isDark ? 0.30d : 0.16d);
        var foreground = AccentPalette.GetReadableForegroundArgb(selection);

        SetBrush("EfironAccentBrush", selection);
        SetBrush("EfironAccentHoverBrush", hover);
        SetBrush("EfironAccentPressedBrush", pressed);
        SetBrush("EfironAccentSelectionBrush", selectedSurface);
        SetBrush("EfironAccentForegroundBrush", foreground);
        SetBrush("EfironFocusBrush", selection);

        TrySetFrameworkBrush("AccentFillColorDefaultBrush", selection);
        TrySetFrameworkBrush("AccentFillColorSecondaryBrush", hover);
        TrySetFrameworkBrush("AccentFillColorTertiaryBrush", pressed);
        TrySetFrameworkBrush("AccentTextFillColorPrimaryBrush", selection);
        TrySetFrameworkBrush("AccentTextFillColorSecondaryBrush", hover);
        TrySetFrameworkBrush("ControlAccentFillColorDefaultBrush", selection);
        TrySetFrameworkBrush("SystemControlHighlightAccentBrush", selection);
        TrySetFrameworkBrush("SystemControlHighlightAltAccentBrush", hover);
        TrySetFrameworkBrush("SystemControlHighlightListAccentLowBrush", selectedSurface);
        TrySetFrameworkBrush("SystemControlHighlightListAccentMediumBrush", hover);
        TrySetFrameworkBrush("SystemControlHighlightListAccentHighBrush", selection);
        TrySetFrameworkBrush("FocusStrokeColorOuterBrush", selection);
    }

    private static void ApplyRootResources(FrameworkElement root)
    {
        var appBackground = GetBrush("EfironAppBackgroundBrush");
        var surface = GetBrush("EfironSurfaceBrush");
        var subtle = GetBrush("EfironSubtleSurfaceBrush");
        var accent = GetBrush("EfironAccentBrush");
        var accentHover = GetBrush("EfironAccentHoverBrush");
        var accentPressed = GetBrush("EfironAccentPressedBrush");
        var selection = GetBrush("EfironAccentSelectionBrush");
        var focus = GetBrush("EfironFocusBrush");

        root.Resources["NavigationViewExpandedPaneBackground"] = surface;
        root.Resources["NavigationViewDefaultPaneBackground"] = surface;
        root.Resources["NavigationViewTopPaneBackground"] = surface;
        root.Resources["NavigationViewSelectionIndicatorForeground"] = accent;
        root.Resources["NavigationViewItemBackgroundSelected"] = selection;
        root.Resources["NavigationViewItemBackgroundSelectedPointerOver"] = selection;
        root.Resources["NavigationViewItemBackgroundSelectedPressed"] = selection;

        root.Resources["ListViewItemSelectionIndicatorBrush"] = accent;
        root.Resources["ListViewItemBackgroundSelected"] = selection;
        root.Resources["ListViewItemBackgroundSelectedPointerOver"] = selection;
        root.Resources["ListViewItemBackgroundSelectedPressed"] = selection;

        root.Resources["AccentFillColorDefaultBrush"] = accent;
        root.Resources["AccentFillColorSecondaryBrush"] = accentHover;
        root.Resources["AccentFillColorTertiaryBrush"] = accentPressed;
        root.Resources["AccentTextFillColorPrimaryBrush"] = accent;
        root.Resources["ControlAccentFillColorDefaultBrush"] = accent;
        root.Resources["FocusStrokeColorOuterBrush"] = focus;
        root.Resources["ControlFillColorDefaultBrush"] = subtle;

        if (root is NavigationView navigationView)
        {
            navigationView.Background = appBackground;
        }
    }

    private static bool IsSystemDark()
    {
        var background = UiSettings.GetColorValue(UIColorType.Background);
        return ((background.R * 299) + (background.G * 587) + (background.B * 114)) < 128000;
    }

    private static void SetBrush(string resourceKey, uint argb)
    {
        if (Application.Current.Resources[resourceKey] is not SolidColorBrush brush)
        {
            throw new InvalidOperationException($"Appearance brush '{resourceKey}' is missing.");
        }

        brush.Color = ColorFromArgb(argb);
    }

    private static void TrySetFrameworkBrush(string resourceKey, uint argb)
    {
        try
        {
            if (Application.Current.Resources[resourceKey] is SolidColorBrush brush)
            {
                brush.Color = ColorFromArgb(argb);
            }
        }
        catch (Exception exception) when (
            exception is KeyNotFoundException or ArgumentException or COMException)
        {
        }
    }

    private static uint ToArgb(Color color) =>
        ((uint)color.A << 24) |
        ((uint)color.R << 16) |
        ((uint)color.G << 8) |
        color.B;
}
