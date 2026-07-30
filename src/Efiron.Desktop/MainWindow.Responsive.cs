using Microsoft.UI.Xaml;

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
            ? new Thickness(16, 20, 16, 32)
            : compact
                ? new Thickness(26, 26, 26, 38)
                : new Thickness(42, 34, 42, 48);

        SourceCardsFirstColumn.Width = new GridLength(1, GridUnitType.Star);
        SourceCardsSecondColumn.Width = compact
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);
        SourceCardsSecondRow.Height = compact
            ? GridLength.Auto
            : new GridLength(0);
        Grid.SetColumn(GuideSourceCard, compact ? 0 : 1);
        Grid.SetRow(GuideSourceCard, compact ? 1 : 0);

        StorageActionColumn.Width = compact
            ? new GridLength(0)
            : GridLength.Auto;
        StorageActionRow.Height = compact
            ? GridLength.Auto
            : new GridLength(0);
        Grid.SetColumn(StorageActionPanel, compact ? 0 : 1);
        Grid.SetRow(StorageActionPanel, compact ? 1 : 0);
        StorageActionPanel.HorizontalAlignment = compact
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Right;

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
