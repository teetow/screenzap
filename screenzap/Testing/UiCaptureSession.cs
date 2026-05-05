using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using screenzap.lib;

namespace screenzap.Testing
{
    /// <summary>
    /// Captures screenshots of the editor at each step of the slice 1+2 user flow so a human
    /// reviewer (or claude) can inspect what the UI actually renders. No assertions; this exists
    /// to surface visual breakage that programmatic harnesses miss.
    /// </summary>
    internal static class UiCaptureSession
    {
        public static int Run()
        {
            Logger.Log("UI capture session starting...");
            var outputDir = Path.Combine(Path.GetTempPath(), "screenzap-uitests");
            Directory.CreateDirectory(outputDir);

            try
            {
                CaptureSlice1And2Flow(outputDir);
                CaptureMultiLayerFlow(outputDir);
                CaptureCommitAndUndoFlow(outputDir);
                CaptureRubberBandFlow(outputDir);
                CaptureHistorySwitchFlow(outputDir);
                CaptureTextAnnotationMoveModeFlow(outputDir);
                CaptureZoomedPasteFlow(outputDir);
                CaptureMultiCommitCycleFlow(outputDir);
                CaptureAnnotationToolFlow(outputDir);
                CaptureLayerRotationFlow(outputDir);
            }
            catch (Exception ex)
            {
                Logger.Log($"UI capture session failed: {ex}");
                return 1;
            }

            Logger.Log($"UI capture session complete. Output: {outputDir}");
            return 0;
        }

        private static void CaptureSlice1And2Flow(string outputDir)
        {
            using var kit = new UiTestKit(new Size(800, 600), withHost: true, visible: false);
            Logger.Log("State after kit ctor: " + kit.Editor.TestDescribeState());
            kit.LoadCanvas(96, 64, Color.White);
            Logger.Log("State after LoadCanvas: " + kit.Editor.TestDescribeState());
            Save(kit, outputDir, "01-canvas-loaded");
            SavePictureBoxOnly(kit, outputDir, "01b-picturebox-only");

            using var pasted = MakeBitmap(20, 14, Color.Magenta);
            kit.PasteImage(pasted);
            Logger.Log("State after paste: " + kit.Editor.TestDescribeState());
            Save(kit, outputDir, "02-after-paste");

            // Find the layer's frame and click its center to select via the real input pipeline.
            var frame = kit.Editor.GetImageLayerFrameForTests(0);
            var center = new Point(
                (int)(frame.X + frame.Width / 2),
                (int)(frame.Y + frame.Height / 2));
            kit.Click(center);
            Save(kit, outputDir, "03-clicked-center-expect-selected");

            Logger.Log($"selectedLayerIndex after click = {kit.Editor.SelectedLayerIndexForTests}");
            Logger.Log($"layer frame = {frame}");

            // Drag the body to translate.
            kit.Drag(center, new Point(center.X + 18, center.Y + 10));
            Save(kit, outputDir, "04-after-drag-translate");

            var afterDragFrame = kit.Editor.GetImageLayerFrameForTests(0);
            Logger.Log($"layer frame after drag = {afterDragFrame}");

            // Click the bottom-right resize handle and drag.
            var corner = new Point((int)afterDragFrame.Right, (int)afterDragFrame.Bottom);
            kit.Drag(corner, new Point(corner.X + 12, corner.Y + 8));
            Save(kit, outputDir, "05-after-drag-resize");

            var afterResizeFrame = kit.Editor.GetImageLayerFrameForTests(0);
            Logger.Log($"layer frame after resize = {afterResizeFrame}");

            // Press Escape to deselect.
            kit.Press(Keys.Escape);
            Save(kit, outputDir, "06-after-escape-deselect");

            // Press Ctrl+Z to undo the resize.
            kit.Press(Keys.Control | Keys.Z);
            Save(kit, outputDir, "07-after-undo-resize");

            // Click empty corner to verify deselect path through real cascade.
            kit.Click(new Point(2, 2));
            Save(kit, outputDir, "08-clicked-empty");
        }

