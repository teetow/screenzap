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

        [Fact]
        public void CheckerboardColors_AreConfigurable_AndPaintThroughTransparentPixels()
        {
            StaTest.Run(() =>
            {
                // The checkerboard squares are 8px, so the viewport must be several squares wide to
                // show both colors.
                using var control = new ImageViewportControl
                {
                    ClientSize = new Size(32, 32),
                    CheckerboardLightColor = Color.FromArgb(10, 20, 30),
                    CheckerboardDarkColor = Color.FromArgb(40, 50, 60),
                };

                Assert.Equal(Color.FromArgb(10, 20, 30), control.CheckerboardLightColor);
                Assert.Equal(Color.FromArgb(40, 50, 60), control.CheckerboardDarkColor);

                using var transparent = new Bitmap(8, 8, PixelFormat.Format32bppArgb); // fully transparent
                control.Image = transparent;
                control.ZoomLevel = 4m; // 8x8 image -> 32x32 dest, spanning multiple checker squares

                using var render = new Bitmap(32, 32);
                control.DrawToBitmap(render, new Rectangle(0, 0, 32, 32));

                // Every rendered pixel under the transparent image must be one of the two custom
                // checkerboard colors — never the old default greys.
                var seen = new System.Collections.Generic.HashSet<int>();
                for (int y = 0; y < 32; y++)
                    for (int x = 0; x < 32; x++)
                        seen.Add(render.GetPixel(x, y).ToArgb());

                Assert.Contains(Color.FromArgb(255, 10, 20, 30).ToArgb(), seen);
                Assert.Contains(Color.FromArgb(255, 40, 50, 60).ToArgb(), seen);
                Assert.DoesNotContain(Color.FromArgb(255, 205, 205, 205).ToArgb(), seen);
            });
        }
    }
}
