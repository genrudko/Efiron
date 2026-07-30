# REWRITE-001 — EPG two-axis virtualization contract

## Status

This document records the accepted implementation boundary for the greenfield Efiron programme guide after real-provider testing exposed that the former dual-`ListView` composition did not scale to a catalogue of approximately 1,235 channels.

The document is architectural evidence only. It does not mark the Programme product slice as user-accepted and does not permit merging Draft PR #18.

## Rejected rendering architecture

The following composition is prohibited for the Programme screen:

- one `ListView` for the fixed channel column;
- a second `ListView` for programme rows;
- bidirectional synchronization between two independent vertical scroll viewers;
- eager creation of one XAML row for every channel;
- eager creation of programme controls outside the visible horizontal time range;
- synchronous day projection from a date-navigation event handler.

That architecture caused expensive duplicated measure/arrange passes, unstable vertical synchronization, blocked UI input during day changes and made the player appear stalled while the UI thread was occupied.

## Required rendering architecture

The Programme screen uses one logical viewport and manual two-axis virtualization.

### Vertical axis

- A single vertical offset is authoritative.
- The scrollbar range is derived from `visible channel count × row height`.
- A bounded pool of row visuals is reused.
- Only rows intersecting the viewport, plus a small overscan margin, are realized.
- Channel identity, logo, title and category are rebound when a pooled row is reused.

### Horizontal axis

- A single horizontal offset is authoritative for the header and programme bubbles.
- Only programme intervals intersecting the visible time range are realized.
- Timeline labels and tick marks are generated only for the current viewport.
- Timeline width is derived from `1,440 minutes × pixels per minute`.
- Zoom preserves the approximate centre time while changing pixels per minute.

### Day projection

- XMLTV-to-row projection runs outside the UI thread.
- A newer request cancels an obsolete date projection.
- Results are cached per date with a bounded cache.
- Selecting another day must not implicitly return to today.
- Returning to a cached day must not rebuild every row or programme control.

## Presentation invariants

- Programme bubbles remain visually separated by a clear horizontal gap.
- Titles may wrap to a maximum of three lines when the bubble permits it.
- The current programme uses an accent border and surface.
- Channel rows use subtle alternating surfaces without full-width fence-like borders.
- Timeline ticks are lightweight and adapt their interval to zoom.
- Search is debounced and must not rebuild the visual tree for every keystroke.

## Runtime evidence contract

The provider-scale gate uses 1,500 channels and 4,500 programme entries. It must prove all of the following:

- the process remains responsive during initial projection;
- the number of realized row visuals is bounded and substantially lower than the catalogue size;
- vertical scrolling changes the authoritative offset and reuses the row pool;
- timeline zoom changes pixels per minute and timeline width;
- switching to another day completes without blocking the UI indefinitely;
- returning to the original day uses the cached projection successfully;
- working-set memory remains within the defined gate limit;
- the application remains alive after the interaction sequence.

The exact-head implementation preceding this document demonstrated six simultaneously realized rows for a 1,500-channel catalogue, successful vertical scrolling and zoom, a day-switch-and-return sequence measured in tens of milliseconds, zero unresponsive process samples and a working set of roughly 240 MiB in the deterministic fixture.

## Fullscreen relationship

Programme rendering and video fullscreen are independent contracts. Fullscreen video uses the real WinUI `FullScreen` presenter and applies LibVLC crop geometry matching the player surface when entering fullscreen. This intentionally fills the display and may crop video edges. Leaving fullscreen clears the crop geometry and returns to normal aspect-fit behaviour.

## Merge gate

Draft PR #18 remains unmergeable by product decision until real-provider Windows testing confirms:

- smooth initial Programme opening;
- working vertical scrolling;
- acceptable horizontal scrolling and zoom;
- responsive day switching;
- acceptable visual density;
- fullscreen fill behaviour on actual provider streams.
