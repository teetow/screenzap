using System.Drawing;
using screenzap.lib;
using Xunit;
using ZXing;

namespace Screenzap.ViewportTests;

public class QrCodeDecoderTests
{
    [Fact]
    public void DecodesBitmapWithoutTheRemovedToolbarRuntimeDependency()
    {
        const string expected = "Screenzap clipboard capture";
        var pixels = new ZXing.QrCode.QRCodeWriter().encode(expected, BarcodeFormat.QR_CODE, 200, 200);
        using var bitmap = new Bitmap(pixels.Width, pixels.Height);
        for (int y = 0; y < pixels.Height; y++)
            for (int x = 0; x < pixels.Width; x++)
                bitmap.SetPixel(x, y, pixels[x, y] ? Color.Black : Color.White);
        Assert.Equal(expected, QrCodeDecoder.TryDecode(bitmap));
    }
}
