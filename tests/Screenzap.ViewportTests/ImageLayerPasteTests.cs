using System.Drawing;
using screenzap.Components;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class ImageLayerPasteTests
    {
        [Fact]
        public void Paste_AddsImageLayer_WithoutMutatingBaseBitmap()
        {
            StaTest.Run(() =>
            {
                using var editor = new screenzap.ImageEditor();
                using var canvas = new Bitmap(64, 48);
                using (var g = Graphics.FromImage(canvas))
                {
                    g.Clear(Color.LightGray);
                }
                editor.LoadImage(canvas);

                using var pasted = new Bitmap(20, 12);
                using (var g = Graphics.FromImage(pasted))
                {
                    g.Clear(Color.Red);
                }
                editor.SetInternalClipboardImageForDiagnostics(pasted);

                Assert.Equal(0, editor.ImageLayerCountForTests);

                Assert.True(editor.PasteFromClipboardForDiagnostics());

                Assert.Equal(1, editor.ImageLayerCountForTests);

                // Base bitmap is unchanged — paste is non-destructive.
                using var baseCopy = editor.CloneBaseBitmapForTests()!;
                Assert.Equal(Color.LightGray.ToArgb(), baseCopy.GetPixel(2, 2).ToArgb());
                Assert.Equal(Color.LightGray.ToArgb(), baseCopy.GetPixel(canvas.Width / 2, canvas.Height / 2).ToArgb());

                // Layer frame is centered on canvas.
                var frame = editor.GetImageLayerFrameForTests(0);
                Assert.Equal((canvas.Width - pasted.Width) / 2f, frame.X);
                Assert.Equal((canvas.Height - pasted.Height) / 2f, frame.Y);
                Assert.Equal(pasted.Width, frame.Width);
                Assert.Equal(pasted.Height, frame.Height);
            });
        }

        [Fact]
        public void Paste_ThenUndo_RemovesLayer_RedoRestoresIt()
        {
            StaTest.Run(() =>
            {
                using var editor = new screenzap.ImageEditor();
                using var canvas = new Bitmap(40, 30);
                editor.LoadImage(canvas);

                using var pasted = new Bitmap(10, 10);
                using (var g = Graphics.FromImage(pasted))
                {
                    g.Clear(Color.Blue);
                }
                editor.SetInternalClipboardImageForDiagnostics(pasted);

                Assert.True(editor.PasteFromClipboardForDiagnostics());
                Assert.Equal(1, editor.ImageLayerCountForTests);

                var presenter = (IClipboardDocumentPresenter)editor;
                Assert.True(presenter.CanExecute(EditorCommandId.Undo));
                Assert.True(presenter.TryExecute(EditorCommandId.Undo));
                Assert.Equal(0, editor.ImageLayerCountForTests);

                Assert.True(presenter.CanExecute(EditorCommandId.Redo));
                Assert.True(presenter.TryExecute(EditorCommandId.Redo));
                Assert.Equal(1, editor.ImageLayerCountForTests);
            });
        }

        [Fact]
        public void BuildCompositeImage_BakesPastedLayer()
        {
            StaTest.Run(() =>
            {
                using var editor = new screenzap.ImageEditor();
                using var canvas = new Bitmap(40, 30);
                using (var g = Graphics.FromImage(canvas))
                {
                    g.Clear(Color.White);
                }
                editor.LoadImage(canvas);

                using var pasted = new Bitmap(8, 8);
                using (var g = Graphics.FromImage(pasted))
                {
                    g.Clear(Color.Lime);
                }
                editor.SetInternalClipboardImageForDiagnostics(pasted);
                Assert.True(editor.PasteFromClipboardForDiagnostics());

                using var composite = editor.BuildCompositeImageForTests();

                // Sample center pixel — should be the lime layer (canvas is 40x30, layer is 8x8 centered at (16,11) to (24,19)).
                Assert.Equal(Color.Lime.ToArgb(), composite.GetPixel(20, 15).ToArgb());
                // Corner is still white (untouched by layer).
                Assert.Equal(Color.White.ToArgb(), composite.GetPixel(0, 0).ToArgb());
            });
        }

        [Fact]
        public void StashHistoryItemState_ThenLoadHistoryItem_RoundTripsLayers()
        {
            StaTest.Run(() =>
            {
                using var editor = new screenzap.ImageEditor();
                using var canvas = new Bitmap(40, 30);
                editor.LoadImage(canvas);

                using var pasted = new Bitmap(10, 10);
                editor.SetInternalClipboardImageForDiagnostics(pasted);
                Assert.True(editor.PasteFromClipboardForDiagnostics());
                Assert.Equal(1, editor.ImageLayerCountForTests);

                var presenter = (IClipboardDocumentPresenter)editor;
                using var item = ClipboardHistoryItem.FromImage(canvas);

                presenter.StashHistoryItemState(item);
                Assert.NotNull(item.ImageLayers);
                Assert.Single(item.ImageLayers!);

                // Clear the editor by loading a fresh image.
                using var blankCanvas = new Bitmap(40, 30);
                editor.LoadImage(blankCanvas);
                Assert.Equal(0, editor.ImageLayerCountForTests);

                presenter.LoadHistoryItem(item);
                Assert.Equal(1, editor.ImageLayerCountForTests);
            });
        }

        [Fact]
        public void Paste_ThenUndoAfterCommit_RestoresUnflattenedBaselineAndDropsLayer()
        {
            StaTest.Run(() =>
            {
                using var editor = new screenzap.ImageEditor();
                using var canvas = new Bitmap(40, 30);
                using (var g = Graphics.FromImage(canvas))
                {
                    g.Clear(Color.White);
                }
                editor.LoadImage(canvas);

                using var pasted = new Bitmap(8, 8);
                using (var g = Graphics.FromImage(pasted))
                {
                    g.Clear(Color.Magenta);
                }
                editor.SetInternalClipboardImageForDiagnostics(pasted);
                Assert.True(editor.PasteFromClipboardForDiagnostics());

                // Simulate the host commit cycle: stash → flatten → MarkClean → reload.
                var presenter = (IClipboardDocumentPresenter)editor;
                using var item = ClipboardHistoryItem.FromImage(canvas);

                presenter.StashHistoryItemState(item);
                using var flattened = (Bitmap)presenter.GetCurrentContent()!;
                item.UpdateCurrentImage(flattened);
                item.MarkClean();
                presenter.LoadHistoryItem(item);

                // Post-commit: layers cleared, base is the flattened (magenta-stamped) bitmap.
                Assert.Equal(0, editor.ImageLayerCountForTests);
                using (var afterCommit = editor.CloneBaseBitmapForTests()!)
                {
                    Assert.Equal(Color.Magenta.ToArgb(), afterCommit.GetPixel(20, 15).ToArgb());
                }

                // Undo should walk back across the commit by restoring the unflattened base + dropping the layer.
                Assert.True(presenter.CanExecute(EditorCommandId.Undo));
                Assert.True(presenter.TryExecute(EditorCommandId.Undo));

                Assert.Equal(0, editor.ImageLayerCountForTests);
                using (var afterUndo = editor.CloneBaseBitmapForTests()!)
                {
                    Assert.Equal(Color.White.ToArgb(), afterUndo.GetPixel(20, 15).ToArgb());
                }
            });
        }
    }
}
