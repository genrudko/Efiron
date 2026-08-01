using Efiron.Domain.Playback;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Efiron.Desktop.Views;

public sealed partial class LiveTvView
{
    private bool _autoBackendPolicyApplied;

    private void ApplyAutoBackendPolicyFromTemplate()
    {
        if (!TryApplyAutoBackendPolicy())
        {
            DispatcherQueue.TryEnqueue(() => TryApplyAutoBackendPolicy());
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Arrange happens after InitializePlaybackBackendController has created
        // the selectors and before the queued visible activation starts.
        TryApplyAutoBackendPolicy();
        return base.ArrangeOverride(finalSize);
    }

    private bool TryApplyAutoBackendPolicy()
    {
        if (_autoBackendPolicyApplied)
        {
            return true;
        }

        if (_playbackBackendSelector is null ||
            _playbackBackendSelector.Items.Count == 0 ||
            _playbackBackendSelector.Items[0] is not ComboBoxItem autoOption)
        {
            return false;
        }

        // The user-visible option remains "Automatic", while its effective
        // backend is mpv. On the provider HEVC 4K sample mpv produced exact
        // 50 fps with zero decoder/VO drops and materially lower Video Codec
        // load than LibVLC. LibVLC remains available as an explicit choice.
        autoOption.Tag = PlaybackBackendId.Mpv;
        _selectedPlaybackBackend = PlaybackBackendId.Mpv;
        _selectedMpvProfile = MpvPlaybackProfile.Auto;

        _updatingPlaybackBackendSelectors = true;
        _playbackBackendSelector.SelectedIndex = 0;
        if (_mpvProfileSelector is not null)
        {
            _mpvProfileSelector.SelectedIndex = 0;
        }
        _updatingPlaybackBackendSelectors = false;

        UpdateProfileSelectorVisibility();
        UpdatePlaybackBackendStatus("mpv · Auto");
        _autoBackendPolicyApplied = true;

        if (_playbackBackend is not null &&
            _playbackBackend.Id != PlaybackBackendId.Mpv)
        {
            _ = SwitchPlaybackBackendAsync(restartCurrentRequest: true);
        }

        return true;
    }
}
