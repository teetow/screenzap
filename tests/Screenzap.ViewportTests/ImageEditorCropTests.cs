using System.Drawing;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class ImageEditorCropTests
    {
        [Fact]
        public void Crop_ResizesImage_ClearsSelection_AndSupportsUndoRedo()
        {
            StaTest.Run(() =>
            {
                using var editor = new screenzap.ImageEditor();
                using var source = new Bitmap(100, 60);
                using (var g = Graphics.FromImage(source))
                {
                    g.Clear(Color.White);
                }

                editor.LoadImage(source);
                var cropSelection = new Rectangle(10, 8, 40, 20);
                editor.SetSelectionForDiagnostics(cropSelection);

                var cropped = editor.ExecuteCropForDiagnostics();
                Assert.True(cropped);

                Assert.Equal(new Size(40, 20), editor.ViewportDiagnostics.ImagePixelSize);
                Assert.Equal(Rectangle.Empty, editor.SelectionDiagnostics.Selection);

                var presenter = (IClipboardDocumentPresenter)editor;
                Assert.True(presenter.CanExecute(EditorCommandId.Undo));
                Assert.True(presenter.TryExecute(EditorCommandId.Undo));

                Assert.Equal(new Size(100, 60), editor.ViewportDiagnostics.ImagePixelSize);
                Assert.Equal(cropSelection, editor.SelectionDiagnostics.Selection);

                Assert.True(presenter.CanExecute(EditorCommandId.Redo));
                Assert.True(presenter.TryExecute(EditorCommandId.Redo));

                Assert.Equal(new Size(40, 20), editor.ViewportDiagnostics.ImagePixelSize);
                Assert.Equal(Rectangle.Empty, editor.SelectionDiagnostics.Selection);
            });
        }

        [Fact]
        public void Crop_RepositionsFloatingLayersAndText_ToMatchNewOrigin_AndUndoRestores()
        {
            StaTest.Run(() =>
            {
                using var editor = new screenzap.ImageEditor();
                using var source = new Bitmap(100, 60);
                editor.LoadImage(source);

                editor.TestAddTextAnnotation(new Point(50, 40), "hello");

                using var pasted = new Bitmap(10, 10);
                editor.SetInternalClipboardImageForDiagnostics(pasted);
                Assert.True(editor.PasteFromClipboardForDiagnostics());
                var originalLayerFrame = editor.GetImageLayerFrameForTests(0);

                var cropSelection = new Rectangle(30, 20, 40, 30);
                editor.SetSelectionForDiagnostics(cropSelection);
                Assert.True(editor.ExecuteCropForDiagnostics());

                // Content is pinned in place: positions shift by the crop origin, not left untouched.
                Assert.Equal(1, editor.TestTextAnnotationCount);
                Assert.Contains("pos={X=20,Y=20}", editor.TestDescribeTextAnnotations());

                Assert.Equal(1, editor.ImageLayerCountForTests);
                var croppedLayerFrame = editor.GetImageLayerFrameForTests(0);
                Assert.Equal(originalLayerFrame.X - cropSelection.X, croppedLayerFrame.X);
                Assert.Equal(originalLayerFrame.Y - cropSelection.Y, croppedLayerFrame.Y);

                var presenter = (IClipboardDocumentPresenter)editor;
                Assert.True(presenter.TryExecute(EditorCommandId.Undo));

                Assert.Equal(originalLayerFrame, editor.GetImageLayerFrameForTests(0));
                Assert.Contains("pos={X=50,Y=40}", editor.TestDescribeTextAnnotations());

                Assert.True(presenter.TryExecute(EditorCommandId.Redo));
                var redoLayerFrame = editor.GetImageLayerFrameForTests(0);
                Assert.Equal(originalLayerFrame.X - cropSelection.X, redoLayerFrame.X);
                Assert.Equal(originalLayerFrame.Y - cropSelection.Y, redoLayerFrame.Y);
            });
        }

        [Fact]
        public void Crop_ClampsOutOfBoundsSelection_ToImageBounds()
        {
            StaTest.Run(() =>
            {
                using var editor = new screenzap.ImageEditor();
                using var source = new Bitmap(100, 60);
                editor.LoadImage(source);

                editor.SetSelectionForDiagnostics(new Rectangle(-10, -10, 30, 25));

                var cropped = editor.ExecuteCropForDiagnostics();
                Assert.True(cropped);

                Assert.Equal(new Size(20, 15), editor.ViewportDiagnostics.ImagePixelSize);
                Assert.Equal(Rectangle.Empty, editor.SelectionDiagnostics.Selection);
            });
        }
    }
}
