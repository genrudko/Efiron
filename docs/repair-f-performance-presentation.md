# Repair F — real Windows performance and presentation

Repair E exact head `cab3c2ab4d6cee986dc32cbbbda39115b0d8eae1` was rejected after real Windows validation.

Confirmed on the user's Windows 11 workstation with AMD RX 6700 XT:

- real cold readiness with saved playlist/XMLTV remained about 40 seconds;
- playback working set remained about 900 MiB;
- GPU load remained about 80–90% for both HD and 4K;
- concurrent YouTube 1080p playback still stalled;
- mpv rendered SD/HD/4K successfully but showed the same motion judder as LibVLC;
- the light theme did not propagate into the lazy Live TV workspace;
- the EPG scrollbar repair was accepted.

Primary causes identified in code:

1. `LiveTvView.PresentationPolish` subscribed to `LiveRoot.LayoutUpdated` and rewrote XAML layout properties on every composition/layout pass. Those writes could invalidate layout again, producing a continuous common UI/render loop for every playback backend.
2. Startup always cleared the catalog, downloaded both configured sources and parsed the complete XMLTV before exposing Live TV. The previous startup gate measured shell appearance rather than source-ready time.
3. XMLTV gzip content was fully decompressed into a second in-memory buffer (up to 256 MiB), parsed element-by-element, and the complete multi-day schedule was retained in the live catalog.
4. Lazy workspaces were created without explicitly inheriting the current `RequestedTheme`, so a Light root could create a Default/Dark Live TV child.

Repair F must remove the continuous layout invalidation, propagate the active theme, introduce a bounded retained EPG window and a last-known-good catalog/source cache, and measure process-start-to-live-ready rather than shell-only startup.
