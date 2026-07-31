using System.Globalization;
using Efiron.Desktop.Presentation;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Efiron.Desktop.Views;

public sealed partial class ProgrammeGuideView
{
    private EpgRowVisual CreateRowVisual()
    {
        var root = new Grid
        {
            Height = RowHeight,
            Background = ResolveEpgBrush("EfironSurfaceBrush"),
        };
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });

        var channelButton = new Button
        {
            Padding = new Thickness(12, 0, 12, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        channelButton.Click += ChannelButton_Click;

        var channelGrid = new Grid { ColumnSpacing = 10 };
        channelGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(30),
        });
        channelGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(42),
        });
        channelGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        var number = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = ResolveEpgBrush("EfironTextTertiaryBrush"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
        };
        channelGrid.Children.Add(number);

        var logoHost = new Grid
        {
            Width = 38,
            Height = 38,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(logoHost, 1);
        logoHost.Children.Add(new Border
        {
            Background = ResolveEpgBrush("EfironAccentQuietBrush"),
            CornerRadius = new CornerRadius(9),
        });
        var fallback = new TextBlock
        {
            Text = "\uE7F4",
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 17,
            Foreground = ResolveEpgBrush("EfironTextTertiaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var logo = new Image
        {
            Stretch = Stretch.Uniform,
            Opacity = 0,
        };
        logoHost.Children.Add(fallback);
        logoHost.Children.Add(logo);
        channelGrid.Children.Add(logoHost);

        var labels = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 2,
        };
        Grid.SetColumn(labels, 2);
        var name = new TextBlock
        {
            Foreground = ResolveEpgBrush("EfironTextBrush"),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var category = new TextBlock
        {
            Foreground = ResolveEpgBrush("EfironTextTertiaryBrush"),
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        labels.Children.Add(name);
        labels.Children.Add(category);
        channelGrid.Children.Add(labels);
        channelButton.Content = channelGrid;
        root.Children.Add(channelButton);

        var timelineClip = new Grid
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        Grid.SetColumn(timelineClip, 1);
        var programmeCanvas = new Canvas { Height = RowHeight };
        timelineClip.Children.Add(programmeCanvas);
        root.Children.Add(timelineClip);

        var visual = new EpgRowVisual(
            root,
            channelButton,
            number,
            logo,
            fallback,
            name,
            category,
            timelineClip,
            programmeCanvas);
        logo.ImageOpened += (_, _) => HandleRowLogoOpened(visual);
        logo.ImageFailed += (_, _) => HandleRowLogoFailed(visual);
        return visual;
    }

    private static void HandleRowLogoOpened(EpgRowVisual visual)
    {
        if (visual.RequestedLogoSource is null ||
            !ReferenceEquals(visual.Logo.Source, visual.RequestedLogoSource))
        {
            return;
        }

        visual.LogoOpened = true;
        visual.LogoFailed = false;
        visual.Logo.Opacity = 1;
        visual.LogoFallback.Visibility = Visibility.Collapsed;
    }

    private static void HandleRowLogoFailed(EpgRowVisual visual)
    {
        if (visual.RequestedLogoSource is null ||
            !ReferenceEquals(visual.Logo.Source, visual.RequestedLogoSource))
        {
            return;
        }

        visual.LogoOpened = false;
        visual.LogoFailed = true;
        visual.Logo.Opacity = 0;
        visual.LogoFallback.Visibility = Visibility.Visible;
    }

    private void UpdateRowVisual(
        EpgRowVisual visual,
        EpgChannelRowItem row,
        int rowIndex,
        double viewportWidth)
    {
        visual.Row = row;
        visual.BoundRowIndex = rowIndex;
        visual.Root.ColumnDefinitions[0].Width = new GridLength(_channelColumnWidth);
        visual.Root.Background = ResolveEpgBrush(
            rowIndex % 2 == 0
                ? "EfironSurfaceBrush"
                : "EfironSurfaceRaisedBrush");
        visual.ChannelButton.Tag = row;
        visual.Number.Text = row.Number.ToString(CultureInfo.CurrentCulture);
        visual.Name.Text = row.Name;
        visual.Category.Text = row.Category;

        var logoSource = row.LogoUrl;
        if (logoSource is null)
        {
            visual.RequestedLogoSource = null;
            visual.LogoOpened = false;
            visual.LogoFailed = false;
            visual.Logo.Opacity = 0;
            visual.Logo.Source = null;
            visual.LogoFallback.Visibility = Visibility.Visible;
        }
        else if (!ReferenceEquals(visual.RequestedLogoSource, logoSource) ||
                 !ReferenceEquals(visual.Logo.Source, logoSource))
        {
            visual.RequestedLogoSource = logoSource;
            visual.LogoOpened = false;
            visual.LogoFailed = false;
            visual.Logo.Opacity = 0;
            visual.LogoFallback.Visibility = Visibility.Collapsed;
            visual.Logo.Source = null;
            visual.Logo.Source = logoSource;
        }
        else if (visual.LogoOpened)
        {
            visual.Logo.Opacity = 1;
            visual.LogoFallback.Visibility = Visibility.Collapsed;
        }
        else
        {
            visual.Logo.Opacity = 0;
            visual.LogoFallback.Visibility = visual.LogoFailed
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        var timelineWidth = Math.Max(0, viewportWidth - _channelColumnWidth);
        visual.TimelineClip.Width = timelineWidth;
        visual.TimelineClip.Clip = new RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(0, 0, timelineWidth, RowHeight),
        };
        visual.ProgrammeCanvas.Width = timelineWidth;
        RenderProgrammes(visual, row, timelineWidth);
    }

    private void RenderProgrammes(
        EpgRowVisual visual,
        EpgChannelRowItem row,
        double timelineViewportWidth)
    {
        visual.ProgrammeCanvas.Children.Clear();
        var scale = _pixelsPerMinute / BasePixelsPerMinute;
        var visibleStart = _horizontalOffset;
        var visibleEnd = _horizontalOffset + timelineViewportWidth;

        foreach (var block in row.Programmes)
        {
            var absoluteLeft = block.Left * scale;
            var absoluteWidth = Math.Max(MinimumProgrammeWidth, block.Width * scale);
            var absoluteRight = absoluteLeft + absoluteWidth;
            if (absoluteRight <= visibleStart || absoluteLeft >= visibleEnd)
            {
                continue;
            }

            var clippedLeft = Math.Max(absoluteLeft, visibleStart);
            var clippedRight = Math.Min(absoluteRight, visibleEnd);
            var visibleWidth = clippedRight - clippedLeft;
            if (visibleWidth <= 1)
            {
                continue;
            }

            var continuesFromLeft = absoluteLeft < visibleStart;
            var continuesToRight = absoluteRight > visibleEnd;
            var button = CreateProgrammeButton(
                block,
                Math.Max(4, visibleWidth - 6),
                continuesFromLeft,
                continuesToRight);
            Canvas.SetLeft(button, clippedLeft - visibleStart + 3);
            Canvas.SetTop(button, 6);
            visual.ProgrammeCanvas.Children.Add(button);
            _realizedProgrammeButtons[ProgrammeVisualKey.From(block)] = button;
        }
    }

    private Button CreateProgrammeButton(
        EpgProgrammeBlockItem block,
        double width,
        bool continuesFromLeft,
        bool continuesToRight)
    {
        var button = new Button
        {
            Width = width,
            Height = RowHeight - 12,
            Style = Resources["EpgProgrammeButtonStyle"] as Style,
            Tag = block,
            Background = ResolveEpgBrush(
                block.IsCurrent
                    ? "EfironAccentSubtleBrush"
                    : "EfironAccentQuietBrush"),
            BorderBrush = ResolveEpgBrush(
                block.IsCurrent
                    ? "EfironAccentBrush"
                    : "EfironStrokeSubtleBrush"),
            BorderThickness = block.IsCurrent
                ? new Thickness(2)
                : new Thickness(1),
        };
        button.Click += ProgrammeButton_Click;

        var continuationPrefix = continuesFromLeft ? "← " : string.Empty;
        var continuationSuffix = continuesToRight ? " →" : string.Empty;
        if (width < 52)
        {
            button.Padding = new Thickness(0);
            button.Content = null;
        }
        else if (width < 96)
        {
            button.Padding = new Thickness(7, 5, 7, 5);
            button.Content = new TextBlock
            {
                Text = $"{continuationPrefix}{block.Title}{continuationSuffix}",
                Foreground = ResolveEpgBrush("EfironTextBrush"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 11.5,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        else
        {
            var showFullMetadata = width >= 150;
            button.Padding = showFullMetadata
                ? new Thickness(10, 7, 10, 7)
                : new Thickness(8, 6, 8, 6);

            var content = new Grid { RowSpacing = showFullMetadata ? 3 : 2 };
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star),
            });
            var meta = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
            };
            if (block.IsCurrent && showFullMetadata)
            {
                meta.Children.Add(new Border
                {
                    Padding = new Thickness(5, 1, 5, 1),
                    Background = ResolveEpgBrush("EfironAccentBrush"),
                    CornerRadius = new CornerRadius(5),
                    Child = new TextBlock
                    {
                        Text = "LIVE",
                        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                        FontSize = 9,
                        FontWeight = FontWeights.Bold,
                    },
                });
            }

            meta.Children.Add(new TextBlock
            {
                Text = $"{continuationPrefix}{block.TimeText}{continuationSuffix}",
                Foreground = ResolveEpgBrush("EfironTextSecondaryBrush"),
                FontSize = showFullMetadata ? 10.5 : 10,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            content.Children.Add(meta);
            var title = new TextBlock
            {
                Text = block.Title,
                Foreground = ResolveEpgBrush("EfironTextBrush"),
                FontWeight = FontWeights.SemiBold,
                FontSize = showFullMetadata ? 12.5 : 12,
                TextWrapping = showFullMetadata
                    ? TextWrapping.Wrap
                    : TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = showFullMetadata ? 3 : 1,
                VerticalAlignment = VerticalAlignment.Top,
            };
            Grid.SetRow(title, 1);
            content.Children.Add(title);
            button.Content = content;
        }

        ToolTipService.SetToolTip(
            button,
            string.IsNullOrWhiteSpace(block.Description)
                ? $"{block.TimeText} · {block.Title}"
                : $"{block.TimeText} · {block.Title}\n{block.Description}");
        return button;
    }

    private Brush ResolveEpgBrush(string key)
    {
        var resources = Microsoft.UI.Xaml.Application.Current.Resources;
        var dictionaryKey = ProgrammeRoot.ActualTheme == ElementTheme.Light
            ? "Light"
            : "Default";
        if (resources.ThemeDictionaries[dictionaryKey] is ResourceDictionary dictionary &&
            dictionary[key] is Brush themeBrush)
        {
            return themeBrush;
        }

        return resources[key] as Brush ??
            new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    private void ChannelButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: EpgChannelRowItem row })
        {
            PlayChannelRequested?.Invoke(
                this,
                new PlayChannelRequestedEventArgs(row.StableId));
        }
    }

    private sealed class EpgRowVisual(
        Grid root,
        Button channelButton,
        TextBlock number,
        Image logo,
        TextBlock logoFallback,
        TextBlock name,
        TextBlock category,
        Grid timelineClip,
        Canvas programmeCanvas)
    {
        public Grid Root { get; } = root;
        public Button ChannelButton { get; } = channelButton;
        public TextBlock Number { get; } = number;
        public Image Logo { get; } = logo;
        public TextBlock LogoFallback { get; } = logoFallback;
        public TextBlock Name { get; } = name;
        public TextBlock Category { get; } = category;
        public Grid TimelineClip { get; } = timelineClip;
        public Canvas ProgrammeCanvas { get; } = programmeCanvas;
        public EpgChannelRowItem? Row { get; set; }
        public int BoundRowIndex { get; set; } = -1;
        public ImageSource? RequestedLogoSource { get; set; }
        public bool LogoOpened { get; set; }
        public bool LogoFailed { get; set; }
    }
}