        private static void CaptureMultiLayerFlow(string outputDir)
        {
            Logger.Log("--- Multi-layer flow ---");
            using var kit = new UiTestKit(new Size(800, 600), withHost: true, visible: false);
            kit.LoadCanvas(120, 80, Color.LightYellow);

            using var redLayer = MakeBitmap(24, 16, Color.Red);
            using var blueLayer = MakeBitmap(20, 12, Color.Blue);
            using var greenLayer = MakeBitmap(28, 10, Color.LimeGreen);

            kit.PasteImage(redLayer);
            Save(kit, outputDir, "ml-01-after-red-paste");

            kit.PasteImage(blueLayer);
            Save(kit, outputDir, "ml-02-after-blue-paste");
            Logger.Log("State after blue paste: " + kit.Editor.TestDescribeState());

            kit.PasteImage(greenLayer);
            Save(kit, outputDir, "ml-03-after-green-paste");
            Logger.Log("State after green paste: " + kit.Editor.TestDescribeState());

            // Click the red layer (which is now bottom-most). Hit-test should pick top-most by
            // default; clicking on a position that only the red layer covers should select red.
            var red = kit.Editor.GetImageLayerFrameForTests(0);
            var redOnly = new Point((int)(red.X + 2), (int)(red.Y + 2));
            kit.Click(redOnly);
            Save(kit, outputDir, "ml-04-after-click-red-only-corner");
            Logger.Log($"selected after click red corner: {kit.Editor.SelectedLayerIndexForTests}");

            // Click the center which all three overlap — top-most (green=index 2) should win.
            var green = kit.Editor.GetImageLayerFrameForTests(2);
            var greenCenter = new Point((int)(green.X + green.Width / 2), (int)(green.Y + green.Height / 2));
            kit.Click(greenCenter);
            Save(kit, outputDir, "ml-05-after-click-overlap-expect-green");
            Logger.Log($"selected after click overlap: {kit.Editor.SelectedLayerIndexForTests}");

            // Press Delete to remove the green layer.
            kit.Press(Keys.Delete);
            Save(kit, outputDir, "ml-06-after-delete-green");
            Logger.Log("State after delete: " + kit.Editor.TestDescribeState());
        }

        private static void CaptureCommitAndUndoFlow(string outputDir)
        {
            Logger.Log("--- Commit + undo flow ---");
            using var kit = new UiTestKit(new Size(800, 600), withHost: true, visible: false);
            kit.LoadCanvas(96, 64, Color.White);
            using var pasted = MakeBitmap(24, 16, Color.OrangeRed);
            kit.PasteImage(pasted);

            // Move the layer somewhere unambiguous.
            var f = kit.Editor.GetImageLayerFrameForTests(0);
            kit.Drag(
                new Point((int)(f.X + f.Width / 2), (int)(f.Y + f.Height / 2)),
                new Point((int)(f.X + f.Width / 2 - 12), (int)(f.Y + f.Height / 2 - 8)));
            Save(kit, outputDir, "co-01-before-commit");
            Logger.Log("Before commit: " + kit.Editor.TestDescribeState());

            // Commit through the host with intermediate state logging to find where undo is lost.
            var item = kit.Host!.HistoryStore.ActiveItem;
            Logger.Log($"Pre-commit: item.IsDirty={item?.IsDirty} snapshot={item?.TestDescribeUndoSnapshot()}");
            Logger.Log($"Pre-commit: editor undo stack: {kit.Editor.TestDescribeUndoStack()}");

            var committed = kit.Host.ExecuteCommandForDiagnostics(Components.Shared.EditorCommandId.CommitEdits);
            Logger.Log($"Commit returned {committed}");
            kit.PumpUi();
            Logger.Log($"Post-commit: snapshot={item?.TestDescribeUndoSnapshot()}");
            Save(kit, outputDir, "co-02-after-commit");
            Logger.Log("After commit: " + kit.Editor.TestDescribeState());
            Logger.Log("Undo stack: " + kit.Editor.TestDescribeUndoStack());

            // Undo across commit (just the drag).
            kit.Press(Keys.Control | Keys.Z);
            Save(kit, outputDir, "co-03-after-first-undo");
            Logger.Log("After 1st undo: " + kit.Editor.TestDescribeState());
            Logger.Log("Undo stack: " + kit.Editor.TestDescribeUndoStack());

            // Undo again — the paste step. Should restore unflattened base + drop layer.
            kit.Press(Keys.Control | Keys.Z);
            Save(kit, outputDir, "co-04-after-second-undo");
            Logger.Log("After 2nd undo: " + kit.Editor.TestDescribeState());
        }

