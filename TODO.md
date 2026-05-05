# Image Editor Backlog

## Done (Slice 2 — Move-tool model)

- [x] Layer selection: click body to select, click empty to deselect, dotted blue bounding box with 8 handles
- [x] Drag-to-translate: move layer by dragging body; undo step pushed on mouse-up
- [x] Drag-to-resize: 8 corner/edge handles, free aspect, min 1px
- [x] Delete/Escape: Delete removes selected layer; Escape deselects
- [x] Text-tool fix: clicking existing text in Move mode selects it (no auto-activation); Enter/F2 promotes to edit mode
- [x] UI test kit: real WinForms input pipeline, screenshot capture, `--ui-capture` CLI mode
- [x] 6 bugs fixed (see [ISSUES.md](ISSUES.md))

## Done (thumbnail panel + stash perf — `6d3bc89`)

- [x] Reviewed and committed separate-agent changes: thumbnail button sizing, stash performance, `ThumbnailActionRegressionTests`

## Done (Slice 3 — annotation Move-mode + Shift-aspect resize — `7cdb845`)

- [x] Annotation Move-mode: validated correct (click-to-select, drag, delete, Escape all work via real pipeline)
- [x] 6 annotation Move-mode unit tests added (`AnnotationSelectionTests`)
- [x] Shift-to-preserve-aspect-ratio on corner resize handles
- [x] Annotation tools exercised through `--ui-capture` (arrow + rect flows)

## Next (Slice 4)

- [ ] Rotation

## Backlog — unexercised flows (wire through kit)

- [ ] ClipboardHistoryPanel thumbnail strip — click to switch items
- [ ] Censor / Straighten / Crop tools
- [ ] Color correction
- [ ] Reload / Revert / Duplicate flows
- [ ] Multi-monitor / DPI scaling
- [ ] Persistence reload across app-restart

## Feature: Paste Toolbar

When pasting content, a floating Paste toolbar appears. It shows inputs for X and Y, width and height, as well as a "scaling algorithm" dropdown that has Nearest Neighbor as well as whatever other scaling algos supported by the Graphics API. I assume linear or bilinear, no need to make it more complex than that. It also contains Cancel and OK buttons for aborting or committing the paste.

## Feature: Variable fonts

Variable fonts (OpenType 1.8+) support multiple design axes like weight, width, slant, etc. in a single font file.

### Implementation notes:

- WinForms `System.Drawing.Font` has limited variable font support
- Need to detect if selected font is a variable font (check for `fvar` table)
- Options for implementation:
  - Use `System.Windows.Media` (WPF) for font inspection
  - Use DirectWrite interop for full variable font control
  - Use a library like HarfBuzzSharp or SkiaSharp
- UI: Add a popout panel with sliders for each available axis
- Common axes: `wght` (weight), `wdth` (width), `slnt` (slant), `ital` (italic), `opsz` (optical size)
