using System.Text.Json;
using Efiron.Application.Playback;
using Efiron.Domain.Playback;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Efiron.Desktop.Views;

public sealed partial class LiveTvView
{
    private const string InteractionVerificationEnvironmentVariable =
        "EFIRON_CI_INTERACTION_VERIFICATION";
    private static readonly TimeSpan PlayerChromeHideDelay = TimeSpan.FromSeconds(3.2);

    private bool _presentationPolishEnabled;
    private bool _floatingPlayerControlsConfigured;
    private bool _pointerOverPlayerControls;
    private bool _interactionEvidenceStarted;
    private DispatcherQueueTimer? _playerChromeTimer;
    private UIElement? _playerProgrammeOverlay;
    private UIElement? _playerOverlayScrim;
    private PlaybackState _playerChromePlaybackState;
    private string? _selectedCategory;

    internal void EnablePresentationPolish()
    {
        if (_presentationPolishEnabled)
        {
            return;
        }

        _presentationPolishEnabled = true;
        ResolvePlayerChromeElements();
        ConfigureFloatingPlayerControls();

        PlaybackSnapshotChanged += PresentationPolish_PlaybackSnapshotChanged;
        LiveRoot.SizeChanged += PresentationPolish_LiveRootSizeChanged;
        LiveRoot.LayoutUpdated += PresentationPolish_LiveRootLayoutUpdated;
        LiveRoot.KeyDown += PresentationPolish_LiveRootKeyDown;
        CategoryRailListView.SelectionChanged +=
            PresentationPolish_CategoryRailSelectionChanged;
        CategoryComboBox.SelectionChanged +=
            PresentationPolish_CategoryComboBoxSelectionChanged;

        PlayerSurfaceBorder.PointerEntered += PlayerSurface_PointerEntered;
        PlayerSurfaceBorder.PointerMoved += PlayerSurface_PointerMoved;
        PlayerSurfaceBorder.PointerPressed += PlayerSurface_PointerPressed;
        PlayerSurfaceBorder.PointerExited += PlayerSurface_PointerExited;
        PlayerControlsBorder.PointerEntered += PlayerControls_PointerEntered;
        PlayerControlsBorder.PointerExited += PlayerControls_PointerExited;

        _playerChromeTimer = DispatcherQueue.CreateTimer();
        _playerChromeTimer.Interval = PlayerChromeHideDelay;
        _playerChromeTimer.IsRepeating = false;
        _playerChromeTimer.Tick += PlayerChromeTimer_Tick;

        ApplyCompactChannelWidth(LiveRoot.ActualWidth);
        ShowPlayerChrome(restartAutoHide: false);
    }

    private void ResolvePlayerChromeElements()
    {
        if (PlayerSurfaceBorder.Child is not Grid playerGrid)
        {
            return;
        }

        DependencyObject current = SelectedChannelText;
        while (VisualTreeHelper.GetParent(current) is DependencyObject parent &&
               !ReferenceEquals(parent, playerGrid))
        {
            current = parent;
        }

        _playerProgrammeOverlay = current as UIElement;
        _playerOverlayScrim = playerGrid.Children
            .OfType<Grid>()
            .FirstOrDefault(grid =>
                grid.Height >= 180 &&
                grid.VerticalAlignment == VerticalAlignment.Bottom &&
                !ReferenceEquals(grid, _playerProgrammeOverlay));
    }

