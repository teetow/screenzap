using System.Drawing;
using screenzap;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests
{
    /// <summary>
    /// The Tools/View menu items route through IClipboardDocumentPresenter.TryExecute/CanExecute
    /// (the same path the host's ExecuteCommand uses), so verify the editor honors the tool commands.
    /// </summary>
    public class ToolMenuCommandTests
    {
        private static ImageEditor EditorWithImage(int w = 80, int h = 60)
        {
            var editor = new ImageEditor();
            var canvas = new Bitmap(w, h);
            using (var g = Graphics.FromImage(canvas))
                g.Clear(Color.White);
            editor.LoadImage(canvas);
            canvas.Dispose();
            return editor;
        }

        [Fact]
        public void ToolCommands_AreDisabledWithoutImage_EnabledWithImage()
        {
            StaTest.Run(() =>
            {
                using var empty = new ImageEditor();
                var emptyPresenter = (IClipboardDocumentPresenter)empty;
                Assert.False(emptyPresenter.CanExecute(EditorCommandId.CropTool));
                Assert.False(emptyPresenter.CanExecute(EditorCommandId.FreeRotateTool));

                using var editor = EditorWithImage();
                var presenter = (IClipboardDocumentPresenter)editor;
                Assert.True(presenter.CanExecute(EditorCommandId.CropTool));
                Assert.True(presenter.CanExecute(EditorCommandId.CensorTool));
                Assert.True(presenter.CanExecute(EditorCommandId.ColorCorrect));
                Assert.True(presenter.CanExecute(EditorCommandId.ToggleTransparencyGrid));
            });
        }

        [Fact]
        public void ArrowToolCommand_ActivatesArrowDrawingTool()
        {
            StaTest.Run(() =>
            {
                using var editor = EditorWithImage();
                var presenter = (IClipboardDocumentPresenter)editor;

                Assert.True(presenter.TryExecute(EditorCommandId.ArrowTool));
                Assert.Equal(DrawingTool.Arrow, editor.TestActiveDrawingTool);
            });
        }

        [Fact]
        public void FreeRotateToolCommand_ActivatesFreeRotate()
        {
            StaTest.Run(() =>
            {
                using var editor = EditorWithImage();
                var presenter = (IClipboardDocumentPresenter)editor;

                Assert.True(presenter.TryExecute(EditorCommandId.FreeRotateTool));
                Assert.True(editor.TestIsFreeRotateToolActive);
            });
        }

        [Fact]
        public void ToggleTransparencyGridCommand_FlipsAlphaView()
        {
            StaTest.Run(() =>
            {
                using var editor = EditorWithImage();
                var presenter = (IClipboardDocumentPresenter)editor;
                Assert.True(editor.TestAlphaViewEnabled);

                Assert.True(presenter.TryExecute(EditorCommandId.ToggleTransparencyGrid));
                Assert.False(editor.TestAlphaViewEnabled);
            });
        }

        [Fact]
        public void RotateRightCommand_RotatesImage90()
        {
            StaTest.Run(() =>
            {
                using var editor = EditorWithImage(80, 60);
                var presenter = (IClipboardDocumentPresenter)editor;

                Assert.True(presenter.TryExecute(EditorCommandId.RotateRight));

                using var after = editor.CloneBaseBitmapForTests()!;
                // 90° rotation swaps width/height.
                Assert.Equal(new Size(60, 80), after.Size);
            });
        }
    }
}
