# REUSE-001 — M3U parser audit

**Decision:** PORT  
**Legacy source:** `src/Efiron.Core/Playlists/M3uPlaylistParser.cs`  
**Source commit:** `main@0fb8b5a54ea6b1fd6243b784fccc85af624f5fa8`  
**Source blob:** `de6c685e931cfffd4bc4f2d750cfba7bd9ba6641`

## Dependency audit

The parser uses only BCL namespaces:

- `System.Security.Cryptography`;
- `System.Text`;
- `System.Text.RegularExpressions`.

It has no WinUI, LibVLC, `MainWindow`, control, storage or network dependency. Its output types are small headless records.

## Existing evidence

Legacy characterization tests cover:

- BOM and CRLF normalization;
- quoted attributes and comma-containing display names;
- explicit groups and catch-up metadata;
- HLS manifest rejection;
- malformed-entry warnings;
- relative stream URI resolution;
- stable identity with stable `tvg-id`;
- duplicate identity disambiguation;
- VLC/Kodi directives and inline URL options.

## Port decision

The algorithm is ported into `Efiron.Infrastructure.Playlists` behind the new `Efiron.Application.Playlists.IPlaylistParser` port.

The legacy namespace and output records are not referenced. Output maps to new Domain records. Characterization tests are copied as behavioral specifications, not as a project dependency.

## Constraints

- `Efiron.Infrastructure` must not reference `Efiron.Core` for this parser.
- Stable ID behavior remains byte-for-byte compatible for migration safety.
- Parser warnings remain non-localized domain diagnostics; user-facing localization belongs to Desktop/Application presentation.
