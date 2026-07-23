using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class AlphaViewToggleTests
    {
        [Fact]
        public void KeyDown_M_TogglesAlphaViewEnabled()
        {
            StaTest.Run(() =>
            {
                using var editor = new screenzap.ImageEditor();
                using var canvas = new Bitmap(10, 10);
                editor.LoadImage(canvas);

                Assert.True(editor.TestAlphaViewEnabled);

                editor.TestFireKeyDown(Keys.M);
                Assert.False(editor.TestAlphaViewEnabled);

                editor.TestFireKeyDown(Keys.M);
                Assert.True(editor.TestAlphaViewEnabled);
            });
        }

        [Fact]
        public void KeyDown_M_WithoutEditableImage_DoesNothing()
        {
            StaTest.Run(() =>
            {
                using var editor = new screenzap.ImageEditor();

                editor.TestFireKeyDown(Keys.M);

                Assert.True(editor.TestAlphaViewEnabled);
            });
        }

        [Fact]
        public void LoadImage_ResetsAlphaViewEnabledToTrue()
        {
            StaTest.Run(() =>
            {
                using var editor = new screenzap.ImageEditor();
                using var first = new Bitmap(10, 10);
                editor.LoadImage(first);
                editor.TestFireKeyDown(Keys.M);
                Assert.False(editor.TestAlphaViewEnabled);

                using var second = new Bitmap(10, 10);
                editor.LoadImage(second);

                Assert.True(editor.TestAlphaViewEnabled);
            });
        }

        [Fact]
        public void OnPaint_FlatMode_RevealsRawRgbUnderMaskedPixels_WhileOpaquePixelsAreUnaffected()
        {
            StaTest.Run(() =>
            {
                using var control = new ImageViewportControl
                {
                    ClientSize = new Size(8, 8)
                };

                using var source = new Bitmap(2, 2, PixelFormat.Format32bppArgb);
                source.SetPixel(0, 0, Color.FromArgb(0, 200, 10, 20));   // fully transparent - "masked"
                source.SetPixel(1, 1, Color.FromArgb(255, 0, 0, 255));  // opaque blue

                control.Image = source;
                control.ZoomLevel = 4m; // 2x2 image -> fills the 8x8 client exactly, one image pixel per 4x4 block

                using (var alphaView = new Bitmap(8, 8))
                {
                    control.DrawToBitmap(alphaView, new Rectangle(0, 0, 8, 8));

                    // Masked pixel: fully covered by the checkerboard (light quadrant at this offset).
                    var maskedPixel = alphaView.GetPixel(1, 1);
                    Assert.Equal(205, maskedPixel.R);
                    Assert.Equal(205, maskedPixel.G);
                    Assert.Equal(205, maskedPixel.B);

                    // Opaque pixel renders normally regardless of view mode.
                    var opaquePixel = alphaView.GetPixel(6, 6);
                    Assert.Equal(Color.FromArgb(255, 0, 0, 255).ToArgb(), opaquePixel.ToArgb());
                }

                control.AlphaViewEnabled = false;

                using (var flatView = new Bitmap(8, 8))
                {
                    control.DrawToBitmap(flatView, new Rectangle(0, 0, 8, 8));

                    // Flat mode reveals the raw RGB that alpha was hiding.
                    var revealedPixel = flatView.GetPixel(1, 1);
                    Assert.Equal(200, revealedPixel.R);
                    Assert.Equal(10, revealedPixel.G);
                    Assert.Equal(20, revealedPixel.B);

                    var opaquePixel = flatView.GetPixel(6, 6);
                    Assert.Equal(Color.FromArgb(255, 0, 0, 255).ToArgb(), opaquePixel.ToArgb());
                }
            });
        }
    }
}
