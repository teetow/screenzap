using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using screenzap.lib;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class ClipboardImageDecoderTests
    {
        [Fact]
        public void TryRead_PrefersPngFormat_OverOpaqueLegacyBitmap()
        {
            StaTest.Run(() =>
            {
                using var pngSource = new Bitmap(2, 2, PixelFormat.Format32bppArgb);
                pngSource.SetPixel(0, 0, Color.FromArgb(128, 255, 0, 0));
                using var pngStream = new MemoryStream();
                pngSource.Save(pngStream, ImageFormat.Png);
                pngStream.Position = 0;

                using var opaqueLegacy = new Bitmap(2, 2);
                opaqueLegacy.SetPixel(0, 0, Color.Red);

                var data = new DataObject();
                data.SetData("PNG", pngStream);
                data.SetData(DataFormats.Bitmap, true, opaqueLegacy);

                Assert.True(ClipboardImageDecoder.HasAlphaCapableFormat(data));

                using var decoded = ClipboardImageDecoder.TryRead(data);
                Assert.NotNull(decoded);
                Assert.Equal(128, decoded!.GetPixel(0, 0).A);
            });
        }

        [Fact]
        public void TryRead_AcceptsImagePngFormatName_OverOpaqueLegacyBitmap()
        {
            // Regression test: XnView (and other MIME-style clipboard writers) register the
            // alpha-safe PNG payload as "image/png" rather than the Chromium/Firefox convention
            // "PNG". Confirmed via a live clipboard dump against a real XnView copy.
            StaTest.Run(() =>
            {
                using var pngSource = new Bitmap(2, 2, PixelFormat.Format32bppArgb);
                pngSource.SetPixel(0, 0, Color.FromArgb(64, 10, 20, 30));
                using var pngStream = new MemoryStream();
                pngSource.Save(pngStream, ImageFormat.Png);
                pngStream.Position = 0;

                using var opaqueLegacy = new Bitmap(2, 2);
                opaqueLegacy.SetPixel(0, 0, Color.Red);

                var data = new DataObject();
                data.SetData("image/png", pngStream);
                data.SetData(DataFormats.Bitmap, true, opaqueLegacy);

                Assert.True(ClipboardImageDecoder.HasAlphaCapableFormat(data));

                using var decoded = ClipboardImageDecoder.TryRead(data);
                Assert.NotNull(decoded);
                Assert.Equal(64, decoded!.GetPixel(0, 0).A);
            });
        }

        [Fact]
        public void TryRead_FallsBackToLegacyBitmap_WhenNoAlphaCapableFormatPresent()
        {
            StaTest.Run(() =>
            {
                using var opaqueLegacy = new Bitmap(2, 2);
                opaqueLegacy.SetPixel(0, 0, Color.Red);

                var data = new DataObject();
                data.SetData(DataFormats.Bitmap, true, opaqueLegacy);

                using var decoded = ClipboardImageDecoder.TryRead(data);
                Assert.NotNull(decoded);
                Assert.Equal(255, decoded!.GetPixel(0, 0).A);
            });
        }

        [Fact]
        public void TryRead_DecodesDibV5_WithRealAlphaMask_RespectingBottomUpRowOrder()
        {
            StaTest.Run(() =>
            {
                var dibV5Bytes = BuildTwoByTwoDibV5(
                    topLeft: (a: 128, r: 255, g: 0, b: 0),
                    topRight: (a: 255, r: 0, g: 255, b: 0),
                    bottomLeft: (a: 0, r: 0, g: 0, b: 255),
                    bottomRight: (a: 255, r: 255, g: 255, b: 255));

                var data = new DataObject();
                data.SetData("Format17", new MemoryStream(dibV5Bytes));

                using var decoded = ClipboardImageDecoder.TryRead(data);
                Assert.NotNull(decoded);

                var topLeft = decoded!.GetPixel(0, 0);
                Assert.Equal(128, topLeft.A);
                Assert.Equal(255, topLeft.R);

                var bottomLeft = decoded.GetPixel(0, 1);
                Assert.Equal(0, bottomLeft.A);

                var bottomRight = decoded.GetPixel(1, 1);
                Assert.Equal(255, bottomRight.A);
                Assert.Equal(255, bottomRight.R);
                Assert.Equal(255, bottomRight.G);
                Assert.Equal(255, bottomRight.B);
            });
        }

        private static byte[] BuildTwoByTwoDibV5(
            (int a, int r, int g, int b) topLeft,
            (int a, int r, int g, int b) topRight,
            (int a, int r, int g, int b) bottomLeft,
            (int a, int r, int g, int b) bottomRight)
        {
            const int headerSize = 56;
            const int width = 2;
            const int height = 2; // Positive height => bottom-up row storage, per BITMAPV5HEADER convention.
            const int stride = width * 4;

            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true))
            {
                w.Write((uint)headerSize);
                w.Write(width);
                w.Write(height);
                w.Write((ushort)1); // Planes
                w.Write((ushort)32); // BitCount
                w.Write((uint)3); // BI_BITFIELDS
                w.Write((uint)(stride * height)); // SizeImage
                w.Write(0); // XPelsPerMeter
                w.Write(0); // YPelsPerMeter
                w.Write((uint)0); // ClrUsed
                w.Write((uint)0); // ClrImportant
                w.Write((uint)0x00FF0000); // RedMask
                w.Write((uint)0x0000FF00); // GreenMask
                w.Write((uint)0x000000FF); // BlueMask
                w.Write((uint)0xFF000000); // AlphaMask

                void WritePixel((int a, int r, int g, int b) p)
                {
                    w.Write((byte)p.b);
                    w.Write((byte)p.g);
                    w.Write((byte)p.r);
                    w.Write((byte)p.a);
                }

                // Bottom-up: file's first pixel row is the image's bottom row.
                WritePixel(bottomLeft);
                WritePixel(bottomRight);
                WritePixel(topLeft);
                WritePixel(topRight);
            }

            return ms.ToArray();
        }
    }
}
