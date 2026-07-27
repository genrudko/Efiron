using Efiron.App.Appearance;
using Efiron.Core.Appearance;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.UI;

namespace Efiron.App;

public sealed partial class MainWindow
{
    private readonly Dictionary<AppAccentMode, ToggleButton> _accentButtons = [];

    private AppearanceSettings _appearanceSettings = AppearanceSettings.Default;
    private ResourceLoader _appearanceResources = null!;
    private ComboBox _themeModeComboBox = null!;
    private ColorPicker _customAccentColorPicker = null!;
    private InfoBar _appearanceInfoBar = null!;
    private bool _appearanceWorkspaceInitialized;
    private bool _isUpdatingAppearanceControls;

    internal void InitializeAppearanceWorkspace()
    {
        if (_appearanceWorkspaceInitialized)
        {
            return;
        }

        _appearanceWorkspaceInitialized = true;
        _appearanceResources = new ResourceLoader(
            ResourceLoader.GetDefaultResourceFilePath(),
            "Appearance");
        _appearanceSettings = AppearanceSettingsStore.Load(out var invalidStoreRecovered);

        CreateAppearanceSettingsCard();
        ApplyAppearance(save: false);
        AppearanceManager.SystemColorsChanged += AppearanceManager_SystemColorsChanged;
        Closed += AppearanceWindow_Closed;

        if (invalidStoreRecovered)
        {
            ShowAppearanceMessage(
                InfoBarSeverity.Warning,
                _appearanceResources.GetString("StoreRecoveredTitle"),
                _appearanceResources.GetString("StoreRecoveredMessage"));
        }
    }

    private void CreateAppearanceSettingsCard()
    {
        var cardContent = new StackPanel { Spacing = 16 };
        cardContent.Children.Add(new TextBlock
        {
            Text = _appearanceResources.GetString("InterfaceTitle"),
            Style = (Style)Application.Current.Resources["EfironSectionTitleTextStyle"],
        });
        cardContent.Children.Add(new TextBlock
        {
            Text = _appearanceResources.GetString("InterfaceDescription"),
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["EfironCaptionTextStyle"],
        });

        _themeModeComboBox = new ComboBox
        {
            Header = _appearanceResources.GetString("ThemeHeader"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxWidth = 360,
        };
        AddThemeItem(AppThemeMode.System, "ThemeSystem");
        AddThemeItem(AppThemeMode.Light, "ThemeLight");
        AddThemeItem(AppThemeMode.Dark, "ThemeDark");
        _themeModeComboBox.SelectionChanged += ThemeModeComboBox_SelectionChanged;
        cardContent.Children.Add(_themeModeComboBox);

        var accentSection = new StackPanel { Spacing = 8 };
        accentSection.Children.Add(new TextBlock
        {
            Text = _appearanceResources.GetString("AccentHeader"),
            Style = (Style)Application.Current.Resources["EfironBodyTextStyle"],
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        accentSection.Children.Add(new TextBlock
        {
            Text = _appearanceResources.GetString("AccentDescription"),
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["EfironCaptionTextStyle"],
        });

        var swatches = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        AddAccentButton(swatches, AppAccentMode.Windows, "AccentWindows");
        AddAccentButton(swatches, AppAccentMode.Blue, "AccentBlue");
        AddAccentButton(swatches, AppAccentMode.Purple, "AccentPurple");
        AddAccentButton(swatches, AppAccentMode.Pink, "AccentPink");
        AddAccentButton(swatches, AppAccentMode.Orange, "AccentOrange");
        AddAccentButton(swatches, AppAccentMode.Green, "AccentGreen");
        AddAccentButton(swatches, AppAccentMode.Teal, "AccentTeal");
        accentSection.Children.Add(swatches);

        var customAccentButton = new Button
        {
            Content = _appearanceResources.GetString("AccentCustom"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Style = (Style)Application.Current.Resources["EfironSecondaryButtonStyle"],
        };
        AutomationProperties.SetName(
            customAccentButton,
            _appearanceResources.GetString("AccentCustomAutomation"));

        _customAccentColorPicker = new ColorPicker
        {
            IsAlphaEnabled = false,
            Color = AppearanceManager.ColorFromArgb(
                AppearanceManager.ResolveAccentArgb(_appearanceSettings)),
        };
        _customAccentColorPicker.ColorChanged += CustomAccentColorPicker_ColorChanged;
        customAccentButton.Flyout = new Flyout
        {
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = _appearanceResources.GetString("AccentCustomPickerTitle"),
                        Style = (Style)Application.Current.Resources["EfironSectionTitleTextStyle"],
                    },
                    _customAccentColorPicker,
                },
            },
        };
        accentSection.Children.Add(customAccentButton);
        cardContent.Children.Add(accentSection);
        cardContent.Children.Add(CreateAppearancePreview());

        _appearanceInfoBar = new InfoBar
        {
            IsOpen = false,
            IsClosable = true,
        };
        cardContent.Children.Add(_appearanceInfoBar);

        var card = new Border
        {
            Style = (Style)Application.Current.Resources["EfironSurfaceBorderStyle"],
            Child = cardContent,
        };
        SettingsView.Children.Insert(0, card);
    }

    private FrameworkElement CreateAppearancePreview()
    {
        var previewContent = new StackPanel { Spacing = 12 };
        previewContent.Children.Add(new TextBlock
        {
            Text = _appearanceResources.GetString("PreviewTitle"),
            Style = (Style)Application.Current.Resources["EfironBodyTextStyle"],
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });

        var selectedChannel = new Border
        {
            Background = (Brush)Application.Current.Resources["EfironAccentSelectionBrush"],
            BorderBrush = (Brush)Application.Current.Resources["EfironAccentBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = new Grid
            {
                ColumnSpacing = 12,
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                },
                Children =
                {
                    new FontIcon
                    {
                        Glyph = "\uE7F4",
                        Foreground = (Brush)Application.Current.Resources["EfironAccentBrush"],
                        FontSize = 18,
                    },
                    CreatePreviewChannelText(),
                },
            },
        };
        Grid.SetColumn((FrameworkElement)((Grid)selectedChannel.Child).Children[1], 1);
        previewContent.Children.Add(selectedChannel);

        var actionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        actionRow.Children.Add(new Button
        {
            Content = _appearanceResources.GetString("PreviewPrimaryAction"),
            Style = (Style)Application.Current.Resources["EfironPrimaryButtonStyle"],
        });
        actionRow.Children.Add(CreateBadge(
            _appearanceResources.GetString("PreviewLiveBadge"),
            "EfironLiveBrush"));
        actionRow.Children.Add(CreateBadge(
            _appearanceResources.GetString("PreviewArchiveBadge"),
            "EfironArchiveBrush"));
        previewContent.Children.Add(actionRow);

        previewContent.Children.Add(new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 62,
            Style = (Style)Application.Current.Resources["EfironAccentProgressBarStyle"],
        });

        return new Border
        {
            Background = (Brush)Application.Current.Resources["EfironSubtleSurfaceBrush"],
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = previewContent,
        };
    }

