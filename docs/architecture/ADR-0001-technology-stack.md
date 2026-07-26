# ADR-0001: Windows technology stack

- Status: Accepted
- Date: 2026-07-26

## Context

Efiron needs native Windows integration, reliable hardware-accelerated network playback, a modern desktop UI, localisation, keyboard and media-key support, and future timeshift/recording pipelines.

## Decision

- Runtime: .NET 10 LTS, pinned through `global.json`.
- UI: WinUI 3 through Windows App SDK.
- Initial deployment: unpackaged, x64 and self-contained.
- Media engine: LibVLCSharp.WinUI with the LGPL LibVLC Windows package.
- Domain logic: UI-independent `Efiron.Core` assembly.
- Tests: xUnit v3.
- Automation: Windows GitHub Actions build and tests.

## Rationale

WinUI 3 supplies the native Windows 11 design system. LibVLC provides broad IPTV protocol and codec support without requiring Efiron to implement decoders. A separate core assembly prevents playlist, EPG, archive and timeshift rules from becoming coupled to XAML controls.

## Risks and gates

1. The WinUI video surface must be verified in both Debug and Release x64 builds.
2. Overlay controls, resizing, fullscreen and window closing require explicit tests because video surfaces have platform-specific lifecycle behaviour.
3. Local timeshift is not delegated to a pause button. It will be implemented as a separate segmented ring-buffer pipeline.
4. Server archive availability must be derived from provider capabilities, not inferred merely because an EPG event is in the past.

## Revisit conditions

Move the application shell to WPF only if the WinUI media-surface acceptance gate fails in a reproducible way and cannot be corrected without destabilising the product.
