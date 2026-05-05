# Screenzap Agent Session Summary

This file is a handoff document so a fresh agent can continue the work without
replaying the entire conversation. Read [TODO.md](TODO.md) for the live backlog
and [ISSUES.md](ISSUES.md) for the bug ledger.

## What this work is about

Building **composer mode** for Screenzap (clipboard utility / image editor).
The user's vision: the editor should feel like a Figma-style canvas all along.
Pasting an image creates a non-destructive "smart layer" that can be moved,
resized, masked, and only flattens at commit time. Same model unifies layers,
annotations, and text annotations under a single Move tool.

Slicing:
- **Slice 1**: Paste-as-layer + non-destructive composite + commit-flattens
  (commit `81cb325`). Includes a related bug fix in `MarkClean` that was
  nulling `UndoSnapshot`.
- **Slice 2**: Move-tool model — layer selection, drag-translate, 8 resize
  handles, Escape/Delete, plus the text-tool fix so clicking existing text in
  Move mode just selects (commit `8bb4935`).
- **UI Test Kit** (commit `9ba84be`): drives real `MouseEventArgs`/`KeyEventArgs`
  through actual handlers + screenshot capture. Found 6 visual bugs that the
  unit tests had missed.
- **Bug fixes from the kit**: commits `20aaa2f` (bugs 1–4) and `45a3e43`
  (bugs 5–6). Multi-commit-cycle capture in `3e79666`.

## Current state (when handing off)

**Unstaged changes from a separate (cheaper) agent** — applied while I was
running. Reviewed and accepted:

| File | Change |
|------|--------|
| `screenzap/Components/ClipboardHistoryItem.cs` | Stores `lastThumbMaxWidth/Height` so no-arg `RebuildThumbnail()` (called from `SetPreviewComposite` during stash) uses the panel's current size instead of the hardcoded 64×64. Fixes shrunk thumbnails at non-100% DPI. |
| `screenzap/Components/ClipboardHistoryPanel.cs` | Tracks `lastActiveItemId` so active-item swap only rebinds the previous + new active buttons (not all). Adds `Rebind(item, active, maxW, maxH)` overload that sizes buttons from the panel's thumbnail config rather than the bitmap dimensions (fixes shrinking buttons for tall/short images). Adds double-buffering on the flow panel. Centers thumbnail within button. |
| `screenzap/Components/ImageEditor.cs` | `StashHistoryItemState` now skips `BuildCompositeImage` when there are no annotations or layers (perf). |
| `tests/Screenzap.ViewportTests/ThumbnailActionRegressionTests.cs` | New test `SwitchingActiveItem_DoesNotMutateExistingThumbnailButtonSizes`. |

These changes should be reviewed for correctness (they look good) and then
build + tests run before committing.

## File map (where things live)

```
screenzap/
  Program.cs                                  # entry point, --editor-harness, --ui-capture flags
  Components/
    ImageEditor.cs                            # main editor partial (paste, undo, lifecycle)
    ImageEditor.Annotations.cs                # arrow / rectangle annotation shapes
    ImageEditor.Designer.cs                   # WinForms designer (do not hand-edit)
    ImageEditor.CensorTool.cs                 # PushUndoStep + ApplyImageUndoStep live here
    ImageEditor.ColorCorrector.cs
    ImageEditor.Layers.cs                     # ImageLayer state, hit-test, render, undo step plumbing
    ImageEditor.Selection.cs                  # mouse cascade + paint pass + rubber-band selection
    ImageEditor.StraightenTool.cs
    ImageEditor.TextTool.cs                   # text annotation creation + Move-mode selection
    ImageEditor.TestInput.cs                  # internal partial: real input firing, screenshots
    ClipboardEditorHostForm.cs                # host form: history, commit, deletion, navigation
    ClipboardHistoryItem.cs                   # one history entry; owns Original/Committed/Current bitmaps + UndoSnapshot
    ClipboardHistoryPanel.cs                  # left-side thumbnail strip
    ClipboardHistoryStore.cs                  # in-memory items list + events
    ClipboardHistoryPersistence.cs            # JSON manifest under %LOCALAPPDATA%/Screenzap
    Shared/IClipboardDocumentPresenter.cs     # presenter interface (image vs text)
  lib/
    UndoRedo.cs                               # ImageUndoStep, TextAnnotationUndoStep, Snapshot
    ImageLayer.cs                             # smart-object data type (Source, Frame, Fill, RotationDeg, Mask)
    Logger.cs                                 # writes to %APPDATA%/Screenzap/screenzap.log
  Testing/
    EditorHarness.cs                          # programmatic --editor-harness validations
    UiTestKit.cs                              # high-level driver (Click, Drag, Type, CaptureForm)
    UiCaptureSession.cs                       # --ui-capture mode, dumps screenshots
shared/
  Screenzap.Components.Shared/
    ImageViewportControl.cs                   # the PictureBox subclass (zoom, pan, ZoomLevel, PanOffset, Metrics)
tests/
  Screenzap.ViewportTests/                    # xUnit tests (45 currently passing)
    StaTest.cs                                # STA test harness wrapper
    ImageLayerPasteTests.cs                   # slice 1 regression tests
    ImageLayerSelectionTests.cs               # slice 2 regression tests
    ClipboardHistoryRegressionTests.cs        # MarkClean preserves UndoSnapshot
    ImageEditorReloadTests.cs
    ImageEditorCropTests.cs
    ImageEditorViewportRegressionTests.cs
    ThumbnailActionRegressionTests.cs
    TextToolRegressionTests.cs
    TextOutlinePerfSmokeTests.cs
    ReplaceBackgroundInterpolationTests.cs
    UndoRedoTests.cs
    ViewportMetricsTests.cs
```