        private static void CaptureRubberBandFlow(string outputDir)
        {
            Logger.Log("--- Rubber-band selection flow ---");
            using var kit = new UiTestKit(new Size(800, 600), withHost: true, visible: false);
            kit.LoadCanvas(120, 80, Color.White);
            Save(kit, outputDir, "rb-01-empty-canvas");

            // Drag from top-left to bottom-right of canvas with no layer present — should produce
            // a rubber-band image-region selection.
            kit.Drag(new Point(20, 15), new Point(80, 55));
            Save(kit, outputDir, "rb-02-after-drag-rubber-band");
            Logger.Log("After rubber-band drag: " + kit.Editor.TestDescribeState());
            Logger.Log($"Selection = {kit.Editor.SelectionDiagnostics.Selection}");
        }

        private static void CaptureHistorySwitchFlow(string outputDir)
        {
            Logger.Log("--- History switch flow ---");
            using var kit = new UiTestKit(new Size(800, 600), withHost: true, visible: false);
            var firstItem = kit.LoadCanvas(96, 64, Color.LightCyan);
            using var pasted = MakeBitmap(20, 14, Color.Purple);
            kit.PasteImage(pasted);
            Save(kit, outputDir, "hs-01-first-item-with-paste");

            // Add a second history item and activate it. State of first item should be stashed.
            using var second = new Bitmap(96, 64);
            using (var g = Graphics.FromImage(second)) { g.Clear(Color.LightSalmon); }
            var secondItem = kit.Host!.HistoryStore.AddObservedImage(second);
            kit.Host.ActivateHistoryItem(secondItem);
            kit.PumpUi();
            Save(kit, outputDir, "hs-02-switched-to-second");
            Logger.Log("On second item: " + kit.Editor.TestDescribeState());

            // Switch back to first — the layer should still be there.
            kit.Host.ActivateHistoryItem(firstItem);
            kit.PumpUi();
            Save(kit, outputDir, "hs-03-back-to-first-expect-layer-restored");
            Logger.Log("Back on first: " + kit.Editor.TestDescribeState());
        }

