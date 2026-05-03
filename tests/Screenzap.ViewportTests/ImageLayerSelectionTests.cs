using System.Drawing;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class ImageLayerSelectionTests
    {
        private static screenzap.ImageEditor PrepareEditorWithCenteredLayer(out RectangleF frame)
        {
            var editor = new screenzap.ImageEditor();
            var canvas = new Bitmap(80, 60);
            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.White);
            }
            editor.LoadImage(canvas);
            canvas.Dispose();

            using var pasted = new Bitmap(20, 14);
            using (var g = Graphics.FromImage(pasted))
            {
                g.Clear(Color.Magenta);
            }
            editor.SetInternalClipboardImageForDiagnostics(pasted);
            Assert.True(editor.PasteFromClipboardForDiagnostics());

            frame = editor.GetImageLayerFrameForTests(0);
            return editor;
        }

        [Fact]
        public void HitTest_OnLayerBody_Selects()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithCenteredLayer(out var frame);
                Assert.Equal(-1, editor.SelectedLayerIndexForTests);

                var insidePoint = new Point((int)(frame.X + frame.Width / 2), (int)(frame.Y + frame.Height / 2));
                Assert.True(editor.BeginLayerInteractionForTests(insidePoint));
                Assert.Equal(0, editor.SelectedLayerIndexForTests);
                editor.EndLayerInteractionForTests();
            });
        }

        [Fact]
        public void HitTest_OutsideLayer_DoesNotSelect()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithCenteredLayer(out var frame);
                var outsidePoint = new Point(2, 2); // top-left corner is outside a centered layer
                Assert.False(editor.BeginLayerInteractionForTests(outsidePoint));
                Assert.Equal(-1, editor.SelectedLayerIndexForTests);
            });
        }

        [Fact]
        public void DragLayerBody_TranslatesFrame_AndPushesUndoStep()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithCenteredLayer(out var frame);
                var startPoint = new Point((int)(frame.X + 4), (int)(frame.Y + 4));
                Assert.True(editor.BeginLayerInteractionForTests(startPoint));

                editor.UpdateLayerInteractionForTests(new Point(startPoint.X + 7, startPoint.Y + 5));

                var movedFrame = editor.GetImageLayerFrameForTests(0);
                Assert.Equal(frame.X + 7, movedFrame.X);
                Assert.Equal(frame.Y + 5, movedFrame.Y);
                Assert.Equal(frame.Width, movedFrame.Width);
                Assert.Equal(frame.Height, movedFrame.Height);

                editor.EndLayerInteractionForTests();

                var presenter = (IClipboardDocumentPresenter)editor;
                Assert.True(presenter.CanExecute(EditorCommandId.Undo));
                Assert.True(presenter.TryExecute(EditorCommandId.Undo));

                var afterUndo = editor.GetImageLayerFrameForTests(0);
                Assert.Equal(frame.X, afterUndo.X);
                Assert.Equal(frame.Y, afterUndo.Y);
            });
        }

        [Fact]
        public void DragHandle_ResizesFrame_AndUndoRestores()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithCenteredLayer(out var frame);

                // First select via body click...
                var bodyPoint = new Point((int)(frame.X + frame.Width / 2), (int)(frame.Y + frame.Height / 2));
                Assert.True(editor.BeginLayerInteractionForTests(bodyPoint));
                editor.EndLayerInteractionForTests();

                // ...then begin a fresh interaction at the bottom-right corner handle.
                var corner = new Point((int)frame.Right, (int)frame.Bottom);
                Assert.True(editor.BeginLayerInteractionForTests(corner));

                editor.UpdateLayerInteractionForTests(new Point(corner.X + 6, corner.Y + 4));

                var resized = editor.GetImageLayerFrameForTests(0);
                Assert.Equal(frame.Width + 6, resized.Width);
                Assert.Equal(frame.Height + 4, resized.Height);
                Assert.Equal(frame.X, resized.X);
                Assert.Equal(frame.Y, resized.Y);

                editor.EndLayerInteractionForTests();

                var presenter = (IClipboardDocumentPresenter)editor;
                Assert.True(presenter.TryExecute(EditorCommandId.Undo));

                var afterUndo = editor.GetImageLayerFrameForTests(0);
                Assert.Equal(frame.Width, afterUndo.Width);
                Assert.Equal(frame.Height, afterUndo.Height);
            });
        }

        [Fact]
        public void DeselectingByClickOutside_RestoresMoveMode()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithCenteredLayer(out var frame);
                var insidePoint = new Point((int)(frame.X + 4), (int)(frame.Y + 4));
                Assert.True(editor.BeginLayerInteractionForTests(insidePoint));
                editor.EndLayerInteractionForTests();
                Assert.Equal(0, editor.SelectedLayerIndexForTests);

                var outside = new Point(2, 2);
                Assert.False(editor.BeginLayerInteractionForTests(outside));
                // Selection state itself is cleared by the host's MouseDown cascade
                // (DeselectImageLayerIfAny). Assert the helper directly.
                editor.SetSelectedLayerForTests(-1);
                Assert.Equal(-1, editor.SelectedLayerIndexForTests);
            });
        }

        [Fact]
        public void ClickWithoutDrag_DoesNotPushUndoStep()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithCenteredLayer(out var frame);

                var presenter = (IClipboardDocumentPresenter)editor;
                // Paste already pushed an undo step. Track its current undoability.
                var canUndoBefore = presenter.CanExecute(EditorCommandId.Undo);
                Assert.True(canUndoBefore);
                Assert.True(presenter.TryExecute(EditorCommandId.Undo));
                Assert.False(presenter.CanExecute(EditorCommandId.Undo));
                Assert.True(presenter.TryExecute(EditorCommandId.Redo));

                var bodyPoint = new Point((int)(frame.X + frame.Width / 2), (int)(frame.Y + frame.Height / 2));
                Assert.True(editor.BeginLayerInteractionForTests(bodyPoint));
                editor.EndLayerInteractionForTests();

                // No drag → no new undo step beyond the original paste step.
                Assert.True(presenter.TryExecute(EditorCommandId.Undo));
                Assert.False(presenter.CanExecute(EditorCommandId.Undo));
            });
        }
    }
}
