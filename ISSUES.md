# Screenzap — Known Issues & Bug Tracking

## Fixed (surfaced by UI test kit, May 2026)

| # | Bug | Commit | Fix |
|---|-----|--------|-----|
| 1 | Phantom marching-ants rectangle stuck at paste origin after layer moved | `20aaa2f` | Don't set image-region Selection on paste |
| 2 | Commit silently cleared undo stack — host writes flattened image to clipboard, editor's own observer treats it as external change and calls `LoadImage` (clears `undoStack`) | `20aaa2f` | Host informs editor of internal write via signature (`TrackHostClipboardImageWrite`) |
| 3 | Undo across commit double-rendered the layer — drag/resize steps had `ReplacesImage=false` and didn't snapshot the base bitmap; after commit baked the layer into the base, undoing a pre-commit drag showed the layer at the pre-drag position on top of the baked post-drag base | `20aaa2f` | Layer-only steps now clone the base bitmap, `ReplacesImage=true` |
| 4 | Multi-layer click: handle hit on selected layer ate body-clicks on other layers — a click on a different layer's body that overlapped the selected layer's handle tolerance zone resized the selected layer instead of switching selection | `20aaa2f` | Body hit on a different layer wins over edge-handle hit on currently selected layer; handles still win for clicks that miss every body |
| 5 | Second Escape from text tool didn't deactivate it — `HandleTextToolKeyDown`'s object-selection Escape branch consumed the keystroke and returned early, shadowing the form-level handler that sets `isTextToolActive=false` | `45a3e43` | Object-selection Escape branch now also deactivates the tool |
| 6 | Text annotation remained selected after clicking empty canvas (discovered same capture run as #5) — `pictureBox1_MouseDown`'s empty-click branch called `DeselectImageLayerIfAny()` but left text and shape annotation selections intact | `45a3e43` | Single deselect-all on empty-click clears layers + text + shape annotations |

## Open — Known limitations

| # | Area | Description |
|---|------|-------------|
| L1 | Layer handles | Hit-tolerance is always 8px screen-pixels regardless of zoom level — at high zoom, handles become very hard to hit |
| L2 | Layer selection | No multi-layer selection; only one layer at a time |
| L3 | Layer resize | ~~No Shift-to-preserve-aspect-ratio~~ **Fixed** — corner drags preserve aspect by default ("Lock aspect ratio" starts checked); Shift inverts the lock for the duration of a drag |
| L4 | Annotation tools | ~~Arrow/Rectangle still use click-to-create semantics; no Move-mode integration (Slice 3 work)~~ **Fixed in `7cdb845`** — click-to-select/drag/delete/Escape all work in Move mode; 6 unit tests added |
| L5 | Rendering | Handle/box rendering was verified via offscreen `Graphics` only; pixel-snapping at fractional zoom was not eyeballed in live GUI before the kit was built |

## Open — Unexercised flows (potential bugs unknown)

| # | Area | Notes |
|---|------|-------|
| U1 | Annotation drawing (Arrow, Rectangle) | Cascade was touched during Slice 2 but not driven through the real input pipeline |
| U2 | Censor / Straighten / Crop tools | Not covered by `--ui-capture` |
| U3 | Color correction | Not covered |
| U4 | ClipboardHistoryPanel thumbnail strip | Clicking thumbnails to switch items not wired through kit |
| U5 | Reload / Revert / Duplicate | Not covered |
| U6 | Multi-monitor / DPI scaling | Not covered |
| U7 | Persistence reload across app-restart | Not covered |
| U8 | Unstaged changes from separate agent | `ClipboardHistoryItem.cs`, `ClipboardHistoryPanel.cs`, `ImageEditor.cs`, `ThumbnailActionRegressionTests.cs` — review before continuing |
