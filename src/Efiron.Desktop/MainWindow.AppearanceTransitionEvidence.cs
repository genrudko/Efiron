using System.Text.Json;
using Efiron.Domain.Appearance;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Efiron.Desktop;

public sealed partial class MainWindow
{
    private const string AppearanceTransitionVerificationEnvironmentVariable =
        "EFIRON_CI_APPEARANCE_TRANSITION_VERIFICATION";

    private bool _appearanceTransitionVerificationStarted;

    private void TryStartAppearanceTransitionVerification()
    {
        if (_appearanceTransitionVerificationStarted ||
            !string.Equals(
                Environment.GetEnvironmentVariable(
                    AppearanceTransitionVerificationEnvironmentVariable),
                "1",
                StringComparison.Ordinal) ||
            _liveTvWorkspace is null)
        {
            return;
        }

        _appearanceTransitionVerificationStarted = true;
        DispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Low,
            async () => await RecordAppearanceTransitionEvidenceAsync());
    }

    private async Task RecordAppearanceTransitionEvidenceAsync()
    {
        try
        {
            var initialTheme = WindowRoot.ActualTheme.ToString();
            ApplyAppearance(new AppearancePreferences(
                AppearanceTheme.Light,
                _appearancePreferences.Accent));
            await Task.Delay(TimeSpan.FromMilliseconds(800), _lifetime.Token);

            var live = _liveTvWorkspace?.GetThemeRuntimeEvidence();
            var evidence = new
            {
                InitialTheme = initialTheme,
                WindowRequestedTheme = WindowRoot.RequestedTheme.ToString(),
                WindowActualTheme = WindowRoot.ActualTheme.ToString(),
                Live = live,
                ProcessLifetimeMilliseconds = App.ProcessLifetimeElapsed.TotalMilliseconds,
                RecordedAtUtc = DateTimeOffset.UtcNow,
            };
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Efiron",
                "diagnostics",
                "appearance-transition-runtime.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(evidence),
                _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
