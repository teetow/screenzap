using System;
using System.Drawing;

namespace Screenzap.ViewportTests
{
    /// <summary>
    /// The editor setup every suite here opened with — construct, paint a white canvas, load it —
    /// in one place instead of fifteen near-identical PrepareEditor helpers.
    ///
    /// It also pins the modifier seams, which is not cosmetic: they default to reading the real
    /// keyboard, so a suite that does not pin them fails whenever whoever is running the tests
    /// happens to be holding Shift or Ctrl. Suites that want a modifier down set it explicitly.
    /// </summary>
    internal static class EditorFixture
    {
        /// <summary>
        /// A white <paramref name="width"/>×<paramref name="height"/> canvas loaded into a fresh
        /// editor.
        /// </summary>
        /// <param name="paint">
        /// Runs over the white fill, for suites needing recognisable content (a coloured block to
        /// stamp, clone, or crop).
        /// </param>
        /// <param name="createControl">
        /// Force the handle up BEFORE the image is loaded, for suites that assert on layout or
        /// drive ProcessCmdKey. Order matters — LoadImage centres against the viewport it can see
        /// at the time — so this is a parameter rather than something a caller bolts on after.
        /// </param>
        /// <param name="formSize">Client size to apply once the handle exists.</param>
        internal static screenzap.ImageEditor WithCanvas(
            int width,
            int height,
            Action<Graphics>? paint = null,
            bool createControl = false,
            Size? formSize = null)
        {
            var editor = new screenzap.ImageEditor();
            try
            {
                PinModifiers(editor);

                if (createControl || formSize.HasValue)
                {
                    editor.CreateControl();
                }

                if (formSize.HasValue)
                {
                    editor.TestSetSize(formSize.Value.Width, formSize.Value.Height);
                }

                using var canvas = new Bitmap(width, height);
                using (var graphics = Graphics.FromImage(canvas))
                {
                    graphics.Clear(Color.White);
                    paint?.Invoke(graphics);
                }

                // LoadImage clones the source, so the canvas is ours to dispose.
                editor.LoadImage(canvas);
                return editor;
            }
            catch
            {
                editor.Dispose();
                throw;
            }
        }

        internal static screenzap.ImageEditor WithCanvas(
            Size size,
            Action<Graphics>? paint = null,
            bool createControl = false,
            Size? formSize = null)
            => WithCanvas(size.Width, size.Height, paint, createControl, formSize);

        /// <summary>
        /// Pin every modifier the editor branches on to "up". Passing false is an assertion that
        /// the key is not held, not a hand-back to the OS — see the tri-state overrides in
        /// ImageEditor.Annotations.cs.
        /// </summary>
        internal static void PinModifiers(screenzap.ImageEditor editor)
        {
            editor.TestSetShiftHeld(false);
            editor.TestSetCtrlHeld(false);
            editor.TestSetAltHeld(false);
            editor.SetLayerCropModifierForTests(false);
        }

        /// <summary>A standalone filled bitmap, for tests that load or paste one themselves.</summary>
        internal static Bitmap Canvas(int width, int height, Color? fill = null)
        {
            var bitmap = new Bitmap(width, height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(fill ?? Color.White);
            }
            return bitmap;
        }
    }
}
