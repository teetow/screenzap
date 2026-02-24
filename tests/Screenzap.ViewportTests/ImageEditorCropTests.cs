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
        }

        [Fact]
        public void Crop_ClampsOutOfBoundsSelection_ToImageBounds()
        {
            using var editor = new screenzap.ImageEditor();
            using var source = new Bitmap(100, 60);
            editor.LoadImage(source);

            editor.SetSelectionForDiagnostics(new Rectangle(-10, -10, 30, 25));

            var cropped = editor.ExecuteCropForDiagnostics();
            Assert.True(cropped);

            Assert.Equal(new Size(20, 15), editor.ViewportDiagnostics.ImagePixelSize);
            Assert.Equal(Rectangle.Empty, editor.SelectionDiagnostics.Selection);
        }
    }
}
