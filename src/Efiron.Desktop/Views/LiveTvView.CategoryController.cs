using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Efiron.Desktop.Views;

public sealed partial class LiveTvView
{
    private bool _categoryControllerEnabled;
    private bool _categoryControllerUpdating;
    private bool _categoryControllerEvidenceStarted;
    private string _categoryControllerSignature = string.Empty;
    private string? _categoryControllerSelectedCategory;
    private readonly List<string?> _categoryControllerValues = [];

    internal void EnableCategoryController()
    {
        if (_categoryControllerEnabled)
        {
            return;
        }

        _categoryControllerEnabled = true;

        // Disable both legacy category synchronization loops. They competed for
        // SelectedIndex during real pointer interaction and could restore the
        // previous list on the next LayoutUpdated pass.
        CategoryRailListView.SelectionChanged -= CategoryRailListView_SelectionChanged;
        CategoryRailListView.SelectionChanged -= PresentationPolish_CategoryRailSelectionChanged;
        CategoryComboBox.SelectionChanged -= CategoryComboBox_SelectionChanged;
        CategoryComboBox.SelectionChanged -= PresentationPolish_CategoryComboBoxSelectionChanged;
        _categoryRailSyncing = true;

        // Replace the earlier immediate evidence path with a stability check
        // that survives multiple layout cycles.
        _interactionEvidenceStarted = true;

        CategoryRailListView.SelectionChanged += CategoryController_SelectionChanged;
        LiveRoot.LayoutUpdated += CategoryController_LayoutUpdated;

        RebuildCategoryRailIfRequired(force: true);
    }

    private void CategoryController_LayoutUpdated(object? sender, object e)
    {
        RebuildCategoryRailIfRequired(force: false);
        TryStartCategoryControllerEvidence();
    }

    private void RebuildCategoryRailIfRequired(bool force)
    {
        var options = CategoryComboBox.Items
            .OfType<ComboBoxItem>()
            .Select(static item => new CategoryControllerOption(
                item.Content?.ToString() ?? string.Empty,
                NormalizeCategory(item.Tag as string)))
            .ToArray();

        var signature = string.Join(
            '\u001F',
            options.Select(static option => $"{option.Label}\u001E{option.Value}"));
        if (!force && string.Equals(
                signature,
                _categoryControllerSignature,
                StringComparison.Ordinal))
        {
            return;
        }

        _categoryControllerSignature = signature;
        var previousCategory = _categoryControllerSelectedCategory;
        if (previousCategory is null && CategoryComboBox.SelectedItem is ComboBoxItem selected)
        {
            previousCategory = NormalizeCategory(selected.Tag as string);
        }

        _categoryControllerUpdating = true;
        try
        {
            CategoryRailListView.Items.Clear();
            _categoryControllerValues.Clear();

            foreach (var option in options)
            {
                CategoryRailListView.Items.Add(option.Label);
                _categoryControllerValues.Add(option.Value);
            }

            var selectedIndex = FindCategoryIndex(previousCategory);
            if (selectedIndex < 0 && options.Length > 0)
            {
                selectedIndex = 0;
                previousCategory = null;
            }

            _categoryControllerSelectedCategory = previousCategory;
            CategoryRailListView.SelectedIndex = selectedIndex;
            if (CategoryComboBox.SelectedIndex != selectedIndex)
            {
                CategoryComboBox.SelectedIndex = selectedIndex;
            }
        }
        finally
        {
            _categoryControllerUpdating = false;
        }

        ApplyCategoryControllerFilters();
    }