    private void ConfigureFloatingPlayerControls()
    {
        if (!_floatingPlayerControlsConfigured)
        {
            _floatingPlayerControlsConfigured = true;
            PlayerControlsBorder.HorizontalAlignment = HorizontalAlignment.Center;
            PlayerControlsBorder.Background = new SolidColorBrush(
                Microsoft.UI.ColorHelper.FromArgb(218, 7, 11, 17));
            PlayerControlsBorder.BorderThickness = new Thickness(0);
            PlayerControlsBorder.CornerRadius = new CornerRadius(24);
            PlayerControlsBorder.Padding = new Thickness(8, 6, 8, 6);
            PlayerControlsBorder.Margin = new Thickness(0, 0, 0, 14);

            var overlayForeground = new SolidColorBrush(
                Microsoft.UI.ColorHelper.FromArgb(255, 247, 249, 252));
            foreach (var button in PlaybackControlsGrid.Children.OfType<Button>())
            {
                button.Background = new SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(34, 255, 255, 255));
                button.BorderThickness = new Thickness(0);
                button.CornerRadius = new CornerRadius(18);
                button.Width = 36;
                button.Height = 36;
                button.Foreground = overlayForeground;

                if (button.Content is FontIcon icon)
                {
                    // The player surface is always dark, regardless of the app theme.
                    // Keep media glyphs readable when the surrounding app uses Light.
                    icon.Foreground = overlayForeground;
                }
            }

            VolumeSlider.Foreground = overlayForeground;
        }

