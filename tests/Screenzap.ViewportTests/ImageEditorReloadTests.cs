using System.Drawing;
using System.Windows.Forms;
using screenzap.Components;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class ImageEditorReloadTests
    {
        [Fact]
        public void SelectionCopy_PreservesSelection_ForInEditorPasteWorkflow()
        {
            using var imageEditor = new screenzap.ImageEditor();
            using var image = new Bitmap(48, 32);
            using (var graphics = Graphics.FromImage(image))
            {
                graphics.Clear(Color.AliceBlue);
            }

            imageEditor.LoadImage(image);
            var sourceSelection = new Rectangle(6, 4, 14, 10);
            imageEditor.SetSelectionForDiagnostics(sourceSelection);
            Assert.True(imageEditor.CopySelectionToClipboardForDiagnostics());

            var pasteTarget = new Rectangle(24, 12, 1, 1);
            imageEditor.SetSelectionForDiagnostics(pasteTarget);
            Assert.True(imageEditor.PasteFromClipboardForDiagnostics());

            var pastedSelection = imageEditor.SelectionDiagnostics.Selection;
            Assert.Equal(new Point(pasteTarget.X, pasteTarget.Y), pastedSelection.Location);
            Assert.Equal(new Size(sourceSelection.Width, sourceSelection.Height), pastedSelection.Size);
            Assert.False(imageEditor.ClipboardHasPendingReloadForDiagnostics);
        }

        [Fact]
        public void Reload_UpdatesHostIndicator_AndRespectsDirtyConfirmation()
        {
            using var imageEditor = new screenzap.ImageEditor();
            using var textEditor = new screenzap.TextEditor();
            using var host = new ClipboardEditorHostForm(imageEditor, textEditor)
            {
                SuppressActivation = true,
                ShowInTaskbar = false
            };

            imageEditor.RequestTextEditor = () => textEditor;
            host.CreateControl();

            using var initialImage = new Bitmap(20, 10);
            var imageData = new DataObject();
            imageData.SetData(DataFormats.Bitmap, true, initialImage);
            Assert.True(host.TryShowClipboardData(imageData));
            Assert.IsType<screenzap.ImageEditor>(host.ActivePresenter);

            imageEditor.SetPendingReloadForDiagnostics(hasPendingReload: true, useTextTarget: false);
            Assert.True(host.HasPendingReloadIndicator);

            imageEditor.SetHasUnsavedChangesForDiagnostics(true);
            imageEditor.ConfirmReloadWhenDirtyOverrideForDiagnostics = () => false;
            imageEditor.ClipboardImageProviderForDiagnostics = () => new Bitmap(40, 30);

            var presenter = (IClipboardDocumentPresenter)imageEditor;
            Assert.True(presenter.TryExecute(EditorCommandId.Reload));
            Assert.Equal(new Size(20, 10), imageEditor.ViewportDiagnostics.ImagePixelSize);
            Assert.True(imageEditor.ClipboardHasPendingReloadForDiagnostics);
            Assert.True(host.HasPendingReloadIndicator);

            imageEditor.ConfirmReloadWhenDirtyOverrideForDiagnostics = () => true;
            Assert.True(presenter.TryExecute(EditorCommandId.Reload));
            Assert.Equal(new Size(40, 30), imageEditor.ViewportDiagnostics.ImagePixelSize);
            Assert.False(imageEditor.ClipboardHasPendingReloadForDiagnostics);
            Assert.False(host.HasPendingReloadIndicator);
        }

        [Fact]
        public void Reload_WithPendingText_SwitchesToTextEditorAndClearsIndicator()
        {
            using var imageEditor = new screenzap.ImageEditor();
            using var textEditor = new screenzap.TextEditor();
            using var host = new ClipboardEditorHostForm(imageEditor, textEditor)
            {
                SuppressActivation = true,
                ShowInTaskbar = false
            };

            imageEditor.RequestTextEditor = () => textEditor;
            host.CreateControl();

            using var initialImage = new Bitmap(16, 16);
            var imageData = new DataObject();
            imageData.SetData(DataFormats.Bitmap, true, initialImage);
            Assert.True(host.TryShowClipboardData(imageData));
            Assert.IsType<screenzap.ImageEditor>(host.ActivePresenter);

            imageEditor.SetPendingReloadForDiagnostics(hasPendingReload: true, useTextTarget: true);
            imageEditor.ConfirmReloadWhenDirtyOverrideForDiagnostics = () => true;
            imageEditor.ClipboardImageProviderForDiagnostics = () => null;
            imageEditor.ClipboardTextProviderForDiagnostics = () => "clipboard text";

            var presenter = (IClipboardDocumentPresenter)imageEditor;
            Assert.True(presenter.TryExecute(EditorCommandId.Reload));

            Assert.IsType<screenzap.TextEditor>(host.ActivePresenter);
            Assert.Equal("clipboard text", textEditor.CurrentTextForDiagnostics);
            Assert.False(host.HasPendingReloadIndicator);
        }
    }
}
