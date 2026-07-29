using System.Text.Json;
using Efiron.Application.Playback;
using Efiron.Domain.Playback;
using Microsoft.UI.Xaml;

namespace Efiron.Desktop.Views;

public sealed partial class LiveTvView
{
    private long _visibilityCallbackToken;
    private bool _playbackEvidenceWritten;
    private bool _playbackEvidenceHooksAttached;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_playbackEvidenceHooksAttached)
        {
            return;
        }

        _playbackEvidenceHooksAttached = true;
        PlaybackSnapshotChanged += LiveTvView_PlaybackSnapshotChanged;
        _visibilityCallbackToken = RegisterPropertyChangedCallback(
            VisibilityProperty,
            LiveTvView_VisibilityChanged);
        Unloaded += LiveTvView_Unloaded;

        if (Visibility == Visibility.Visible)
        {
            _ = ActivateAsync();
        }
    }

    private void LiveTvView_VisibilityChanged(
        DependencyObject sender,
        DependencyProperty property)
    {
        if (Visibility == Visibility.Visible)
        {
            _ = ActivateAsync();
        }
    }

    private void LiveTvView_PlaybackSnapshotChanged(
        object? sender,
        PlaybackSnapshotChangedEventArgs e)
    {
        if (e.Snapshot.State == PlaybackState.Playing &&
            !_playbackEvidenceWritten)
        {
            _playbackEvidenceWritten = true;
            _ = RecordPlaybackEvidenceAsync(e.Snapshot);
        }
    }

    private async Task RecordPlaybackEvidenceAsync(PlaybackSnapshot snapshot)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Efiron",
                "diagnostics",
                "playback-playing.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var evidence = new PlaybackEvidence(
                snapshot.State.ToString(),
                snapshot.Source?.AbsoluteUri,
                snapshot.ChannelStableId,
                snapshot.DisplayName,
                snapshot.Volume,
                snapshot.IsMuted,
                DateTimeOffset.UtcNow);
            await File.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(evidence));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void LiveTvView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_visibilityCallbackToken != 0)
        {
            UnregisterPropertyChangedCallback(
                VisibilityProperty,
                _visibilityCallbackToken);
            _visibilityCallbackToken = 0;
        }

        PlaybackSnapshotChanged -= LiveTvView_PlaybackSnapshotChanged;
        Unloaded -= LiveTvView_Unloaded;
        _playbackEvidenceHooksAttached = false;
    }

    private sealed record PlaybackEvidence(
        string State,
        string? Source,
        string? ChannelStableId,
        string? DisplayName,
        int Volume,
        bool IsMuted,
        DateTimeOffset RecordedAtUtc);
}