        // Responsive layout must not turn the floating player controls back into
        // a full-width information bar.
        SelectedChannelPanel.Visibility = Visibility.Collapsed;
        SelectedInfoColumn.Width = new GridLength(0);
        VolumeColumn.Width = new GridLength(112);
    }

    private void PresentationPolish_PlaybackSnapshotChanged(
        object? sender,
        PlaybackSnapshotChangedEventArgs e)
    {
        _playerChromePlaybackState = e.Snapshot.State;
        DispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Low,
            () =>
            {
                if (e.Snapshot.State == PlaybackState.Opening)
                {
                    PlayerEmptyState.Visibility = Visibility.Collapsed;
                }

                ShowPlayerChrome(
                    restartAutoHide: e.Snapshot.State == PlaybackState.Playing);
            });
    }

    private void PresentationPolish_CategoryRailSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (CategoryRailListView.SelectedIndex < 0)
        {
            return;
        }

        _selectedCategory = CategoryRailListView.SelectedIndex == 0
            ? null
            : CategoryRailListView.SelectedItem?.ToString();

        if (CategoryComboBox.SelectedIndex != CategoryRailListView.SelectedIndex)
        {
            CategoryComboBox.SelectedIndex = CategoryRailListView.SelectedIndex;
        }

        ApplyPresentationFilters();
    }

    private void PresentationPolish_CategoryComboBoxSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isUpdatingCategory)
        {
            return;
        }

        _selectedCategory =
            (CategoryComboBox.SelectedItem as ComboBoxItem)?.Tag as string;

        if (CategoryRailListView.SelectedIndex != CategoryComboBox.SelectedIndex)
        {
            CategoryRailListView.SelectedIndex = CategoryComboBox.SelectedIndex;
        }

        ApplyPresentationFilters();
    }

    private void ApplyPresentationFilters()
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

        if (!string.IsNullOrWhiteSpace(_selectedCategory))
        {
            query = query.Where(item => string.Equals(
                item.Category,
                _selectedCategory,
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
    }

    private void PresentationPolish_LiveRootSizeChanged(
        object sender,
        SizeChangedEventArgs e) =>
        ApplyCompactChannelWidth(e.NewSize.Width);

    private void PresentationPolish_LiveRootLayoutUpdated(
        object? sender,
        object e)
    {
        ApplyCompactChannelWidth(LiveRoot.ActualWidth);
        ConfigureFloatingPlayerControls();
        TryStartInteractionEvidence();
    }

    private void TryStartInteractionEvidence()
    {
        if (_interactionEvidenceStarted ||
            !string.Equals(
                Environment.GetEnvironmentVariable(
                    InteractionVerificationEnvironmentVariable),
                "1",
                StringComparison.Ordinal) ||
            CategoryRailListView.Items.Count < 3 ||
            _allItems.Count < 2)
        {
            return;
        }

        _interactionEvidenceStarted = true;
        _ = RecordInteractionEvidenceAsync();
    }

    private async Task RecordInteractionEvidenceAsync()
    {
        try
        {
            var categoryIndex = -1;
            string? category = null;
            var expectedCount = 0;
            for (var index = 1; index < CategoryRailListView.Items.Count; index++)
            {
                var candidate = CategoryRailListView.Items[index]?.ToString();
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                var count = _allItems.Count(item => string.Equals(
                    item.Category,
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
            await Task.Yield();

            var glyphColors = PlaybackControlsGrid.Children
                .OfType<Button>()
                .Select(button => button.Content is FontIcon icon &&
                                  icon.Foreground is SolidColorBrush brush
                    ? brush.Color.ToString()
                    : string.Empty)
                .ToArray();
            var evidence = new InteractionEvidence(
                _allItems.Count,
                category,
                expectedCount,
                _visibleItems.Count,
                _selectedCategory,
                glyphColors,
                glyphColors.Length > 0 && glyphColors.All(static color =>
                    string.Equals(color, "#FFF7F9FC", StringComparison.OrdinalIgnoreCase)),
                DateTimeOffset.UtcNow);

            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Efiron",
                "diagnostics",
                "interaction-runtime.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(evidence));

            CategoryRailListView.SelectedIndex = 0;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void PresentationPolish_LiveRootKeyDown(
        object sender,
        KeyRoutedEventArgs e) =>
        ShowPlayerChrome(restartAutoHide: true);

    private void PlayerSurface_PointerEntered(
        object sender,
        PointerRoutedEventArgs e) =>
        ShowPlayerChrome(restartAutoHide: true);

    private void PlayerSurface_PointerMoved(
        object sender,
        PointerRoutedEventArgs e) =>
        ShowPlayerChrome(restartAutoHide: true);

    private void PlayerSurface_PointerPressed(
        object sender,
        PointerRoutedEventArgs e) =>
        ShowPlayerChrome(restartAutoHide: true);

    private void PlayerSurface_PointerExited(
        object sender,
        PointerRoutedEventArgs e)
    {
        _pointerOverPlayerControls = false;
        RestartPlayerChromeTimer();
    }

    private void PlayerControls_PointerEntered(
        object sender,
        PointerRoutedEventArgs e)
    {
        _pointerOverPlayerControls = true;
        _playerChromeTimer?.Stop();
    }

    private void PlayerControls_PointerExited(
        object sender,
        PointerRoutedEventArgs e)
    {
        _pointerOverPlayerControls = false;
        RestartPlayerChromeTimer();
    }

    private void PlayerChromeTimer_Tick(
        DispatcherQueueTimer sender,
        object args)
    {
        if (_playerChromePlaybackState == PlaybackState.Playing &&
            !_pointerOverPlayerControls)
        {
            SetPlayerChromeVisibility(Visibility.Collapsed);
        }
    }

    private void ShowPlayerChrome(bool restartAutoHide)
    {
        SetPlayerChromeVisibility(Visibility.Visible);
        if (restartAutoHide)
        {
            RestartPlayerChromeTimer();
        }
        else
        {
            _playerChromeTimer?.Stop();
        }
    }

    private void RestartPlayerChromeTimer()
    {
        _playerChromeTimer?.Stop();
        if (_playerChromePlaybackState == PlaybackState.Playing &&
            !_pointerOverPlayerControls)
        {
            _playerChromeTimer?.Start();
        }
    }

    private void SetPlayerChromeVisibility(Visibility visibility)
    {
        if (_playerProgrammeOverlay is not null)
        {
            _playerProgrammeOverlay.Visibility = visibility;
        }

        if (_playerOverlayScrim is not null)
        {
            _playerOverlayScrim.Visibility = visibility;
        }

        PlayerControlsBorder.Visibility = visibility;
    }

    private void ApplyCompactChannelWidth(double width)
    {
        if (!_isFullscreen && width is >= 620 and < 760)
        {
            CategoryRailColumn.Width = new GridLength(292);
            ChannelBrowserColumn.Width = new GridLength(1, GridUnitType.Star);
            PlayerColumn.Width = new GridLength(0);
        }
    }

    private sealed record InteractionEvidence(
        int AllChannelCount,
        string Category,
        int ExpectedCategoryCount,
        int VisibleCategoryCount,
        string? SelectedCategory,
        IReadOnlyList<string> OverlayGlyphColors,
        bool AllOverlayGlyphsReadable,
        DateTimeOffset RecordedAtUtc);
}
