# REWRITE-001 — Greenfield Efiron Architecture

**Status:** BINDING  
**Base:** `main@0fb8b5a54ea6b1fd6243b784fccc85af624f5fa8`  
**Supersedes:** LIVE-TV-002 / PR #16 presentation implementation  
**Issue:** #17

## 1. Decision

Efiron is redeveloped as a new desktop/application product. The legacy `Efiron.App` remains only as historical source material while the greenfield implementation is built and validated. It is not a base class, presentation host, compatibility runtime or service provider for the new application.

The rewrite boundary covers:

- application composition;
- navigation and screen lifecycle;
- presentation state;
- first-run and source management;
- Live TV, EPG, Channels and Settings screens;
- player integration boundary;
- startup and user-visible readiness evidence.

Headless algorithms may be ported or reused only through the whitelist process below.

## 2. New dependency graph

```text
Efiron.Desktop
  -> Efiron.Application
  -> Efiron.Domain

Efiron.Infrastructure
  -> Efiron.Application
  -> Efiron.Domain

Efiron.Playback
  -> Efiron.Application
  -> Efiron.Domain
```

Rules:

1. `Efiron.Domain` references no other Efiron project and no UI framework.
2. `Efiron.Application` references only `Efiron.Domain`.
3. `Efiron.Infrastructure` implements application ports and may reference `Efiron.Core` only after a reuse audit.
4. `Efiron.Playback` implements the playback port and owns LibVLC-specific types.
5. `Efiron.Desktop` references application contracts and adapter composition, never `Efiron.App`.
6. No new project may reference `Efiron.App`.

## 3. Legacy exclusion

The following patterns are prohibited in the greenfield projects:

- project reference to `src/Efiron.App/Efiron.App.csproj`;
- namespace dependency on `Efiron.App`;
- hidden controls used as application state or command backends;
- `CompatibilityBridge` or equivalent invisible visual tree;
- calling event handlers such as `LoadPlaylistButton_Click` from application code;
- copying `MainWindow.xaml`, old screen XAML or old code-behind into the new project;
- disabled navigation items representing screens that do not exist;
- publishing an old and new presentation tree together.

Architecture tests enforce the project-reference and source-token parts of this rule. Runtime review enforces the behavioral parts.

## 4. Reuse whitelist process

A legacy component may enter the new graph only when a short audit records:

- source path and exact commit;
- dependency check proving no WinUI or `MainWindow` coupling;
- public contract used by the new application;
- existing tests or new characterization tests;
- decision: `PORT`, `WRAP`, `REIMPLEMENT`, or `REJECT`.

Initial audit candidates:

- M3U parsing;
- XMLTV parsing;
- EPG matching and Now/Next indexing;
- stable channel identity;
- headless channel presentation rules;
- LibVLC primitives behind a new playback port.

No PR #16 presentation file is eligible for reuse.

## 5. Product-state contract

Screens render immutable application state and send intents to application services. UI controls do not own source configuration, channel catalogs, playback truth or EPG truth.

The first vertical slice uses these state families:

- `SourceConfigurationState`;
- `SourceRefreshState`;
- `ChannelBrowserState`;
- `PlaybackState`;
- `NowNextState`;
- `AppearanceState`.

Each state must represent empty, loading, ready and failure conditions explicitly where applicable.

## 6. First acceptance gate

No user acceptance artifact is published until one build supports the complete path:

```text
first useful paint
→ configure M3U/XMLTV
→ persist sources
→ refresh sources
→ show categories and channels
→ play a channel
→ show Now/Next
→ favorite a channel
→ pause/stop/mute/volume/fullscreen
→ restart with restored configuration
```

The UI must visibly follow the approved Efiron concept. A process-alive smoke test alone is not acceptance evidence.

## 7. CI gates

REWRITE-001 CI must prove:

- Domain and Application compile independently;
- architecture tests reject any `Efiron.App` reference or legacy bridge token in greenfield source;
- the new Desktop project publishes independently of `Efiron.App` once introduced;
- startup evidence measures first useful paint, not only process lifetime;
- an acceptance artifact is uploaded only after functional smoke gates exist and pass.

## 8. Migration and retirement

The legacy application remains buildable during early development only to preserve `main` stability and enable controlled audits. It is removed from the release solution and workflow before the first REWRITE-001 user candidate. It is deleted or moved to an explicit archive only after the greenfield vertical slice is accepted.
