# REWRITE-001 Repair E — runtime regressions

## Status

WIP on branch `rewrite/001-greenfield-desktop`, current documented exact head
`8389f5cf38eacb598164895366432e1c5e6d6fa0`. No Windows candidate is approved
from this repair yet.

The Repair D candidate at exact head
`36770234ae3b7a096c0ac7a9b616491abddf26ae` was rejected on a real Windows
system and must not be distributed as a current candidate.

## User-observed failures

1. The EPG vertical scrollbar was still not accepted as visibly fixed.
2. LibVLC playback quality was materially unchanged.
3. mpv decoded the stream but rendered a black WinUI surface.
4. Playback caused excessive GPU contention, about 1 GiB of application RAM,
   and interrupted concurrent YouTube 1080p playback even when Efiron played
   an HD channel.
5. Cold startup took at least 40–50 seconds with saved settings.
6. Closing the main window with the X button did not terminate the process.

## Diagnostic facts

The supplied `diagnostics(2).zip` proved:

- mpv reached `Playing` for HEVC 3840×2160 at 50 FPS with D3D11VA and a nonzero
  composition swapchain, while the user still saw a black surface;
- therefore the black screen was in the `display-swapchain → SwapChainPanel`
  presentation path, not network acquisition or decoding;
- after the close/stop transition near `21:20:45 UTC`, mpv diagnostics continued
  through `21:24:18 UTC`, proving that the process/session/diagnostics lifecycle
  remained alive for more than three minutes;
- the previous `first-useful-paint` value of approximately `0.0039 ms` was
  invalid because its stopwatch started during `MainWindow` type initialization,
  not at process start;
- the EPG catalog contained 1235 channels and 27,407 projected programme blocks;
- LibVLC Auto/D3D11VA used D3D11VA and still recorded dropped frames on real
  4K sessions.

## Repair E implementation

The current WIP includes:

- a bounded coalescing diagnostics queue and 8 MiB JSONL rotation;
- process working-set/private-memory/managed-heap/thread/handle samples;
- asynchronous bounded media cleanup before the window is closed;
- a true `CloseMainWindow` CI gate that requires process exit and no post-exit
  diagnostics writes;
- mpv `vo=gpu`, BGRA8/sRGB D3D11 composition, bounded cache and explicit
  `SetSwapChain(null)` teardown;
- physical desktop pixel verification for the mpv player region;
- active-theme EPG scrollbar brushes, a dedicated 24 px column, 12 px thumb,
  44 px minimum thumb height and physical pixel contrast verification;
- EPG date projection cache reduced to two dates;
- playlist/XMLTV parsing continuation moved off the UI context;
- Live and EPG pages moved from eager XAML construction to lazy content hosts;
- process-start-to-shell telemetry replacing the invalid type-initialization
  stopwatch.

## Repair E gates

A new candidate is prohibited until all of the following pass on one exact head:

- real `CloseMainWindow` process-exit verification without `Stop-Process -Force`;
- no diagnostics writes after process exit;
- bounded/coalesced diagnostics queue and bounded JSONL files;
- process startup evidence based on `Process.StartTime`;
- lazy construction of Live and EPG workspaces;
- process working-set/private-memory samples during playback;
- mpv physical desktop pixel verification inside the Efiron player region;
- EPG scrollbar physical pixel contrast verification;
- existing fullscreen, restart, interaction, EPG, scale and candidate workflows.

PR #18 must remain open, Draft and unmerged.