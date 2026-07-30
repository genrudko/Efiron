using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Efiron.Desktop;

public sealed partial class MainWindow
{
    private void SourcesContentGrid_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 760;
        var narrow = e.NewSize.Width < 620;

        SourcesContentGrid.Margin = narrow
            ? new Thickness(14, 18, 14, 28)
            : compact
                ? new Thickness(22, 22, 22, 34)
                : new Thickness(30, 26, 30, 44);

        SourceCardsFirstColumn.Width = new GridLength(1, GridUnitType.Star);
        SourceCardsSecondColumn.Width = compact
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);
        SourceCardsSecondRow.Height = compact
            ? GridLength.Auto
            : new GridLength(0);
        Grid.SetColumn(GuideSourceCard, compact ? 0 : 1);
        Grid.SetRow(GuideSourceCard, compact ? 1 : 0);

        StorageActionColumn.Width = GridLength.Auto;
        StorageActionRow.Height = new GridLength(0);
        Grid.SetColumn(StorageActionPanel, 1);
        Grid.SetRow(StorageActionPanel, 0);
        StorageActionPanel.HorizontalAlignment = HorizontalAlignment.Right;

        AppearanceSecondColumn.Width = compact
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);
        AppearanceSecondRow.Height = compact
            ? GridLength.Auto
            : new GridLength(0);
        Grid.SetColumn(AccentOptionPanel, compact ? 0 : 1);
        Grid.SetRow(AccentOptionPanel, compact ? 1 : 0);
    }
}