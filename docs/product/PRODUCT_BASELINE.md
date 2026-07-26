# Efiron product baseline

Status: accepted initial baseline.

## Target

A polished Windows 11 x64 IPTV client suitable for daily desktop use rather than a thin VLC launcher.

## Mandatory capabilities

1. Multiple local and remote M3U/M3U8 playlists.
2. Automatic and manual playlist refresh with preservation of user metadata.
3. Multiple XMLTV and XMLTV.GZ EPG sources with scheduled refresh.
4. EPG representations:
   - time grid across channels;
   - receiver-style programme list for one channel;
   - now and next channel list.
5. Direct provider catch-up playback from every EPG representation, search result and programme details card.
6. Live playback with reconnect, audio-track and subtitle selection, aspect control and fullscreen mode.
7. Local rolling timeshift buffer.
8. Provider catch-up adapters, beginning with M3U catch-up metadata and Xtream-compatible services.
9. Manual and scheduled recordings.
10. Russian and English UI.

## Localisation contract

- Supported language tags: `ru-RU` and `en-US`.
- The first launch follows the Windows language when supported; English is the fallback.
- A user-selected language is applied before UI resources load and requires an application restart.
- Every user-facing string must come from resources. Product names and provider-delivered channel/EPG content are not translated automatically.

## Out of initial scope

- DRM-protected services.
- Android, television and web clients.
- Provider-specific reverse engineering without an explicit adapter and tests.
- Cloud accounts and synchronisation.
