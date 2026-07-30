using System.Text.Json;
using Efiron.Application.Appearance;
using Efiron.Desktop.Appearance;
using Efiron.Domain.Appearance;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Efiron.Desktop;

public sealed partial class MainWindow
{
    private const string AppearanceVerificationEnvironmentVariable =
        "EFIRON_CI_APPEARANCE_VERIFICATION";

    private IAppearancePreferencesStore _appearancePreferencesStore = null!;
    private AppearanceController _appearanceController = null!;
    private AppearancePreferences _appearancePreferences = AppearancePreferences.Default;
    private bool _isUpdatingAppearance;

    public MainWindow(
        IAppearancePreferencesStore appearancePreferencesStore,
        AppearancePreferences appearancePreferences)
        : this()
    {
        _appearancePreferencesStore = appearancePreferencesStore ??
            throw new ArgumentNullException(nameof(appearancePreferencesStore));
        _appearancePreferences = appearancePreferences ??
            throw new ArgumentNullException(nameof(appearancePreferences));
        _appearanceController = new AppearanceController(
            Microsoft.UI.Xaml.Application.Current.Resources,
            WindowRoot);

        PopulateAppearanceOptions();
        ApplyAppearance(_appearancePreferences);
    }

    private void PopulateAppearanceOptions()
    {
        _isUpdatingAppearance = true;
        try
        {
            ThemeComboBox.Items.Clear();
            ThemeComboBox.Items.Add(CreateOption(
                _resources.GetString("AppearanceThemeSystemMessage"),
                AppearanceTheme.System));
            ThemeComboBox.Items.Add(CreateOption(
                _resources.GetString("AppearanceThemeLightMessage"),
                AppearanceTheme.Light));
            ThemeComboBox.Items.Add(CreateOption(
                _resources.GetString("AppearanceThemeDarkMessage"),
                AppearanceTheme.Dark));

            AccentComboBox.Items.Clear();
            AccentComboBox.Items.Add(CreateOption(
                _resources.GetString("AppearanceAccentBlueMessage"),
                AccentPalette.Blue));
            AccentComboBox.Items.Add(CreateOption(
                _resources.GetString("AppearanceAccentVioletMessage"),
                AccentPalette.Violet));
            AccentComboBox.Items.Add(CreateOption(
                _resources.GetString("AppearanceAccentTealMessage"),
                AccentPalette.Teal));
            AccentComboBox.Items.Add(CreateOption(
                _resources.GetString("AppearanceAccentOrangeMessage"),
                AccentPalette.Orange));
            AccentComboBox.Items.Add(CreateOption(
                _resources.GetString("AppearanceAccentRoseMessage"),
                AccentPalette.Rose));
        }
        finally
        {
            _isUpdatingAppearance = false;
        }
    }

    private static ComboBoxItem CreateOption(string content, object value) =>
        new()
        {
            Content = content,
            Tag = value,
        };

    private void ApplyAppearance(AppearancePreferences preferences)
    {
        _isUpdatingAppearance = true;
        try
        {
            _appearancePreferences = preferences;
            _appearanceController.Apply(preferences);
            SelectOption(ThemeComboBox, preferences.Theme);
            SelectOption(AccentComboBox, preferences.Accent);
        }
        finally
        {
            _isUpdatingAppearance = false;
        }

        _ = RecordAppearanceEvidenceAsync(preferences);
    }

    private static void SelectOption(ComboBox comboBox, object value)
    {
        for (var index = 0; index < comboBox.Items.Count; index++)
        {
            if (comboBox.Items[index] is ComboBoxItem item && Equals(item.Tag, value))
            {
                comboBox.SelectedIndex = index;
                return;
            }
        }
    }

    private async void AppearanceComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isUpdatingAppearance ||
            (ThemeComboBox.SelectedItem as ComboBoxItem)?.Tag is not AppearanceTheme theme ||
            (AccentComboBox.SelectedItem as ComboBoxItem)?.Tag is not AccentPalette accent)
        {
            return;
        }

        var previous = _appearancePreferences;
        var next = new AppearancePreferences(theme, accent);
        if (next == previous)
        {
            return;
        }

        ApplyAppearance(next);
        try
        {
            await _appearancePreferencesStore.SaveAsync(next, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            ApplyAppearance(previous);
            ShowMessage(
                InfoBarSeverity.Error,
                "AppearanceSaveErrorTitle",
                "AppearanceSaveErrorMessage");
        }
    }

    private async Task RecordAppearanceEvidenceAsync(
        AppearancePreferences preferences)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(
                    AppearanceVerificationEnvironmentVariable),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await Task.Yield();
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Efiron",
                "diagnostics",
                "appearance-runtime.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var resources = Microsoft.UI.Xaml.Application.Current.Resources;
            var dictionaryKey = WindowRoot.ActualTheme == ElementTheme.Light
                ? "Light"
                : "Default";
            var accentColor =
                resources.ThemeDictionaries[dictionaryKey] is ResourceDictionary dictionary &&
                dictionary["EfironAccentBrush"] is SolidColorBrush brush
                    ? brush.Color.ToString()
                    : string.Empty;
            var evidence = new AppearanceEvidence(
                preferences.Theme.ToString(),
                WindowRoot.RequestedTheme.ToString(),
                WindowRoot.ActualTheme.ToString(),
                preferences.Accent.ToString(),
                accentColor,
                DateTimeOffset.UtcNow);
            await File.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(evidence),
                _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or KeyNotFoundException)
        {
        }
    }

    private sealed record AppearanceEvidence(
        string StoredTheme,
        string RequestedTheme,
        string ActualTheme,
        string Accent,
        string AccentColor,
        DateTimeOffset RecordedAtUtc);
}
