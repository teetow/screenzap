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

        private static Bitmap MakeBitmap(int width, int height, Color fill)
        {
            var bmp = new Bitmap(width, height);
            using var g = Graphics.FromImage(bmp);
            g.Clear(fill);
            return bmp;
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
