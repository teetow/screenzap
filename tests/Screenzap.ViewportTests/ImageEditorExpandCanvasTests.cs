using System.Drawing;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class ImageEditorExpandCanvasTests
    {
        // Mirrors ImageEditor.ExpandCanvasPaddingPixels.
        private const int Padding = 8;

        [Fact]
        public void ExpandCanvas_RepositionsFloatingLayersAndText_ToMatchNewOrigin_AndUndoRestores()
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

                var presenter = (IClipboardDocumentPresenter)editor;
                Assert.True(presenter.TryExecute(EditorCommandId.ExpandCanvas));

                // Canvas grew by Padding on every side; content shifts by (+Padding, +Padding) so it
                // stays pinned to the same pixels rather than jumping toward the old origin.
                Assert.Equal(new Size(100 + Padding * 2, 60 + Padding * 2), editor.ViewportDiagnostics.ImagePixelSize);
                Assert.Contains($"pos={{X={50 + Padding},Y={40 + Padding}}}", editor.TestDescribeTextAnnotations());

                var expandedLayerFrame = editor.GetImageLayerFrameForTests(0);
                Assert.Equal(originalLayerFrame.X + Padding, expandedLayerFrame.X);
                Assert.Equal(originalLayerFrame.Y + Padding, expandedLayerFrame.Y);

                Assert.True(presenter.TryExecute(EditorCommandId.Undo));
                Assert.Equal(new Size(100, 60), editor.ViewportDiagnostics.ImagePixelSize);
                Assert.Equal(originalLayerFrame, editor.GetImageLayerFrameForTests(0));
                Assert.Contains("pos={X=50,Y=40}", editor.TestDescribeTextAnnotations());

                Assert.True(presenter.TryExecute(EditorCommandId.Redo));
                var redoLayerFrame = editor.GetImageLayerFrameForTests(0);
                Assert.Equal(originalLayerFrame.X + Padding, redoLayerFrame.X);
                Assert.Equal(originalLayerFrame.Y + Padding, redoLayerFrame.Y);
            });
        }
    }
}
