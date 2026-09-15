using System.Drawing;
using System.Windows.Forms;
using screenzap.Components;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class ImageLayerApplyTests
    {
        [Fact]
        public void Apply_NoLayers_ReturnsFalse()
        {
            StaTest.Run(() =>
            {
                using var editor = new screenzap.ImageEditor();
                using var canvas = new Bitmap(40, 30);
                editor.LoadImage(canvas);

                Assert.Equal(0, editor.ImageLayerCountForTests);
                Assert.False(editor.ApplyFloatingPasteForTests());
            });
        }

        [Fact]
        public void Apply_BurnsLayerIntoBase_ClearsLayers()
        {
            StaTest.Run(() =>
            {
                using var editor = EditorFixture.WithCanvas(40, 30);

                using var pasted = new Bitmap(8, 8);
                using (var g = Graphics.FromImage(pasted))
                    g.Clear(Color.Lime);
                editor.SetInternalClipboardImageForDiagnostics(pasted);
                Assert.True(editor.PasteFromClipboardForDiagnostics());
                Assert.Equal(1, editor.ImageLayerCountForTests);

                // Apply burns the layer into the base bitmap.
                Assert.True(editor.ApplyFloatingPasteForTests());

                // Layer list must be empty after apply.
                Assert.Equal(0, editor.ImageLayerCountForTests);

                // The base bitmap now contains the composited pixel (layer was centered at (16,11)).
                using var baseCopy = editor.CloneBaseBitmapForTests()!;
                Assert.Equal(Color.Lime.ToArgb(), baseCopy.GetPixel(20, 15).ToArgb());
                // Corners remain white (layer didn't cover them).
                Assert.Equal(Color.White.ToArgb(), baseCopy.GetPixel(0, 0).ToArgb());
            });
        }

        [Fact]
        public void Apply_ThenUndo_RestoresBaseAndLayers()
        {
            StaTest.Run(() =>
            {
                using var editor = EditorFixture.WithCanvas(40, 30);

                using var pasted = new Bitmap(8, 8);
                using (var g = Graphics.FromImage(pasted))
                    g.Clear(Color.Magenta);
                editor.SetInternalClipboardImageForDiagnostics(pasted);
                Assert.True(editor.PasteFromClipboardForDiagnostics());

                Assert.True(editor.ApplyFloatingPasteForTests());
                Assert.Equal(0, editor.ImageLayerCountForTests);

                var presenter = (IClipboardDocumentPresenter)editor;
                Assert.True(presenter.CanExecute(EditorCommandId.Undo));
                Assert.True(presenter.TryExecute(EditorCommandId.Undo));

                // Undo restores the original white base and the floating layer.
                Assert.Equal(1, editor.ImageLayerCountForTests);
                using var afterUndo = editor.CloneBaseBitmapForTests()!;
                Assert.Equal(Color.White.ToArgb(), afterUndo.GetPixel(0, 0).ToArgb());
            });
        }

        /// <summary>
        /// Paste a solid <paramref name="color"/> block and park it at
        /// (<paramref name="x"/>, <paramref name="y"/>). The pasted layer is left selected,
        /// exactly as a real paste leaves it.
        /// </summary>
        private static void PasteBlockAt(
            screenzap.ImageEditor editor,
            Color color,
            int width,
            int height,
            float x,
            float y)
        {
            using var block = new Bitmap(width, height);
            using (var graphics = Graphics.FromImage(block))
            {
                graphics.Clear(color);
            }

            editor.SetInternalClipboardImageForDiagnostics(block);
            Assert.True(editor.PasteFromClipboardForDiagnostics());
            editor.SetSelectedLayerXForTests(x);
            editor.SetSelectedLayerYForTests(y);
        }

        [Fact]
        public void Apply_WithLayerSelected_HardensOnlyThatLayer()
        {
            StaTest.Run(() =>
            {
                // Two pastes side by side, no overlap. Hardening the one that happens to be
                // selected used to burn the whole stack in with it.
                using var editor = EditorFixture.WithCanvas(60, 40);
                PasteBlockAt(editor, Color.Lime, 8, 8, 0f, 0f);
                PasteBlockAt(editor, Color.Magenta, 8, 8, 40f, 20f);
                Assert.Equal(2, editor.ImageLayerCountForTests);
                Assert.Equal(1, editor.SelectedLayerIndexForTests);

                Assert.True(editor.ApplyFloatingPasteForTests());

                // The lime block is still floating, and still where it was.
                Assert.Equal(1, editor.ImageLayerCountForTests);
                Assert.Equal(new RectangleF(0f, 0f, 8f, 8f), editor.GetImageLayerFrameForTests(0));

                // Selection moves to what is left, so the next Enter hardens that one alone.
                Assert.Equal(0, editor.SelectedLayerIndexForTests);

                using var baked = editor.CloneBaseBitmapForTests()!;
                Assert.Equal(Color.Magenta.ToArgb(), baked.GetPixel(44, 24).ToArgb());
                Assert.Equal(Color.White.ToArgb(), baked.GetPixel(4, 4).ToArgb());
            });
        }

        [Fact]
        public void Apply_WithNothingSelected_IsANoop()
        {
            StaTest.Run(() =>
            {
                // Nothing selected means nothing to glue. Enter used to fall through to
                // "burn the whole stack" here, which is a lot of document to change by
                // accident from a key that otherwise commits one layer.
                using var editor = EditorFixture.WithCanvas(60, 40);
                PasteBlockAt(editor, Color.Lime, 8, 8, 0f, 0f);
                PasteBlockAt(editor, Color.Magenta, 8, 8, 40f, 20f);

                editor.SetSelectedLayerForTests(-1);
                Assert.False(editor.ApplyFloatingPasteForTests());
                Assert.Equal(2, editor.ImageLayerCountForTests);

                // The command is disabled too, so the toolbar button cannot do it either.
                var presenter = (IClipboardDocumentPresenter)editor;
                Assert.False(presenter.CanExecute(EditorCommandId.ApplyFloatingPaste));

                using var untouched = editor.CloneBaseBitmapForTests()!;
                Assert.Equal(Color.White.ToArgb(), untouched.GetPixel(4, 4).ToArgb());
                Assert.Equal(Color.White.ToArgb(), untouched.GetPixel(44, 24).ToArgb());
            });
        }

        [Fact]
        public void Apply_OnOverlappingLayer_GluesOnlyThatOne_LeavingTheOtherAbove()
        {
            StaTest.Run(() =>
            {
                // Overlap buys the lower layer nothing: hardening means "glue this one down",
                // so the magenta block joins the canvas and the lime block, still floating,
                // now draws above it. Floating things are on top of the background.
                using var editor = EditorFixture.WithCanvas(60, 40);
                PasteBlockAt(editor, Color.Lime, 10, 10, 0f, 0f);
                PasteBlockAt(editor, Color.Magenta, 10, 10, 5f, 5f);

                Assert.True(editor.ApplyFloatingPasteForTests());
                Assert.Equal(1, editor.ImageLayerCountForTests);
                Assert.Equal(new RectangleF(0f, 0f, 10f, 10f), editor.GetImageLayerFrameForTests(0));

                using var baked = editor.CloneBaseBitmapForTests()!;
                Assert.Equal(Color.Magenta.ToArgb(), baked.GetPixel(7, 7).ToArgb());
                Assert.Equal(Color.Magenta.ToArgb(), baked.GetPixel(12, 12).ToArgb());
                // The lime block went nowhere near the canvas.
                Assert.Equal(Color.White.ToArgb(), baked.GetPixel(2, 2).ToArgb());

                // ...and on screen it now covers the glued-down magenta where they overlap.
                using var composite = editor.BuildCompositeImageForTests();
                Assert.Equal(Color.Lime.ToArgb(), composite.GetPixel(7, 7).ToArgb());
                Assert.Equal(Color.Magenta.ToArgb(), composite.GetPixel(12, 12).ToArgb());
            });
        }

        [Fact]
        public void Apply_LeavesLiveAnnotationsOutOfTheBase()
        {
            StaTest.Run(() =>
            {
                // Apply used to bake through BuildCompositeImage, which flattens annotations
                // too — they stayed live on top of their own baked-in copy.
                using var editor = EditorFixture.WithCanvas(80, 60);
                editor.TestSetAnnotationColor(Color.Red);
                editor.TestToggleRectTool();
                editor.TestFireMouseDownAtImagePixel(new Point(50, 40), MouseButtons.Left);
                editor.TestFireMouseMoveAtImagePixel(new Point(70, 55), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(70, 55), MouseButtons.Left);
                editor.TestDeactivateDrawingTool();
                Assert.Equal(1, editor.TestAnnotationShapeCount);

                PasteBlockAt(editor, Color.Lime, 8, 8, 0f, 0f);
                Assert.True(editor.ApplyFloatingPasteForTests());

                using var baked = editor.CloneBaseBitmapForTests()!;
                Assert.Equal(Color.Lime.ToArgb(), baked.GetPixel(4, 4).ToArgb());

                // The shape is still live, and the canvas under its top edge is untouched.
                Assert.Equal(1, editor.TestAnnotationShapeCount);
                Assert.Equal(Color.White.ToArgb(), baked.GetPixel(60, 40).ToArgb());
            });
        }
    }
}
