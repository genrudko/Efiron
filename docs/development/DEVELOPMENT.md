# Development workflow

## Repository rules

- `main` is the accepted baseline.
- Work is performed in task branches and merged through pull requests.
- A PR must build on Windows and pass tests before merge.
- Package versions are centralised in `Directory.Packages.props`.
- User-visible strings must exist in both `ru-RU` and `en-US` resources.

## Task naming

- `BOOT-nnn`: repository and engineering foundation.
- `MEDIA-nnn`: playback engine and player surface.
- `PLAYLIST-nnn`: playlist import, identity and refresh.
- `EPG-nnn`: guide ingestion, matching and presentation.
- `ARCHIVE-nnn`: provider catch-up.
- `TIMESHIFT-nnn`: rolling local buffer.
- `RECORDING-nnn`: persistent recordings and schedules.
- `UX-nnn`: product design and interaction work.

## Local commands

```powershell
dotnet restore Efiron.sln
dotnet build Efiron.sln -c Debug -p:Platform=x64
dotnet test tests/Efiron.Core.Tests/Efiron.Core.Tests.csproj -c Debug
```

## Definition of done for the media prototype

- Build succeeds in Debug and Release x64.
- A user-entered HTTP/HTTPS stream starts without an application restart.
- Play, pause and stop are functional.
- Window resize and close do not crash.
- Both locales compile into the application resources.
