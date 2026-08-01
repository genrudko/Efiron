using Efiron.Domain.Playback;
using Efiron.Playback;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Efiron.Desktop.Views;

public sealed partial class LiveTvView
{
    private bool _autoBackendPolicyApplied;
    private MpvPlaybackBackend? _fullscreenFillBackend;
    private bool? _fullscreenFillApplied;

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
        var arranged = base.ArrangeOverride(finalSize);
        ApplyMpvFullscreenFill();
        return arranged;
    }

    private void ApplyMpvFullscreenFill()
    {
        if (_playbackBackend is not MpvPlaybackBackend mpv)
        {
            _fullscreenFillBackend = null;
            _fullscreenFillApplied = null;
            return;
        }

        if (ReferenceEquals(_fullscreenFillBackend, mpv) &&
            _fullscreenFillApplied == _isFullscreen)
        {
            return;
        }

        mpv.SetFullscreenVideoFill(_isFullscreen);
        _fullscreenFillBackend = mpv;
        _fullscreenFillApplied = _isFullscreen;
        _playbackDiagnosticsWriter.RequestRecord();
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

        // Automatic remains on the existing in-process mpv path as the
        // control/CI baseline during Repair J. The out-of-process native-window
        // host stays an explicit experiment until physical Windows validation.
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