    private StackPanel CreatePreviewChannelText()
    {
        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock
        {
            Text = _appearanceResources.GetString("PreviewChannelName"),
            Style = (Style)Application.Current.Resources["EfironChannelTitleTextStyle"],
        });
        text.Children.Add(new TextBlock
        {
            Text = _appearanceResources.GetString("PreviewProgrammeName"),
            Style = (Style)Application.Current.Resources["EfironCaptionTextStyle"],
        });
        return text;
    }

    private Border CreateBadge(string text, string brushKey) =>
        new()
        {
            Background = (Brush)Application.Current.Resources[brushKey],
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3, 8, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Colors.White),
                Style = (Style)Application.Current.Resources["EfironBadgeTextStyle"],
            },
        };

    private void AddThemeItem(AppThemeMode mode, string resourceKey) =>
        _themeModeComboBox.Items.Add(new ComboBoxItem
        {
            Content = _appearanceResources.GetString(resourceKey),
            Tag = mode,
        });

    private void AddAccentButton(
        Panel target,
        AppAccentMode mode,
        string resourceKey)
    {
        var previewSettings = _appearanceSettings with { AccentMode = mode };
        var color = AppearanceManager.ResolveAccentArgb(previewSettings);
        var button = new ToggleButton
        {
            Tag = mode,
            Width = 38,
            Height = 38,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(19),
            Background = new SolidColorBrush(AppearanceManager.ColorFromArgb(color)),
            BorderBrush = (Brush)Application.Current.Resources["EfironStrokeBrush"],
            BorderThickness = new Thickness(2),
        };
        button.Click += AccentButton_Click;
        ToolTipService.SetToolTip(button, _appearanceResources.GetString(resourceKey));
        AutomationProperties.SetName(button, _appearanceResources.GetString(resourceKey));
        _accentButtons[mode] = button;
        target.Children.Add(button);
    }

    private void ThemeModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingAppearanceControls ||
            _themeModeComboBox.SelectedItem is not ComboBoxItem { Tag: AppThemeMode mode })
        {
            return;
        }

        _appearanceSettings = _appearanceSettings with { ThemeMode = mode };
        ApplyAppearance(save: true);
    }

    private void AccentButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingAppearanceControls ||
            sender is not ToggleButton { Tag: AppAccentMode mode })
        {
            return;
        }

        _appearanceSettings = _appearanceSettings with { AccentMode = mode };
        ApplyAppearance(save: true);
    }

    private void CustomAccentColorPicker_ColorChanged(
        ColorPicker sender,
        ColorChangedEventArgs args)
    {
        if (_isUpdatingAppearanceControls)
        {
            return;
        }

        var color = args.NewColor;
        var customHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        _appearanceSettings = _appearanceSettings with
        {
            AccentMode = AppAccentMode.Custom,
            CustomAccentHex = customHex,
        };
        ApplyAppearance(save: true);
    }

    private void ApplyAppearance(bool save)
    {
        _appearanceSettings = _appearanceSettings.Normalize();
        AppearanceManager.Apply(RootNavigation, _appearanceSettings);
        ContentRoot.Background = (Brush)Application.Current.Resources["EfironAppBackgroundBrush"];
        UpdateAppearanceControls();

        if (save && !AppearanceSettingsStore.TrySave(_appearanceSettings))
        {
            ShowAppearanceMessage(
                InfoBarSeverity.Warning,
                _appearanceResources.GetString("StoreErrorTitle"),
                _appearanceResources.GetString("StoreErrorMessage"));
        }
    }

    private void UpdateAppearanceControls()
    {
        _isUpdatingAppearanceControls = true;
        try
        {
            for (var index = 0; index < _themeModeComboBox.Items.Count; index++)
            {
                if (_themeModeComboBox.Items[index] is ComboBoxItem { Tag: AppThemeMode mode } &&
                    mode == _appearanceSettings.ThemeMode)
                {
                    _themeModeComboBox.SelectedIndex = index;
                    break;
                }
            }

            foreach (var (mode, button) in _accentButtons)
            {
                button.IsChecked = mode == _appearanceSettings.AccentMode;
                button.Content = button.IsChecked == true
                    ? new FontIcon
                    {
                        Glyph = "\uE73E",
                        Foreground = new SolidColorBrush(
                            AppearanceManager.ColorFromArgb(
                                AccentPalette.GetReadableForegroundArgb(
                                    AppearanceManager.ResolveAccentArgb(
                                        _appearanceSettings with { AccentMode = mode })))),
                        FontSize = 15,
                    }
                    : null;
            }

            _customAccentColorPicker.Color = AppearanceManager.ColorFromArgb(
                AppearanceManager.ResolveAccentArgb(_appearanceSettings));
        }
        finally
        {
            _isUpdatingAppearanceControls = false;
        }
    }

    private void AppearanceManager_SystemColorsChanged(object? sender, EventArgs e)
    {
        if (_appearanceSettings.ThemeMode != AppThemeMode.System &&
            _appearanceSettings.AccentMode != AppAccentMode.Windows)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() => ApplyAppearance(save: false));
    }

    private void ShowAppearanceMessage(
        InfoBarSeverity severity,
        string title,
        string message)
    {
        _appearanceInfoBar.Severity = severity;
        _appearanceInfoBar.Title = title;
        _appearanceInfoBar.Message = message;
        _appearanceInfoBar.IsOpen = true;
    }

    private void AppearanceWindow_Closed(object sender, WindowEventArgs args)
    {
        AppearanceManager.SystemColorsChanged -= AppearanceManager_SystemColorsChanged;
        _themeModeComboBox.SelectionChanged -= ThemeModeComboBox_SelectionChanged;
        _customAccentColorPicker.ColorChanged -= CustomAccentColorPicker_ColorChanged;
        foreach (var button in _accentButtons.Values)
        {
            button.Click -= AccentButton_Click;
        }

        Closed -= AppearanceWindow_Closed;
    }
}