        private static void CaptureTextAnnotationMoveModeFlow(string outputDir)
        {
            Logger.Log("--- Text annotation Move-mode flow ---");
            using var kit = new UiTestKit(new Size(800, 600), withHost: true, visible: false);
            kit.LoadCanvas(160, 100, Color.White);
            Save(kit, outputDir, "tx-01-empty");

            // Activate text tool, click in canvas, type some text.
            kit.Editor.TestToggleTextTool();
            Logger.Log($"After ToggleTextTool: isTextToolActive={kit.Editor.TestIsTextToolActive}");

            // Click in middle of canvas to create text annotation.
            kit.Click(new Point(40, 30));
            Logger.Log("After click in text mode: " + kit.Editor.TestDescribeTextAnnotations());

            kit.Type("HI");
            Logger.Log("After typing 'HI': " + kit.Editor.TestDescribeTextAnnotations());
            Save(kit, outputDir, "tx-02-after-typing");

            // First Escape: finalize the current text annotation, stay in text tool (Figma-ish).
            kit.Press(Keys.Escape);
            Logger.Log($"After 1st Escape: isTextToolActive={kit.Editor.TestIsTextToolActive} annotations={kit.Editor.TestDescribeTextAnnotations()}");
            Save(kit, outputDir, "tx-03-after-1st-escape");

            // Second Escape: exit text tool entirely, return to Move mode.
            kit.Press(Keys.Escape);
            Logger.Log($"After 2nd Escape: isTextToolActive={kit.Editor.TestIsTextToolActive} annotations={kit.Editor.TestDescribeTextAnnotations()}");
            Save(kit, outputDir, "tx-04-after-2nd-escape");

            // CLICK ON EMPTY CANVAS in Move mode — should NOT create another text annotation.
            kit.Click(new Point(120, 80));
            Logger.Log($"After click empty in Move mode: isTextToolActive={kit.Editor.TestIsTextToolActive} annotations={kit.Editor.TestDescribeTextAnnotations()}");
            Save(kit, outputDir, "tx-05-clicked-empty-move-mode");

            // CLICK ON THE EXISTING TEXT in Move mode — should select, NOT auto-activate text tool.
            kit.Click(new Point(45, 35));
            Logger.Log($"After click on text in Move mode: isTextToolActive={kit.Editor.TestIsTextToolActive} annotations={kit.Editor.TestDescribeTextAnnotations()}");
            Save(kit, outputDir, "tx-06-clicked-text-move-mode");

            // CLICK EMPTY AGAIN — should still not create a new text (the slice 2 fix).
            kit.Click(new Point(120, 80));
            Logger.Log($"After 2nd click empty: isTextToolActive={kit.Editor.TestIsTextToolActive} annotations={kit.Editor.TestDescribeTextAnnotations()}");
            Save(kit, outputDir, "tx-07-clicked-empty-again");
        }

        private static void CaptureZoomedPasteFlow(string outputDir)
        {
            Logger.Log("--- Zoomed paste flow ---");
            using var kit = new UiTestKit(new Size(800, 600), withHost: true, visible: false);
            kit.LoadCanvas(96, 64, Color.White);

            // Zoom in 2x and paste.
            kit.Editor.TestSetZoom(2m);
            kit.PumpUi();
            Logger.Log("After zoom 2x: " + kit.Editor.TestDescribeState());
            Save(kit, outputDir, "zm-01-zoomed-empty");

            using var pasted = MakeBitmap(20, 14, Color.OrangeRed);
            kit.PasteImage(pasted);
            Logger.Log("After paste at zoom 2x: " + kit.Editor.TestDescribeState());
            Save(kit, outputDir, "zm-02-zoomed-after-paste");

            // Click center of layer (in image coords).
            var f = kit.Editor.GetImageLayerFrameForTests(0);
            kit.Click(new Point((int)(f.X + f.Width / 2), (int)(f.Y + f.Height / 2)));
            Save(kit, outputDir, "zm-03-zoomed-after-click");

            // Drag at zoom 2x.
            var center = new Point((int)(f.X + f.Width / 2), (int)(f.Y + f.Height / 2));
            kit.Drag(center, new Point(center.X + 12, center.Y + 8));
            Save(kit, outputDir, "zm-04-zoomed-after-drag");
            Logger.Log("After drag at zoom 2x: " + kit.Editor.TestDescribeState());
        }

