using System.Drawing;
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
                using var editor = new screenzap.ImageEditor();
                using var canvas = new Bitmap(40, 30);
                using (var g = Graphics.FromImage(canvas))
                    g.Clear(Color.White);
                editor.LoadImage(canvas);

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
                using var editor = new screenzap.ImageEditor();
                using var canvas = new Bitmap(40, 30);
                using (var g = Graphics.FromImage(canvas))
                    g.Clear(Color.White);
                editor.LoadImage(canvas);

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
    }
}
