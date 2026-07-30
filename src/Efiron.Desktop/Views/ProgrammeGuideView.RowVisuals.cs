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
            Background = ResolveBrush("EfironSurfaceBrush"),
        };
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });

        var channelButton = new Button
        {
            Padding = new Thickness(12, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = ResolveBrush("EfironStrokeSubtleBrush"),
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
            Foreground = ResolveBrush("EfironTextTertiaryBrush"),
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
            Background = ResolveBrush("EfironAccentQuietBrush"),
            CornerRadius = new CornerRadius(9),
        });
        var initials = new TextBlock
        {
            Foreground = ResolveBrush("EfironAccentBrush"),
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var logo = new Image { Stretch = Stretch.Uniform };
        logoHost.Children.Add(initials);
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
            Foreground = ResolveBrush("EfironTextBrush"),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var category = new TextBlock
        {
            Foreground = ResolveBrush("EfironTextTertiaryBrush"),
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

        return new EpgRowVisual(
            root,
            channelButton,
            number,
            logo,
            initials,
            name,
            category,
            timelineClip,
            programmeCanvas);
    }

    private void UpdateRowVisual(
        EpgRowVisual visual,
        EpgChannelRowItem row,
        int rowIndex,
        double viewportWidth)
    {
        visual.Row = row;
        visual.Root.ColumnDefinitions[0].Width = new GridLength(_channelColumnWidth);
        visual.Root.Background = ResolveBrush(
            rowIndex % 2 == 0
                ? "EfironSurfaceBrush"
                : "EfironSurfaceRaisedBrush");
        visual.ChannelButton.Tag = row;
        visual.Number.Text = row.Number.ToString(CultureInfo.CurrentCulture);
        visual.Name.Text = row.Name;
        visual.Category.Text = row.Category;
        visual.Initials.Text = row.Initials;
        visual.Logo.Source = row.LogoUrl;

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
            if (absoluteLeft + absoluteWidth < visibleStart - 20 ||
                absoluteLeft > visibleEnd + 20)
            {
                continue;
            }

            var button = CreateProgrammeButton(
                block,
                Math.Max(MinimumProgrammeWidth, absoluteWidth - 6));
            Canvas.SetLeft(button, absoluteLeft - _horizontalOffset + 3);
            Canvas.SetTop(button, 6);
            visual.ProgrammeCanvas.Children.Add(button);
            _realizedProgrammeButtons[ProgrammeVisualKey.From(block)] = button;
        }
    }

    private Button CreateProgrammeButton(EpgProgrammeBlockItem block, double width)
    {
        var button = new Button
        {
            Width = width,
            Height = RowHeight - 12,
            Style = Resources["EpgProgrammeButtonStyle"] as Style,
            Tag = block,
            Background = ResolveBrush(
                block.IsCurrent
                    ? "EfironAccentSubtleBrush"
                    : "EfironAccentQuietBrush"),
            BorderBrush = ResolveBrush(
                block.IsCurrent
                    ? "EfironAccentBrush"
                    : "EfironStrokeSubtleBrush"),
            BorderThickness = block.IsCurrent
                ? new Thickness(2)
                : new Thickness(1),
        };
        button.Click += ProgrammeButton_Click;

        var content = new Grid { RowSpacing = 3 };
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
        if (block.IsCurrent)
        {
            meta.Children.Add(new Border
            {
                Padding = new Thickness(5, 1, 5, 1),
                Background = ResolveBrush("EfironAccentBrush"),
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
            Text = block.TimeText,
            Foreground = ResolveBrush("EfironTextSecondaryBrush"),
            FontSize = 10.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        content.Children.Add(meta);
        var title = new TextBlock
        {
            Text = block.Title,
            Foreground = ResolveBrush("EfironTextBrush"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 3,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Grid.SetRow(title, 1);
        content.Children.Add(title);
        button.Content = content;
        ToolTipService.SetToolTip(
            button,
            string.IsNullOrWhiteSpace(block.Description)
                ? $"{block.TimeText} · {block.Title}"
                : $"{block.TimeText} · {block.Title}\n{block.Description}");
        return button;
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
        TextBlock initials,
        TextBlock name,
        TextBlock category,
        Grid timelineClip,
        Canvas programmeCanvas)
    {
        public Grid Root { get; } = root;
        public Button ChannelButton { get; } = channelButton;
        public TextBlock Number { get; } = number;
        public Image Logo { get; } = logo;
        public TextBlock Initials { get; } = initials;
        public TextBlock Name { get; } = name;
        public TextBlock Category { get; } = category;
        public Grid TimelineClip { get; } = timelineClip;
        public Canvas ProgrammeCanvas { get; } = programmeCanvas;
        public EpgChannelRowItem? Row { get; set; }
    }
}
