using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using screenzap.Components;
using screenzap.lib;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class LiveAlphaCaptureTests
    {
        // ---- ComputeRgbThumbprint ----

        [Fact]
        public void RgbThumbprint_IsAlphaInsensitive_ForSameRgb()
        {
            StaTest.Run(() =>
            {
                using var opaque = MakeImage(Color.FromArgb(255, 40, 90, 160));
                using var transparent = MakeImage(Color.FromArgb(0, 40, 90, 160)); // same RGB, alpha=0

                var a = ClipboardImageDecoder.ComputeRgbThumbprint(opaque);
                var b = ClipboardImageDecoder.ComputeRgbThumbprint(transparent);

                Assert.Equal(a, b);
            });
        }

        [Fact]
        public void RgbThumbprint_DiffersForDifferentPictures()
        {
            StaTest.Run(() =>
            {
                using var red = MakeImage(Color.Red);
                using var blue = MakeImage(Color.Blue);

                Assert.NotEqual(
                    ClipboardImageDecoder.ComputeRgbThumbprint(red),
                    ClipboardImageDecoder.ComputeRgbThumbprint(blue));
            });
        }

        // ---- TrySubstituteLiveAlpha ----

        [Fact]
        public void Substitute_UsesCandidate_WhenRgbMatchesDecodedOpaque()
        {
            StaTest.Run(() =>
            {
                using var alpha = MakeImage(Color.FromArgb(128, 200, 30, 40), PixelFormat.Format32bppArgb);
                using var opaqueSamePicture = MakeImage(Color.FromArgb(255, 200, 30, 40));
                var candidate = new SystemClipboardHistoryService.LiveAlphaCandidate(alpha);

                using var result = SystemClipboardHistoryService.TrySubstituteLiveAlpha(opaqueSamePicture, candidate, isNewest: false);

                Assert.NotNull(result);
                Assert.Equal(128, result!.GetPixel(0, 0).A); // got the alpha version
            });
        }

        [Fact]
        public void Substitute_KeepsWinrtBitmap_WhenPictureDiffers()
        {
            StaTest.Run(() =>
            {
                using var alpha = MakeImage(Color.FromArgb(128, 10, 20, 30));
                using var differentPicture = MakeImage(Color.FromArgb(255, 240, 250, 90));
                var candidate = new SystemClipboardHistoryService.LiveAlphaCandidate(alpha);

                var result = SystemClipboardHistoryService.TrySubstituteLiveAlpha(differentPicture, candidate, isNewest: true);

                Assert.Null(result); // fall back to the WinRT-decoded bitmap
            });
        }

        [Fact]
        public void Substitute_BackfillsNewestFromCandidate_WhenDecodeFailed()
        {
            StaTest.Run(() =>
            {
                using var alpha = MakeImage(Color.FromArgb(64, 5, 6, 7));
                var candidate = new SystemClipboardHistoryService.LiveAlphaCandidate(alpha);

                using var result = SystemClipboardHistoryService.TrySubstituteLiveAlpha(winrtDecoded: null, candidate, isNewest: true);

                Assert.NotNull(result);
                Assert.Equal(64, result!.GetPixel(0, 0).A);
            });
        }

        [Fact]
        public void Substitute_DoesNotBackfillOlderItems_WhenDecodeFailed()
        {
            StaTest.Run(() =>
            {
                using var alpha = MakeImage(Color.FromArgb(64, 5, 6, 7));
                var candidate = new SystemClipboardHistoryService.LiveAlphaCandidate(alpha);

                var result = SystemClipboardHistoryService.TrySubstituteLiveAlpha(winrtDecoded: null, candidate, isNewest: false);

                Assert.Null(result);
            });
        }

        // ---- TryDecodePackedDib ----

        [Fact]
        public void TryDecodePackedDib_DecodesHeaderlessDib()
        {
            StaTest.Run(() =>
            {
                // Build a proper BMP file, then strip the 14-byte BITMAPFILEHEADER to get the raw
                // packed DIB that Windows clipboard history hands back for some items.
                using var source = MakeImage(Color.FromArgb(255, 12, 34, 56), PixelFormat.Format24bppRgb, 4, 3);
                using var bmpFile = new MemoryStream();
                source.Save(bmpFile, ImageFormat.Bmp);
                var full = bmpFile.ToArray();
                var dibOnly = new byte[full.Length - 14];
                System.Array.Copy(full, 14, dibOnly, 0, dibOnly.Length);

                using var decoded = ClipboardImageDecoder.TryDecodePackedDib(dibOnly);

                Assert.NotNull(decoded);
                Assert.Equal(4, decoded!.Width);
                Assert.Equal(3, decoded.Height);
                Assert.Equal(Color.FromArgb(255, 12, 34, 56).ToArgb(), decoded.GetPixel(0, 0).ToArgb());
            });
        }

        [Fact]
        public void TryDecodePackedDib_ReturnsNull_ForNonDibBytes()
        {
            var junk = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            Assert.Null(ClipboardImageDecoder.TryDecodePackedDib(junk));
        }

        private static Bitmap MakeImage(Color color, PixelFormat format = PixelFormat.Format32bppArgb, int w = 3, int h = 3)
        {
            // SetPixel writes straight (non-premultiplied) ARGB, so RGB is preserved even where
            // alpha is 0 — matching how a real PNG/BMP decodes. Graphics.FillRectangle would
            // premultiply the brush and destroy RGB under low/zero alpha.
            var bmp = new Bitmap(w, h, format);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bmp.SetPixel(x, y, color);
                }
            }
            return bmp;
        }
    }
}
