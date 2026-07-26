# Efiron

Efiron is a modern IPTV client for Windows 11 x64 with live playback, playlists, EPG, provider catch-up, local timeshift and recordings.

**Current status:** foundational bootstrap and media proof of concept.

## Product baseline

- Windows 11 x64 desktop application.
- Russian (`ru-RU`) and English (`en-US`) interface resources from the first commit.
- M3U/M3U8 playlists with scheduled refresh.
- XMLTV EPG with grid, channel-list and now/next views.
- Direct server-archive playback from every EPG view.
- Local timeshift and persistent recordings in later milestones.
- Provider-neutral architecture; no dependency on a single IPTV service.

## Technology

- C# / .NET 10 LTS
- WinUI 3 / Windows App SDK
- LibVLCSharp / LibVLC
- SQLite in the data milestone
- GitHub Actions on Windows

## Build

Requirements:

- Windows 11 x64
- Visual Studio 2026 with Desktop development with C++ and Windows application development workloads
- .NET SDK version from `global.json`

```powershell
dotnet restore Efiron.sln
dotnet build Efiron.sln -c Debug -p:Platform=x64
```

The app is currently configured as an unpackaged, self-contained WinUI application.

## Documentation

- `docs/product/PRODUCT_BASELINE.md`
- `docs/architecture/ADR-0001-technology-stack.md`
- `docs/development/DEVELOPMENT.md`

---

## Русский

Efiron — современный IPTV-клиент для Windows 11 x64. Приложение проектируется с поддержкой прямого эфира, нескольких плейлистов, EPG, серверного архива, локального Timeshift и записей.

Интерфейс изначально локализуется на русский и английский языки. Названия каналов и данные телепрограммы отображаются в том виде, в котором их предоставляет источник.
