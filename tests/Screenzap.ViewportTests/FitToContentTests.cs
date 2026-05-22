using System;
using System.Drawing;
using System.Windows.Forms;
using screenzap.Components;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class FitToContentTests
    {
        [Fact]
        public void FitToContent_WithImage_ResizesHostClientToImagePlusChrome()
        {
            StaTest.Run(() =>
            {
                using var imageEditor = new screenzap.ImageEditor();
                using var textEditor = new screenzap.TextEditor();
                using var host = new ClipboardEditorHostForm(true, imageEditor, textEditor)
                {
                    SuppressActivation = true,
                    ShowInTaskbar = false
                };
                host.CreateControl();

                using var image = new Bitmap(400, 250);
                var imageData = new DataObject();
                imageData.SetData(DataFormats.Bitmap, true, image);
                Assert.True(host.TryShowClipboardData(imageData));

                var initialSize = host.Size;
                host.FitToContent();

                // ClientSize.Width should be >= image width (plus host + presenter chrome).
                // We don't assert exact equality because chrome dimensions depend on system
                // metrics, but the host should have grown horizontally if it was previously
                // smaller than the image, and should not have grown beyond reasonable bounds.
                Assert.True(
                    host.ClientSize.Width >= 400,
                    $"host ClientSize.Width = {host.ClientSize.Width}, expected >= 400 (image width)");
                Assert.True(
                    host.ClientSize.Height >= 250,
                    $"host ClientSize.Height = {host.ClientSize.Height}, expected >= 250 (image height)");

                // Chrome shouldn't bloat the window by more than ~500px horizontally — that
                // would indicate the chrome measurement is broken (e.g. measuring twice).
                Assert.True(host.ClientSize.Width <= 400 + 500);
                Assert.True(host.ClientSize.Height <= 250 + 500);
            });
        }

        [Fact]
        public void FitToContent_NoActivePresenter_DoesNotThrow()
        {
            StaTest.Run(() =>
            {
                using var imageEditor = new screenzap.ImageEditor();
                using var textEditor = new screenzap.TextEditor();
                using var host = new ClipboardEditorHostForm(true, imageEditor, textEditor)
                {
                    SuppressActivation = true,
                    ShowInTaskbar = false
                };
                host.CreateControl();
                var sizeBefore = host.Size;

                // No presenter activated, no content loaded → FitToContent must be a no-op.
                host.FitToContent();

                Assert.Equal(sizeBefore, host.Size);
            });
        }

        [Fact]
        public void FitToContent_ClampsToWorkingArea_WhenImageLargerThanScreen()
        {
            StaTest.Run(() =>
            {
                using var imageEditor = new screenzap.ImageEditor();
                using var textEditor = new screenzap.TextEditor();
                using var host = new ClipboardEditorHostForm(true, imageEditor, textEditor)
                {
                    SuppressActivation = true,
                    ShowInTaskbar = false
                };
                host.CreateControl();

                var workingArea = Screen.FromControl(host).WorkingArea;
                using var hugeImage = new Bitmap(workingArea.Width * 3, workingArea.Height * 3);
                var imageData = new DataObject();
                imageData.SetData(DataFormats.Bitmap, true, hugeImage);
                Assert.True(host.TryShowClipboardData(imageData));

                host.FitToContent();

                Assert.True(host.Size.Width <= workingArea.Width);
                Assert.True(host.Size.Height <= workingArea.Height);
            });
        }
    }
}
