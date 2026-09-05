using System;
using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace Screenzap.ViewportTests
{
    /// <summary>
    /// The image must end up centred no matter when the viewport reaches its final size. The
    /// editor is warmed up and loaded while hidden, so the centring in LoadImage runs against a
    /// viewport that has not been laid out yet; everything that resizes it afterwards (the host
    /// being shown, the thumbnail strip appearing, DPI) has to carry the centring with it.
    /// </summary>
    public class ViewportCenteringTests
    {
        private static screenzap.ImageEditor NewEditor(Size formSize, Size imageSize)
            => EditorFixture.WithCanvas(imageSize, formSize: formSize);

        private static void AssertCentred(screenzap.ImageEditor editor)
        {
            var m = editor.TestViewportMetrics;
            var expectedX = (m.ClientSize.Width - m.ScaledImageSize.Width) / 2f;
            var expectedY = (m.ClientSize.Height - m.ScaledImageSize.Height) / 2f;

            // 1px of slack: CenterImage rounds the offset to a whole pixel.
            Assert.True(
                Math.Abs(m.PanOffset.X - expectedX) <= 1f && Math.Abs(m.PanOffset.Y - expectedY) <= 1f,
                $"pan={m.PanOffset} expected=({expectedX}, {expectedY}) " +
                $"client={m.ClientSize} scaled={m.ScaledImageSize}");
        }

        [Fact]
        public void ImageLoadedBeforeLayoutSettles_IsStillCentredAfterTheViewportGrows()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor(new Size(400, 300), new Size(100, 80));

                // The host is shown and the editor finally gets its real size.
                editor.TestSetSize(900, 700);

                AssertCentred(editor);
            });
        }

        [Fact]
        public void ImageStaysCentred_WhenTheViewportShrinks()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor(new Size(900, 700), new Size(120, 90));

                editor.TestSetSize(500, 400);

                AssertCentred(editor);
            });
        }

        [Fact]
        public void ImageStaysCentred_AcrossSeveralResizes()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor(new Size(300, 240), new Size(64, 48));

                foreach (var size in new[] { new Size(700, 500), new Size(420, 620), new Size(1000, 800) })
                {
                    editor.TestSetSize(size.Width, size.Height);
                    AssertCentred(editor);
                }
            });
        }

        [Fact]
        public void ImageLargerThanTheViewport_IsAlsoCentred()
        {
            StaTest.Run(() =>
            {
                // Overflows the viewport on both axes: the centred offset goes negative.
                using var editor = NewEditor(new Size(400, 320), new Size(2000, 1500));

                editor.TestSetSize(900, 700);

                AssertCentred(editor);
            });
        }

        [Fact]
        public void PannedView_TravelsWithTheWindow_InsteadOfSnappingBackToCentre()
        {
            StaTest.Run(() =>
            {
                using var editor = NewEditor(new Size(600, 480), new Size(1200, 900));

                var before = editor.TestViewportMetrics.PanOffset;
                editor.TestPanViewportBy(new Size(-120, -60));
                var pannedMetrics = editor.TestViewportMetrics;
                Assert.NotEqual(before, pannedMetrics.PanOffset);

                // The viewport centre moves by half the VIEWPORT delta (which is not the form
                // delta — the toolbars take their cut), and the image point under that centre
                // must stay put, so the pan slides by the same amount instead of re-centring.
                editor.TestSetSize(900, 680);
                var after = editor.TestViewportMetrics;

                var expectedX = pannedMetrics.PanOffset.X
                    + (after.ClientSize.Width - pannedMetrics.ClientSize.Width) / 2f;
                var expectedY = pannedMetrics.PanOffset.Y
                    + (after.ClientSize.Height - pannedMetrics.ClientSize.Height) / 2f;

                Assert.Equal(expectedX, after.PanOffset.X, 0);
                Assert.Equal(expectedY, after.PanOffset.Y, 0);
            });
        }
    }
}
