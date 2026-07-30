using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace Efiron.Desktop;

public sealed partial class MainWindow
{
    private bool _titleBarContrastEnabled;

    private void EnableTitleBarContrast()
    {
        if (_titleBarContrastEnabled || !AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        _titleBarContrastEnabled = true;
        WindowRoot.ActualThemeChanged += WindowRoot_ActualThemeChanged;
        ApplyTitleBarContrast();
    }

    private void WindowRoot_ActualThemeChanged(
        FrameworkElement sender,
        object args) =>
        ApplyTitleBarContrast();

    private void ApplyTitleBarContrast()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        var isLight = WindowRoot.ActualTheme == ElementTheme.Light;
        var foreground = isLight
            ? Color.FromArgb(255, 16, 23, 34)
            : Color.FromArgb(255, 247, 249, 252);
        var inactiveForeground = isLight
            ? Color.FromArgb(170, 77, 90, 108)
            : Color.FromArgb(170, 174, 184, 199);
        var hoverBackground = isLight
            ? Color.FromArgb(24, 16, 23, 34)
            : Color.FromArgb(36, 255, 255, 255);
        var pressedBackground = isLight
            ? Color.FromArgb(44, 16, 23, 34)
            : Color.FromArgb(60, 255, 255, 255);
        var transparent = Color.FromArgb(0, 0, 0, 0);

        var titleBar = AppWindow.TitleBar;
        titleBar.ButtonBackgroundColor = transparent;
        titleBar.ButtonInactiveBackgroundColor = transparent;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = inactiveForeground;
        titleBar.ButtonHoverBackgroundColor = hoverBackground;
        titleBar.ButtonPressedBackgroundColor = pressedBackground;
    }
}