        private static void CaptureMultiCommitCycleFlow(string outputDir)
        {
            Logger.Log("--- Multi-commit cycle flow ---");
            using var kit = new UiTestKit(new Size(800, 600), withHost: true, visible: false);
            kit.LoadCanvas(96, 64, Color.White);

            // Cycle 1: paste red, commit.
            using var red = MakeBitmap(20, 14, Color.Red);
            kit.PasteImage(red);
            Save(kit, outputDir, "mc-01-after-red-paste");
            var ok1 = kit.Host!.ExecuteCommandForDiagnostics(Components.Shared.EditorCommandId.CommitEdits);
            Logger.Log($"Cycle 1 commit: {ok1}, state={kit.Editor.TestDescribeState()}");
            Save(kit, outputDir, "mc-02-after-cycle-1-commit");

            // Cycle 2: paste blue ON TOP of the now-baked red, drag, commit.
            using var blue = MakeBitmap(16, 10, Color.Blue);
            kit.PasteImage(blue);
            Save(kit, outputDir, "mc-03-after-blue-paste");
            var f = kit.Editor.GetImageLayerFrameForTests(0);
            kit.Drag(
                new Point((int)(f.X + f.Width / 2), (int)(f.Y + f.Height / 2)),
                new Point((int)(f.X + f.Width / 2 + 10), (int)(f.Y + f.Height / 2 + 5)));
            Save(kit, outputDir, "mc-04-after-blue-drag");
            var ok2 = kit.Host.ExecuteCommandForDiagnostics(Components.Shared.EditorCommandId.CommitEdits);
            Logger.Log($"Cycle 2 commit: {ok2}, state={kit.Editor.TestDescribeState()}");
            Save(kit, outputDir, "mc-05-after-cycle-2-commit");

            // Undo the entire cycle 2 (drag + paste). Canvas should show only red+white from cycle 1.
            kit.Press(Keys.Control | Keys.Z);
            kit.Press(Keys.Control | Keys.Z);
            kit.Press(Keys.Control | Keys.Z);
            Save(kit, outputDir, "mc-06-after-3-undos");
            Logger.Log($"After 3 undos: {kit.Editor.TestDescribeState()}");
        }

        private static Bitmap MakeBitmap(int width, int height, Color fill)
        {
            var bmp = new Bitmap(width, height);
            using var g = Graphics.FromImage(bmp);
            g.Clear(fill);
            return bmp;
        }

        private static void CaptureLayerRotationFlow(string outputDir)
        {
            Logger.Log("--- Layer rotation flow ---");
            using var kit = new UiTestKit(new Size(800, 600), withHost: true, visible: false);
            kit.LoadCanvas(120, 80, Color.White);

            using var layerBmp = MakeBitmap(40, 20, Color.OrangeRed);
            kit.PasteImage(layerBmp);
            Save(kit, outputDir, "rot-01-no-rotation");
            Logger.Log($"rot-01: {kit.Editor.TestDescribeState()}");

            // Set 45° via test helper (simulating what drag-rotate would produce).
            kit.Editor.TestSetLayerRotationDeg(0, 45f);
            kit.Editor.TestFireMouseDownAtImagePixel(new Point(60, 30), MouseButtons.Left); // keep selected
            kit.Editor.TestFireMouseUpAtImagePixel(new Point(60, 30), MouseButtons.Left);
            pictureBox1Invalidate(kit);
            Save(kit, outputDir, "rot-02-45deg");
            Logger.Log($"rot-02: rotation={kit.Editor.TestGetLayerRotationDeg(0):F1}°");

            // Set -30°
            kit.Editor.TestSetLayerRotationDeg(0, -30f);
            pictureBox1Invalidate(kit);
            Save(kit, outputDir, "rot-03-minus30deg");
            Logger.Log($"rot-03: rotation={kit.Editor.TestGetLayerRotationDeg(0):F1}°");

            // Test rotation handle interaction: simulate drag from the layer center
            // at a fixed angle to +90° rotation, using BeginLayerInteractionForTests.
            kit.Editor.TestSetLayerRotationDeg(0, 0f);
            var f = kit.Editor.GetImageLayerFrameForTests(0);
            int cx = (int)(f.X + f.Width / 2);
            int cy = (int)(f.Y + f.Height / 2);
            // Rotation handle is ~28px above the top-center in screen px (zoom=1 → same in image px).
            var rotHandlePixel = new Point(cx, (int)(f.Y - 28));
            kit.Editor.BeginLayerInteractionForTests(rotHandlePixel); // hits Rotate handle
            Logger.Log($"rot-04: activeHandle after begin={kit.Editor.TestActiveLayerHandle}");
            // Drag from top-center direction to the right (90° = pointing right from center).
            kit.Editor.UpdateLayerInteractionForTests(new Point(cx + (int)(f.Height / 2 + 28), cy));
            kit.Editor.EndLayerInteractionForTests();
            Save(kit, outputDir, "rot-04-dragged-to-90deg");
            Logger.Log($"rot-04: rotation after drag={kit.Editor.TestGetLayerRotationDeg(0):F1}°");

            // Undo should restore 0°.
            kit.Editor.TestFireKeyDown(System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Z);
            Save(kit, outputDir, "rot-05-after-undo");
            Logger.Log($"rot-05: rotation after undo={kit.Editor.TestGetLayerRotationDeg(0):F1}°");

            // Verify hit-test on rotated layer: paste fresh layer, rotate 90°, click
            // what would be the body in the rotated position.
            using var kit2 = new UiTestKit(new Size(800, 600), withHost: true, visible: false);
            kit2.LoadCanvas(120, 80, Color.White);
            using var layerBmp2 = MakeBitmap(40, 10, Color.Blue);
            kit2.PasteImage(layerBmp2);
            kit2.Editor.TestSetLayerRotationDeg(0, 90f);
            // Deselect and then try to click on the layer body (which is now rotated).
            kit2.Click(new Point(100, 5)); // deselect
            // The layer center is at approximately (60, 40) for a 120x80 canvas with a 40x10 layer pasted center.
            var f2 = kit2.Editor.GetImageLayerFrameForTests(0);
            int cx2 = (int)(f2.X + f2.Width / 2);
            int cy2 = (int)(f2.Y + f2.Height / 2);
            // At 90° rotation, the 40-wide extent is now vertical, 10-tall extent is horizontal.
            // A point (cx2, cy2+5) should be inside the rotated body.
            kit2.Click(new Point(cx2, cy2 + 5));
            Logger.Log($"rot-06: selected after rotated-body click = {kit2.Editor.SelectedLayerIndexForTests}");
            Save(kit2, outputDir, "rot-06-rotated-hit-test");
        }

