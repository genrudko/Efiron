# REUSE-003 — LibVLC playback

## Decision

**REIMPLEMENT THE ADAPTER; RETAIN ONLY VERIFIED BEHAVIOR.**

The greenfield application must not port `MainWindow.PlayerControls.cs`, old player XAML, control names, timers or event handlers. LibVLC remains the playback engine because the existing Windows runtime proved the basic engine viable, but it will sit behind a new application-facing session contract.

## Audited evidence

- `src/Efiron.Core/Playback/PlaybackRequest.cs`;
- `src/Efiron.App/MainWindow.xaml.cs` LibVLC initialization;
- `src/Efiron.App/MainWindow.PlayerControls.cs` event and control behavior;
- PR #8 runtime contract.

## Retained behavior

- absolute playback URI validation;
- HTTP, HTTPS, RTSP, RTMP, RTP and UDP schemes;
- Opening, Playing, Paused, Stopped, Ended and Failed states;
- play/resume, pause and stop;
- mute/unmute and volume 0–100;
- media-player events marshalled to the UI dispatcher;
- fullscreen controlled by the WinUI window presenter, not LibVLC embedded fullscreen;
- disposal of media, player and LibVLC in deterministic order.

## Explicitly excluded

- no reference to `Efiron.App`;
- no copied `MainWindow.PlayerControls.cs`;
- no player state stored in buttons, sliders or visibility values;
- no retry timer that polls for a hidden legacy media player;
- no `SourceTextBox` fallback;
- no control-specific localization inside the adapter;
- no navigation/sidebar manipulation in the playback engine.

## Greenfield boundary

- playback request and state types: `Efiron.Domain.Playback`;
- session port: `Efiron.Application.Playback`;
- LibVLC implementation and video-surface connector: `Efiron.Playback`;
- controls, keyboard mapping and fullscreen presentation: `Efiron.Desktop`.

`Efiron.Desktop` may know the concrete video-surface connector required by LibVLCSharp WinUI, but all play/pause/stop/mute/volume state and commands must flow through `IPlaybackSession`.

## Acceptance gate

The adapter is not accepted merely because a process remains alive. Internal validation must prove:

1. a real stream enters Opening and Playing;
2. pause/resume and stop update the application snapshot;
3. mute and volume round-trip through LibVLC events;
4. selecting a second channel disposes the previous media object;
5. shutdown disposes all native resources without a crash;
6. the new Live screen remains independent of legacy controls.
