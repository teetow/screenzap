using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class TextOutlinePerfSmokeTests
    {
        [Fact]
        public void ThickOutlineRendering_CompletesWithinReasonableBudget()
        {
            var rendererType = typeof(screenzap.ImageEditor).Assembly.GetType("screenzap.EmojiTextRenderer");
            Assert.NotNull(rendererType);

            var method = rendererType!.GetMethod(
                "DrawText",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types:
                [
                    typeof(Graphics), typeof(string), typeof(PointF), typeof(Color), typeof(string),
                    typeof(float), typeof(FontStyle), typeof(float), typeof(Color?)
                ],
                modifiers: null);

            Assert.NotNull(method);

            using var bitmap = new Bitmap(1400, 900);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.White);

            var stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < 24; i++)
            {
                method!.Invoke(null, new object?[]
                {
                    graphics,
                    "Performance sanity check for thick outlines",
                    new PointF(20, 20 + (i % 8) * 90),
                    Color.Red,
                    "Segoe UI",
                    32f,
                    FontStyle.Bold,
                    8f,
                    Color.Black
                });
            }

            stopwatch.Stop();

            bool changed = false;
            for (int y = 0; y < bitmap.Height && !changed; y += 8)
            {
                for (int x = 0; x < bitmap.Width; x += 8)
                {
                    if (bitmap.GetPixel(x, y).ToArgb() != Color.White.ToArgb())
                    {
                        changed = true;
                        break;
                    }
                }
            }

            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"Expected outlined text rendering to stay responsive, but 24 thick draws took {stopwatch.Elapsed}.");
            Assert.True(changed, "Expected outlined text rendering to change at least some pixels in the output bitmap.");
        }
    }
}