        private static void pictureBox1Invalidate(UiTestKit kit)
        {
            // Force a paint-pass so the screenshot reflects the changed rotation.
            // We use the same trick as the existing tests: just re-fire a no-op mouse move.
            kit.Editor.TestFireMouseMoveAtImagePixel(new Point(0, 0), MouseButtons.None);
        }

        private static void CaptureAnnotationToolFlow(string outputDir)
        {
            Logger.Log("--- Annotation tool flow ---");
            using var kit = new UiTestKit(new Size(800, 600), withHost: true, visible: false);
            kit.LoadCanvas(120, 80, Color.White);

            // ── Arrow: draw, select in Move mode, drag, delete ──────────────────────
            kit.Editor.TestToggleArrowTool();
            Logger.Log($"Arrow tool active: {kit.Editor.TestActiveDrawingTool}");

            // Draw an arrow from (20,20) to (60,50).
            kit.Drag(new Point(20, 20), new Point(60, 50));
            Save(kit, outputDir, "an-01-after-draw-arrow");
            Logger.Log($"After draw arrow: {kit.Editor.TestDescribeAnnotationShapes()}");

            // Switch back to Move mode (deactivate drawing tool).
            kit.Editor.TestDeactivateDrawingTool();
            Logger.Log($"Drawing tool after deactivate: {kit.Editor.TestActiveDrawingTool}");

            // Click somewhere else first to deselect.
            kit.Click(new Point(100, 10));
            Logger.Log($"After click empty: {kit.Editor.TestDescribeAnnotationShapes()}");

            // Click on the arrow midpoint to select it.
            kit.Click(new Point(40, 35));
            Save(kit, outputDir, "an-02-arrow-selected-in-move-mode");
            Logger.Log($"After click arrow: {kit.Editor.TestDescribeAnnotationShapes()} selectedAnnotation={kit.Editor.TestSelectedAnnotation?.Type.ToString() ?? "null"}");

            // Drag the arrow via the Move handle.
            kit.Drag(new Point(40, 35), new Point(50, 45));
            Save(kit, outputDir, "an-03-after-drag-arrow");
            Logger.Log($"After drag arrow: {kit.Editor.TestDescribeAnnotationShapes()}");

            // Delete via keyboard.
            kit.Press(Keys.Delete);
            Save(kit, outputDir, "an-04-after-delete-arrow");
            Logger.Log($"After delete arrow: {kit.Editor.TestDescribeAnnotationShapes()}");

            // ── Rectangle: draw, select, drag endpoint, Escape deselect ────────────
            kit.Editor.TestToggleRectTool();
            kit.Drag(new Point(15, 15), new Point(55, 45));
            Save(kit, outputDir, "an-05-after-draw-rect");
            Logger.Log($"After draw rect: {kit.Editor.TestDescribeAnnotationShapes()}");

            kit.Editor.TestDeactivateDrawingTool();
            kit.Click(new Point(100, 10));  // deselect
            kit.Click(new Point(35, 30));   // select rect by body hit
            Save(kit, outputDir, "an-06-rect-selected-in-move-mode");
            Logger.Log($"After click rect: selected={kit.Editor.TestSelectedAnnotation?.Type.ToString() ?? "null"}");

            // Drag the rect (body drag via Move handle).
            kit.Drag(new Point(35, 30), new Point(45, 38));
            Save(kit, outputDir, "an-07-after-drag-rect");
            Logger.Log($"After drag rect: {kit.Editor.TestDescribeAnnotationShapes()}");

            // Escape deselects.
            kit.Press(Keys.Escape);
            Save(kit, outputDir, "an-08-after-escape-rect");
            Logger.Log($"After escape: selected={kit.Editor.TestSelectedAnnotation?.Type.ToString() ?? "null"}");

            // ── Shift-aspect resize on image layer ───────────────────────────────────
            Logger.Log("--- Shift-aspect-preserve resize ---");
            using var kit2 = new UiTestKit(new Size(800, 600), withHost: true, visible: false);
            kit2.LoadCanvas(120, 80, Color.White);

            // Paste a 40×20 layer (2:1 aspect).
            using var layer40x20 = MakeBitmap(40, 20, Color.Cyan);
            kit2.PasteImage(layer40x20);
            var f = kit2.Editor.GetImageLayerFrameForTests(0);
            Logger.Log($"Layer frame before resize: {f} (aspect={f.Width / f.Height:F2})");

            // Drag the BottomRight handle with Shift (simulate by reading ModifierKeys through
            // TestFireMouseDown with a simulated Shift key state isn't directly possible, so we
            // call UpdateLayerInteractionForTests after manually setting up the interaction and
            // note ModifierKeys can't be set from tests — log the free-resize as baseline and
            // document that Shift path is validated interactively).
            // Free resize without Shift for comparison:
            kit2.Editor.BeginLayerInteractionForTests(new Point((int)f.Right, (int)f.Bottom));
            kit2.Editor.UpdateLayerInteractionForTests(new Point((int)f.Right + 20, (int)f.Bottom + 20));
            kit2.Editor.EndLayerInteractionForTests();
            var fFree = kit2.Editor.GetImageLayerFrameForTests(0);
            Logger.Log($"Layer frame after free resize (+20,+20): {fFree} (aspect={fFree.Width / fFree.Height:F2}, was 2.00)");
            Save(kit2, outputDir, "sr-01-free-resize-baseline");
        }

        private static void Save(UiTestKit kit, string dir, string label)
        {
            var path = Path.Combine(dir, $"{label}.png");
            using var bmp = kit.CaptureForm();
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            Logger.Log($"  saved {label} -> {path}");
        }

        private static void SavePictureBoxOnly(UiTestKit kit, string dir, string label)
        {
            var path = Path.Combine(dir, $"{label}.png");
            using var bmp = kit.CapturePictureBox();
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            Logger.Log($"  saved {label} -> {path}");
        }
    }
}
