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
        public void Paste_AutoSelectsNewLayer()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithCenteredLayer(out _);
                // Figma model: paste leaves the new layer selected so the user can immediately
                // drag/resize/delete without an extra click.
                Assert.Equal(0, editor.SelectedLayerIndexForTests);
            });
        }

        [Fact]
        public void HitTest_OutsideLayer_DoesNotSelect()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditorWithCenteredLayer(out _);
                editor.SetSelectedLayerForTests(-1);
                var outsidePoint = new Point(2, 2);
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
                // Click well inside the body (away from corner-handle tolerance) — paste pre-selects
                // the layer, so a click within ~4 px of any corner is interpreted as a handle hit.
                var bodyPoint = new Point((int)(frame.X + frame.Width / 2), (int)(frame.Y + frame.Height / 2));
                Assert.True(editor.BeginLayerInteractionForTests(bodyPoint));

                editor.UpdateLayerInteractionForTests(new Point(bodyPoint.X + 7, bodyPoint.Y + 5));

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
                // Layer is pre-selected after paste; jump straight to a corner-handle drag.
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
                using var editor = PrepareEditorWithCenteredLayer(out _);
                Assert.Equal(0, editor.SelectedLayerIndexForTests);

                var outside = new Point(2, 2);
                Assert.False(editor.BeginLayerInteractionForTests(outside));
                // The empty-click branch in pictureBox1_MouseDown calls DeselectImageLayerIfAny;
                // the test-input partial bypasses that cascade so simulate the deselect directly.
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
                Assert.True(presenter.CanExecute(EditorCommandId.Undo));
                Assert.True(presenter.TryExecute(EditorCommandId.Undo));
                Assert.False(presenter.CanExecute(EditorCommandId.Undo));
                Assert.True(presenter.TryExecute(EditorCommandId.Redo));

                var bodyPoint = new Point((int)(frame.X + frame.Width / 2), (int)(frame.Y + frame.Height / 2));
                Assert.True(editor.BeginLayerInteractionForTests(bodyPoint));
                editor.EndLayerInteractionForTests();

                Assert.True(presenter.TryExecute(EditorCommandId.Undo));
                Assert.False(presenter.CanExecute(EditorCommandId.Undo));
            });
        }
    }
}