## Architecture notes

### Layer model

`ImageLayer` lives in `screenzap/lib/ImageLayer.cs`:
```csharp
internal sealed class ImageLayer : IDisposable {
    public Bitmap Source { get; }      // never mutated
    public RectangleF Frame { get; set; }       // image-space bounds on canvas
    public RectangleF Fill { get; set; }        // sub-rect of Source that fills Frame
    public float RotationDeg { get; set; }      // not yet wired to UI (slice 4)
    public Bitmap? Mask { get; }                // not yet wired (slice 5)
}
```

`imageLayers` lives on `ImageEditor` (in `ImageEditor.Layers.cs`). Render pass:
1. `pictureBox1.Image` (base bitmap) — drawn natively by PictureBox
2. `DrawImageLayers` — drawn in `pictureBox1_Paint` for screen, in
   `BuildCompositeImage` for the flattened export
3. `DrawAnnotations` / `DrawTextAnnotations` — drawn after layers
4. `DrawSelectedLayerOverlay` — bounding box + 8 handles for selected layer
5. Tool overlays (straighten, censor)

### Coordinate systems

- **Image pixel** (`Point` in image-space): what `Frame.X/Y` are.
- **Client pixel** (`Point` in form coordinates): what `MouseEventArgs.Location` is.
- **Form pixel**: where `DrawToBitmap` captures.

Conversions:
- `pictureBox1.PixelToClient(imagePixel)` → image → client
- `pictureBox1.ClientToPixel(clientPoint)` → client → image
- screen coord = `pan + image_pixel * zoom`

### Undo step model

`ImageUndoStep` (in `screenzap/lib/UndoRedo.cs`) carries optional snapshots:
- `Before` / `After` bitmaps + `ReplacesImage` flag (whole-base replacement)
- `Region` + smaller bitmaps (partial-region patch)
- `ShapesBefore` / `ShapesAfter` (annotation shapes)
- `TextsBefore` / `TextsAfter`
- `LayersBefore` / `LayersAfter` (added in slice 1)

`PushUndoStep` is in `ImageEditor.CensorTool.cs` (historical).
`ApplyImageUndoStep` is also there — handles all step variants.

**Important**: layer-only steps (drag, resize, delete) now ALSO clone the
base bitmap with `ReplacesImage=true` so undo across a commit-flatten
restores the unflattened baseline (Bug 3 fix).

### Commit pipeline

`ClipboardEditorHostForm.CommitActiveItemEdits` (line ~494):
1. `activePresenter.StashHistoryItemState(item)` — moves editor state to item
   (annotations, text, layers, undo snapshot, current image)
2. `activePresenter.GetCurrentContent()` → `BuildCompositeImage()` → flattened
3. `BeginInternalClipboardWrite()` + `editor.TrackHostClipboardImageWrite(flattened)`
   — both suppression flags must be set, otherwise editor's own clipboard
   observer auto-reloads the image and clobbers the undo stack (Bug 2)
4. `Clipboard.SetImage(flattened)`
5. `item.UpdateCurrentImage(flattened)` — bakes flattened as new CurrentImage
6. `historyStore.MarkClean(item)` — sets `CommittedImage = CurrentImage`,
   nulls `Annotations`/`TextAnnotations`/`ImageLayers`, **preserves
   `UndoSnapshot`**
7. `activePresenter.LoadHistoryItem(item)` — `LoadImage` clears editor state,
   `RestoreState` rehydrates the undo stack from the snapshot

### Move-tool semantics (the user's "Figma model")

All in `ImageEditor.Selection.cs` in `pictureBox1_MouseDown`:
1. Active tool (Straighten / Censor / Text-explicit / Annotation-explicit)
   handles cascade first.
2. Otherwise (Move mode): `TryBeginLayerInteraction(cursorPixel)` — body hit
   on different layer wins, then handle hit on selected layer, then body hit
   on selected layer.
3. Click on empty canvas: deselect all (layer + text + shape annotation),
   fall through to rubber-band/resize/pan.
