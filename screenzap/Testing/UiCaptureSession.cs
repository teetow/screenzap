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
