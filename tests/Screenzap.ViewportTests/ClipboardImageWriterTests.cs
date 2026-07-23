using System.Drawing;
using System.Drawing.Imaging;
using screenzap.lib;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class ClipboardImageWriterTests
    {
        [Fact]
        public void BuildDataObject_RoundTripsThroughClipboardImageDecoder_PreservingAlpha()
        {
            StaTest.Run(() =>
            {
                using var source = new Bitmap(2, 2, PixelFormat.Format32bppArgb);
                source.SetPixel(0, 0, Color.FromArgb(64, 10, 20, 30));

                var data = ClipboardImageWriter.BuildDataObject(source);

                // The legacy CF_BITMAP tier is present too (for receivers that don't know "PNG"),
                // and it is inherently opaque - decoding it directly must not be confused with the
                // alpha-preserving "PNG" tier the round trip should actually pick.
                Assert.True(data.GetDataPresent(System.Windows.Forms.DataFormats.Bitmap, true));

                using var decoded = ClipboardImageDecoder.TryRead(data);
                Assert.NotNull(decoded);
                Assert.Equal(64, decoded!.GetPixel(0, 0).A);
            });
        }
    }
}