    private void CategoryController_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_categoryControllerUpdating)
        {
            return;
        }

        var selectedIndex = CategoryRailListView.SelectedIndex;
        if (selectedIndex < 0 || selectedIndex >= _categoryControllerValues.Count)
        {
            return;
        }

        _categoryControllerSelectedCategory =
            _categoryControllerValues[selectedIndex];

        _categoryControllerUpdating = true;
        try
        {
            if (CategoryComboBox.SelectedIndex != selectedIndex)
            {
                CategoryComboBox.SelectedIndex = selectedIndex;
            }
        }
        finally
        {
            _categoryControllerUpdating = false;
        }

        ApplyCategoryControllerFilters();
    }

    private void ApplyCategoryControllerFilters()
    {
        var search = ChannelSearchTextBox.Text.Trim();
        IEnumerable<Efiron.Desktop.Presentation.LiveChannelItem> query = _allItems;

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(item =>
                item.Name.Contains(
                    search,
                    StringComparison.CurrentCultureIgnoreCase) ||
                item.CurrentProgramme.Contains(
                    search,
                    StringComparison.CurrentCultureIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(_categoryControllerSelectedCategory))
        {
            query = query.Where(item => string.Equals(
                NormalizeCategory(item.Category),
                _categoryControllerSelectedCategory,
                StringComparison.CurrentCultureIgnoreCase));
        }

        if (FavoritesOnlyButton.IsChecked is true)
        {
            query = query.Where(static item => item.IsFavorite);
        }

        var filtered = query.ToArray();
        _visibleItems.Clear();
        foreach (var item in filtered)
        {
            _visibleItems.Add(item);
        }

        ChannelEmptyState.Visibility = filtered.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        ChannelListView.Visibility = filtered.Length == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        ChannelListView.SelectedItem =
            _selectedItem is not null && filtered.Contains(_selectedItem)
                ? _selectedItem
                : null;

        if (filtered.Length > 0)
        {
            ChannelListView.ScrollIntoView(filtered[0]);
        }
    }

    private int FindCategoryIndex(string? category)
    {
        for (var index = 0; index < _categoryControllerValues.Count; index++)
        {
            if (string.Equals(
                    _categoryControllerValues[index],
                    category,
                    StringComparison.CurrentCultureIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private void TryStartCategoryControllerEvidence()
    {
        if (_categoryControllerEvidenceStarted ||
            !string.Equals(
                Environment.GetEnvironmentVariable(
                    InteractionVerificationEnvironmentVariable),
                "1",
                StringComparison.Ordinal) ||
            _categoryControllerValues.Count < 3 ||
            _allItems.Count < 2)
        {
            return;
        }

        _categoryControllerEvidenceStarted = true;
        _ = RecordStableCategoryControllerEvidenceAsync();
    }

    private async Task RecordStableCategoryControllerEvidenceAsync()
    {
        try
        {
            var categoryIndex = -1;
            string? category = null;
            var expectedCount = 0;
            for (var index = 1; index < _categoryControllerValues.Count; index++)
            {
                var candidate = _categoryControllerValues[index];
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                var count = _allItems.Count(item => string.Equals(
                    NormalizeCategory(item.Category),
                    candidate,
                    StringComparison.CurrentCultureIgnoreCase));
                if (count > 0 && count < _allItems.Count)
                {
                    categoryIndex = index;
                    category = candidate;
                    expectedCount = count;
                    break;
                }
            }

            if (categoryIndex < 0 || category is null)
            {
                return;
            }

            CategoryRailListView.SelectedIndex = categoryIndex;

            // The previous false-positive read the collection immediately. Wait
            // through several real layout passes so an index-resynchronization
            // defect cannot hide behind a transient filtered state.
            await Task.Delay(TimeSpan.FromSeconds(1.5));

            var visibleItems = _visibleItems.ToArray();
            var stableSelection =
                CategoryRailListView.SelectedIndex == categoryIndex &&
                CategoryComboBox.SelectedIndex == categoryIndex &&
                string.Equals(
                    _categoryControllerSelectedCategory,
                    category,
                    StringComparison.CurrentCultureIgnoreCase);
            var stableContents =
                visibleItems.Length == expectedCount &&
                visibleItems.All(item => string.Equals(
                    NormalizeCategory(item.Category),
                    category,
                    StringComparison.CurrentCultureIgnoreCase));

            var glyphColors = PlaybackControlsGrid.Children
                .OfType<Button>()
                .Select(button => button.Content is FontIcon icon &&
                                  icon.Foreground is SolidColorBrush brush
                    ? brush.Color.ToString()
                    : string.Empty)
                .ToArray();
            var glyphsReadable =
                glyphColors.Length > 0 &&
                glyphColors.All(static color => string.Equals(
                    color,
                    "#FFF7F9FC",
                    StringComparison.OrdinalIgnoreCase));

            var evidence = new CategoryControllerEvidence(
                _allItems.Count,
                category,
                expectedCount,
                visibleItems.Length,
                _categoryControllerSelectedCategory,
                glyphColors,
                glyphsReadable,
                stableSelection,
                stableContents,
                CategoryRailListView.SelectedIndex,
                CategoryComboBox.SelectedIndex,
                DateTimeOffset.UtcNow);

            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Efiron",
                "diagnostics",
                "interaction-runtime.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(evidence));

            _categoryControllerUpdating = true;
            try
            {
                CategoryRailListView.SelectedIndex = 0;
                CategoryComboBox.SelectedIndex = 0;
                _categoryControllerSelectedCategory = null;
            }
            finally
            {
                _categoryControllerUpdating = false;
            }

            ApplyCategoryControllerFilters();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string? NormalizeCategory(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private sealed record CategoryControllerOption(
        string Label,
        string? Value);

    private sealed record CategoryControllerEvidence(
        int AllChannelCount,
        string Category,
        int ExpectedCategoryCount,
        int VisibleCategoryCount,
        string? SelectedCategory,
        IReadOnlyList<string> OverlayGlyphColors,
        bool AllOverlayGlyphsReadable,
        bool StableSelection,
        bool StableContents,
        int RailSelectedIndex,
        int ComboSelectedIndex,
        DateTimeOffset RecordedAtUtc);
}
