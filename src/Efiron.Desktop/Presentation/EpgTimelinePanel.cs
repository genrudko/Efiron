using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Efiron.Desktop.Presentation;

public sealed class EpgTimelinePanel : Panel
{
    private const double RowHeight = 72;

    protected override Size MeasureOverride(Size availableSize)
    {
        var desiredWidth = double.IsFinite(availableSize.Width)
            ? availableSize.Width
            : 0;

        foreach (var child in Children)
        {
            if (child is FrameworkElement { DataContext: EpgProgrammeBlockItem block })
            {
                child.Measure(new Size(block.Width + 2, RowHeight));
                desiredWidth = Math.Max(
                    desiredWidth,
                    block.Left + block.Width + 2);
            }
            else
            {
                child.Measure(new Size(0, RowHeight));
            }
        }

        return new Size(desiredWidth, RowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            if (child is FrameworkElement { DataContext: EpgProgrammeBlockItem block })
            {
                child.Arrange(new Rect(
                    block.Left,
                    0,
                    Math.Max(4, block.Width + 2),
                    RowHeight));
            }
            else
            {
                child.Arrange(new Rect(0, 0, 0, RowHeight));
            }
        }

        return finalSize;
    }
}
