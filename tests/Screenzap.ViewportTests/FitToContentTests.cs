using System;
using System.Drawing;
using System.Windows.Forms;
using screenzap.Components;
using screenzap.Components.Shared;
using screenzap.lib;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class FitToContentTests
    {
        /// <summary>
        /// Builds a host with an image editor, loads an image of the given size, and hands both to
        /// the assertions. The host is never shown: FitToContent runs before the window appears in
        /// the real app too.
        /// </summary>
        private static void WithLoadedEditor(
            Size imageSize,
            Action<ClipboardEditorHostForm, screenzap.ImageEditor> assert,
            Action<ClipboardEditorHostForm>? beforeLoad = null)
        {
            StaTest.Run(() =>
            {
                using var imageEditor = new screenzap.ImageEditor();
                using var host = new ClipboardEditorHostForm(true, imageEditor)
                {
                    SuppressActivation = true,
                    ShowInTaskbar = false
                };
                host.CreateControl();
                beforeLoad?.Invoke(host);

                using var image = new Bitmap(imageSize.Width, imageSize.Height);
                var imageData = new DataObject();
                imageData.SetData(DataFormats.Bitmap, true, image);
                Assert.True(host.TryShowClipboardData(imageData));

                assert(host, imageEditor);
            });
        }

        private static Rectangle WorkingAreaFor(Control control) => Screen.FromControl(control).WorkingArea;

        private static Size CapFor(Rectangle workingArea) => new Size(
            (int)Math.Round(workingArea.Width * WindowLayoutHelper.MaxWorkingAreaFraction),
            (int)Math.Round(workingArea.Height * WindowLayoutHelper.MaxWorkingAreaFraction));

        [Fact]
        public void FitToContent_WithImage_ResizesHostClientToImagePlusChrome()
        {
            WithLoadedEditor(new Size(400, 250), (host, _) =>
            {
                host.FitToContent();

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
                using var host = new ClipboardEditorHostForm(true, imageEditor)
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

        /// <summary>
        /// A thumbnail must not open a window padded out past the form's own minimum. The floor
        /// used to be MinimumSize plus the border and title bar, because a client size was being
        /// clamped against an outer one.
        /// </summary>
        [Fact]
        public void FitToContent_ImageSmallerThanMinimum_StopsAtMinimumSize()
        {
            WithLoadedEditor(new Size(200, 120), (host, _) =>
            {
                host.FitToContent();

                Assert.Equal(host.MinimumSize, host.Size);
            });
        }

        [Fact]
        public void FitToContent_ClampsToWorkingArea_WhenImageLargerThanScreen()
        {
            StaTest.Run(() =>
            {
                using var imageEditor = new screenzap.ImageEditor();
                using var host = new ClipboardEditorHostForm(true, imageEditor)
                {
                    SuppressActivation = true,
                    ShowInTaskbar = false
                };
                host.CreateControl();

                var workingArea = WorkingAreaFor(host);
                using var hugeImage = new Bitmap(workingArea.Width * 3, workingArea.Height * 3);
                var imageData = new DataObject();
                imageData.SetData(DataFormats.Bitmap, true, hugeImage);
                Assert.True(host.TryShowClipboardData(imageData));

                host.FitToContent();

                Assert.True(host.Size.Width <= workingArea.Width);
                Assert.True(host.Size.Height <= workingArea.Height);
            });
        }

        /// <summary>
        /// The common case: a capture at least as big as the screen it came from. It must stop at
        /// the cap rather than growing to the full working area, which looked like a maximized
        /// window that had not actually been maximized.
        /// </summary>
        [Fact]
        public void FitToContent_OversizedImage_StopsAtWorkingAreaFraction()
        {
            StaTest.Run(() =>
            {
                using var imageEditor = new screenzap.ImageEditor();
                using var host = new ClipboardEditorHostForm(true, imageEditor)
                {
                    SuppressActivation = true,
                    ShowInTaskbar = false
                };
                host.CreateControl();

                var workingArea = WorkingAreaFor(host);
                var cap = CapFor(workingArea);
                using var hugeImage = new Bitmap(workingArea.Width * 2, workingArea.Height * 2);
                var imageData = new DataObject();
                imageData.SetData(DataFormats.Bitmap, true, hugeImage);
                Assert.True(host.TryShowClipboardData(imageData));

                host.FitToContent();

                Assert.True(
                    host.Size.Width <= Math.Max(cap.Width, host.MinimumSize.Width),
                    $"width {host.Size.Width} exceeded the {cap.Width} cap");
                Assert.True(
                    host.Size.Height <= Math.Max(cap.Height, host.MinimumSize.Height),
                    $"height {host.Size.Height} exceeded the {cap.Height} cap");

                // Still a real window, not collapsed to the minimum.
                Assert.True(host.Size.Width > workingArea.Width / 2);
            });
        }

        /// <summary>
        /// Whatever size the window settles on, the whole picture has to be inside it. The editor
        /// loads images at 1:1, so an oversized capture used to open center-cropped.
        /// </summary>
        [Fact]
        public void FitToContent_OversizedImage_ZoomsOutUntilTheWholePictureFits()
        {
            StaTest.Run(() =>
            {
                using var imageEditor = new screenzap.ImageEditor();
                using var host = new ClipboardEditorHostForm(true, imageEditor)
                {
                    SuppressActivation = true,
                    ShowInTaskbar = false
                };
                host.CreateControl();

                var workingArea = WorkingAreaFor(host);
                using var hugeImage = new Bitmap(workingArea.Width * 2, workingArea.Height * 2);
                var imageData = new DataObject();
                imageData.SetData(DataFormats.Bitmap, true, hugeImage);
                Assert.True(host.TryShowClipboardData(imageData));

                host.FitToContent();

                var metrics = imageEditor.ViewportDiagnostics;
                Assert.True(metrics.ZoomLevel < 1m, $"zoom {metrics.ZoomLevel} should have been reduced");
                Assert.True(
                    metrics.ScaledImageSize.Width <= metrics.ClientSize.Width + 1,
                    $"scaled width {metrics.ScaledImageSize.Width} overflows canvas {metrics.ClientSize.Width}");
                Assert.True(
                    metrics.ScaledImageSize.Height <= metrics.ClientSize.Height + 1,
                    $"scaled height {metrics.ScaledImageSize.Height} overflows canvas {metrics.ClientSize.Height}");
            });
        }

        /// <summary>
        /// An image that already fits is shown at 1:1 — fitting only ever zooms out.
        /// </summary>
        [Fact]
        public void FitToContent_ImageThatFits_StaysAtNativeZoom()
        {
            WithLoadedEditor(new Size(640, 400), (host, editor) =>
            {
                host.FitToContent();

                Assert.Equal(1m, editor.ViewportDiagnostics.ZoomLevel);
            });
        }

        /// <summary>
        /// A panoramic capture pinned to the width cap renders short; the window must follow the
        /// picture down rather than keeping the height the unscaled image would have needed and
        /// wrapping it in empty canvas.
        /// </summary>
        [Fact]
        public void FitToContent_PanoramicImage_DoesNotLeaveEmptyBands()
        {
            StaTest.Run(() =>
            {
                using var imageEditor = new screenzap.ImageEditor();
                using var host = new ClipboardEditorHostForm(true, imageEditor)
                {
                    SuppressActivation = true,
                    ShowInTaskbar = false
                };
                host.CreateControl();

                var workingArea = WorkingAreaFor(host);
                // Twice as wide as the screen, as tall as it: the width cap forces the zoom and
                // the height has to come along with it.
                using var panorama = new Bitmap(workingArea.Width * 2, workingArea.Height);
                var panoramaHeight = panorama.Height;
                var imageData = new DataObject();
                imageData.SetData(DataFormats.Bitmap, true, panorama);
                Assert.True(host.TryShowClipboardData(imageData));

                host.FitToContent();

                var metrics = imageEditor.ViewportDiagnostics;
                Assert.True(metrics.ZoomLevel < 1m);

                // Height the window would have taken if it had been sized for the picture at 1:1
                // and then zoomed out inside that — what the single-pass measurement produced.
                var fixedChrome = host.Size.Height - metrics.ClientSize.Height;
                var unscaledDemand = panoramaHeight + fixedChrome;
                Assert.True(
                    host.Size.Height < unscaledDemand,
                    $"window height {host.Size.Height} did not come in under the {unscaledDemand} the unscaled picture wanted");

                // And the canvas hugs the scaled picture — unless the form's own minimum height
                // is what is holding the window open, which no amount of measuring can shrink.
                var slack = metrics.ClientSize.Height - metrics.ScaledImageSize.Height;
                Assert.True(
                    slack < 8 || host.Size.Height <= host.MinimumSize.Height,
                    $"{slack:F0}px of empty canvas under a {metrics.ScaledImageSize.Height:F0}px picture");
            });
        }

        /// <summary>
        /// Once the user has sized the window, the guess stops: opening another item must not
        /// resize it out from under them. The content still gets fitted into what they chose.
        /// </summary>
        [Fact]
        public void FitToContent_AfterUserResize_LeavesTheWindowAloneButStillFitsTheContent()
        {
            StaTest.Run(() =>
            {
                using var imageEditor = new screenzap.ImageEditor();
                using var host = new ClipboardEditorHostForm(true, imageEditor)
                {
                    SuppressActivation = true,
                    ShowInTaskbar = false
                };
                host.CreateControl();
                host.MarkShownToUserForDiagnostics();

                var chosen = new Size(host.MinimumSize.Width + 120, host.MinimumSize.Height + 80);
                host.Size = chosen;
                Assert.True(host.HasUserSizedWindow, "a resize the host did not perform is the user's");

                var workingArea = WorkingAreaFor(host);
                using var hugeImage = new Bitmap(workingArea.Width * 2, workingArea.Height * 2);
                var imageData = new DataObject();
                imageData.SetData(DataFormats.Bitmap, true, hugeImage);
                Assert.True(host.TryShowClipboardData(imageData));

                host.FitToContent();

                Assert.Equal(chosen, host.Size);

                var metrics = imageEditor.ViewportDiagnostics;
                Assert.True(metrics.ZoomLevel < 1m);
                Assert.True(metrics.ScaledImageSize.Width <= metrics.ClientSize.Width + 1);
                Assert.True(metrics.ScaledImageSize.Height <= metrics.ClientSize.Height + 1);
            });
        }

        /// <summary>
        /// The host resizing itself is not the user doing it, or the very first fit would disable
        /// every fit after it.
        /// </summary>
        [Fact]
        public void FitToContent_OwnResize_DoesNotCountAsAUserResize()
        {
            WithLoadedEditor(
                new Size(1200, 800),
                (host, _) =>
                {
                    host.FitToContent();
                    Assert.False(host.HasUserSizedWindow);
                },
                beforeLoad: host => host.MarkShownToUserForDiagnostics());
        }

        /// <summary>
        /// Growing from the top-left walked the window toward the bottom-right corner across
        /// successive opens; it grows around its centre instead.
        /// </summary>
        [Fact]
        public void FitToContent_KeepsTheWindowCentredOnWhereItWas()
        {
            WithLoadedEditor(
                new Size(1100, 700),
                (host, _) =>
                {
                    var centreBefore = new Point(
                        host.Bounds.Left + host.Bounds.Width / 2,
                        host.Bounds.Top + host.Bounds.Height / 2);

                    host.FitToContent();

                    var centreAfter = new Point(
                        host.Bounds.Left + host.Bounds.Width / 2,
                        host.Bounds.Top + host.Bounds.Height / 2);

                    // Only the working-area clamp may move the centre, and this window fits.
                    Assert.InRange(centreAfter.X, centreBefore.X - 2, centreBefore.X + 2);
                    Assert.InRange(centreAfter.Y, centreBefore.Y - 2, centreBefore.Y + 2);
                },
                beforeLoad: host => host.MarkShownToUserForDiagnostics());
        }
    }
}
