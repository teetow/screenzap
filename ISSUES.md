# Screenzap — Known Issues & Bug Tracking

## Changed (revert undoability, July 2026)

Covered by `RevertUndoRegressionTests` (5 tests, incl. the real host command pipeline):

- **Revert to original is now undoable** — `RevertToOriginal` appends an image-replacing undo step (pre-revert base + annotations/texts/layers) to `UndoSnapshot` instead of nulling it. Ctrl+Z after a revert restores the edits; redo re-applies the revert. Completes the design that made commit preserve the undo stack (bugs #2/#3).
- **Both revert entry points stash live editor state first** — the toolbar command and the thumbnail context-menu revert now capture the presenter's latest edits before reverting (the context-menu path previously reverted the active item from stale stashed state).
- **Undo/redo now notify the host** — `ApplyUndoStep` fires `MarkDirtyAndNotify`, so undoing (e.g. across a revert or commit) re-dirties the item, re-enables Commit/Revert, and refreshes the thumbnail. Previously undo left the item "clean" with stale command states.

## Changed (tool-mode hardening phase 2, July 2026)

Interaction-model unification, covered by `ToolModeRegressionTests` (15 tests):

- **Move/Select is a first-class rail tool** — new button at the top of the vertical rail, checked whenever `ActiveTool.None`; clicking it cancels whatever tool/modal is engaged.
- **Rail icons toggle uniformly** — clicking a checked icon deactivates it; for censor/straighten that means cancel-without-apply (same as Esc). Supersedes the "second click is a no-op" behavior; programmatic `ActivateStraightenTool` remains idempotent (no reference-line reset, bug #7 below).
- **Unified Escape ladder** — each press steps out ONE level: in-flight gesture → selection (both shape+text at once) → active tool → nothing. Text object-mode Escape no longer short-circuits in `HandleTextToolKeyDown`.
- **Gesture rule for armed drawing tools (Blender-style)** — DRAG draws (even over existing elements; the rectangle-interior deadzone is gone), CLICK selects the element under the cursor (text > shape > layer) and drops the tool; click on empty canvas keeps the tool armed. Selection handles are inert and hidden while a tool is armed.

## Fixed (tool-mode hardening, July 2026)

Covered by `ToolModeRegressionTests` (8 tests, real input pipeline).

| # | Bug | Fix |
|---|-----|-----|
| 7 | Re-clicking the straighten rail button (or Ctrl+L) while the tool was active silently wiped the drawn reference line — `ActivateStraightenTool` unconditionally reset `straightenLine*` | Activation is now idempotent: early-return `true` when already active |
| 8 | Ctrl+A in censor mode was dead code — the censor modal key block in `ImageEditor_KeyDown` returned before the later `Keys.A` branch that called `selectAllToolStripButton_Click` | Ctrl+A handled inside the modal block; dead branch removed |
| 9 | Modal tools didn't own the keyboard: `HandleTextToolKeyDown` ran before the straighten/censor blocks, so Delete during straighten/censor mode deleted a text annotation still selected under the modal overlay | Straighten/censor key blocks moved ahead of the multi-delete and text handlers |
| 10 | Plain click on a shape left a previously selected text annotation in `selectedTexts` (and vice versa); the next drag moved the stale item along (`TranslateSelectionBy` walks both lists) | Plain-click replace now clears BOTH selection lists, mirroring `SelectImageLayer`'s cross-type exclusivity |
| 11 | Clicking a text annotation with a drawing tool armed selected the text but left the tool armed — next canvas click drew a surprise shape (shape clicks already dropped the tool) | Text hit in `HandleTextToolMouseDown` now drops the drawing tool, same rule as `HandleAnnotationMouseDown` |
| 12 | Censor and straighten rail buttons never showed a Checked state — no visual indication the editor was in a modal mode | `Checked` now tracks activate/deactivate for both tools |

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
