# REUSE-002 — XMLTV parser

## Decision

**PORT** the headless parsing behavior into the greenfield architecture. Do not reference `Efiron.Core` from the new publish graph and do not copy any legacy application integration.

## Audited source

- `src/Efiron.Core/Epg/XmlTvParser.cs`
- `src/Efiron.Core/Epg/XmlTvChannel.cs`
- `src/Efiron.Core/Epg/XmlTvProgramme.cs`
- `tests/Efiron.Core.Tests/Epg/XmlTvParserTests.cs`

## Reusable behavior

- streaming XML parsing with `XmlReader`;
- DTD ignored and external resolution disabled;
- case-insensitive XMLTV element matching;
- channel id, display names and absolute icon URI extraction;
- programme channel/start/stop/title/subtitle/description/categories extraction;
- explicit timestamp offsets and deterministic UTC when an offset is absent;
- duplicate channel and malformed programme warnings;
- valid entries retained when unrelated entries are malformed;
- programmes sorted by start time.

## Excluded legacy behavior

- no `Efiron.App` types;
- no source controls, dialogs or click handlers;
- no persisted state from the legacy application;
- no direct dependency on `Efiron.Core`;
- no UI-facing list items.

## Greenfield target

- domain types in `Efiron.Domain.ProgrammeGuide`;
- parser port in `Efiron.Application.ProgrammeGuide`;
- implementation in `Efiron.Infrastructure.ProgrammeGuide`;
- characterization tests in `Efiron.Infrastructure.Tests`.

## Additional greenfield requirements

- accept plain XMLTV and gzip-compressed XMLTV payloads;
- impose a bounded decompressed size;
- preserve cancellation at source-loading boundaries;
- return domain data only.