4. Click on text annotation in Move mode: `HandleTextToolMouseDown` selects
   without auto-activating the text tool. Enter/F2 promotes to edit + activates
   the tool. Escape twice (one for edit, one for tool) returns to Move.

### Build / test commands

```pwsh
# Build (Windows; requires .NET 8 SDK)
dotnet build screenzap/Screenzap.csproj

# Run all tests (45 currently passing)
dotnet test tests/Screenzap.ViewportTests/Screenzap.ViewportTests.csproj --nologo

# Programmatic harness (smoke validation)
dotnet run --project screenzap/Screenzap.csproj -- --editor-harness

# Visual capture: dumps screenshots to %TEMP%/screenzap-uitests
dotnet run --project screenzap/Screenzap.csproj -- --ui-capture
```

Logs land at `%APPDATA%/Screenzap/screenzap.log`.

## UI test kit usage

```csharp
using var kit = new UiTestKit(new Size(800, 600), withHost: true, visible: false);
kit.LoadCanvas(96, 64, Color.White);

using var pasted = MakeBitmap(20, 14, Color.Magenta);
kit.PasteImage(pasted);            // primes internal clipboard, fires Ctrl+V

var f = kit.Editor.GetImageLayerFrameForTests(0);
var center = new Point((int)(f.X + f.Width / 2), (int)(f.Y + f.Height / 2));
kit.Click(center);
kit.Drag(center, new Point(center.X + 18, center.Y + 10));
kit.Press(Keys.Escape);
kit.Press(Keys.Control | Keys.Z);

kit.SaveScreenshot("debug-frame");  // writes PNG to %TEMP%/screenzap-uitests
```

The form is positioned offscreen at `(-32000, -32000)` and shown so that
`DrawToBitmap` renders correctly (a hidden PictureBox doesn't paint its
`Image` content). It never appears on the user's monitors.

To inspect a screenshot in conversation, use the `Read` tool on the PNG path.
For pixel-precise verification, use PowerShell + `System.Drawing.Bitmap`.

## Bugs found and fixed (full ledger in [ISSUES.md](ISSUES.md))

| # | Bug | Fix |
|---|-----|-----|
| 1 | Phantom marching-ants rectangle stuck at paste origin after layer moved | Don't set image-region `Selection` on paste |
| 2 | Commit silently emptied undo stack via clipboard auto-reload | Host informs editor of internal clipboard write |
| 3 | Undo across commit double-rendered the layer (drag step lacked base bitmap) | Layer-only steps now clone base, `ReplacesImage=true` |
| 4 | Multi-layer click priority (handle ate body click on different layer) | Body hit on different layer wins over edge-handle on selected |
| 5 | Second Escape from text tool didn't exit the tool | Object-selection Escape branch deactivates tool |
| 6 | Empty-click in Move mode only deselected layers | Single deselect-all on empty-click |

## Key gotchas (lessons learned)

- **PictureBox `DrawToBitmap` returns empty pixels for the `Image` content if
  the form isn't visible.** Show the form (offscreen) before capturing.
- **Layer-only undo steps need to clone the base bitmap** even though the
  step itself doesn't change the base — otherwise undo across a commit-flatten
  produces double-renders (Bug 3).
- **The editor and the host both have suppression mechanisms for clipboard
  writes during commit; both must fire.** The editor uses signature-based
  matching (`expectedInternalClipboardSignature`); the host uses a time
  window. Bug 2 was the editor's flag not getting set.
- **Internal access** is enabled via `[InternalsVisibleTo("Screenzap.ViewportTests")]`
  in `screenzap/Properties/AssemblyInfo.cs` — safe to add new internal
  diagnostic helpers.
- **Tests touching the system clipboard collide when xUnit runs them in
  parallel.** Use `editor.SetInternalClipboardImageForDiagnostics` to prime
  the editor's internal clipboard rather than `Clipboard.SetImage`.
- **STA threading** — WinForms must be on STA. Use `StaTest.Run(...)` from
  test code; the harness uses `[STAThread]` on `Main`.
- **Don't trust visual eyeballing of multimodal-rendered screenshots.**
  Sample pixels with `System.Drawing.Bitmap.GetPixel` to verify positions.

## Where to go next (per [TODO.md](TODO.md))

The big-picture vision (Figma-style canvas) is not yet complete. Slice 3
candidates: annotation Move-mode integration (Arrow/Rectangle click selects
instead of creating new), Shift-to-preserve-aspect-ratio on layer resize,
exercising annotation tools through the kit. Slice 4: rotation. Slice 5: layer
masks (paint alpha into a per-layer mask).

When debugging anything UI-related, first thing to do: add a flow to
`UiCaptureSession.cs`, run `--ui-capture`, and inspect the PNGs. Don't trust
unit tests that bypass `pictureBox1_MouseDown` — they will miss visual bugs.
