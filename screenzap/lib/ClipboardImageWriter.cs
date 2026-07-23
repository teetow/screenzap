using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace screenzap.lib
{
    /// <summary>
    /// Writes an image to the clipboard the way Chromium/Firefox do: CF_BITMAP for legacy
    /// consumers (always opaque - a device-dependent bitmap has no alpha channel by
    /// construction), plus a registered "PNG" format carrying the real alpha-safe bytes for any
    /// receiver that knows to look for it. Without the "PNG" format, anything Screenzap copies
    /// or commits back to the clipboard comes out fully opaque in whatever app receives it,
    /// even if the source image had transparency. See <see cref="ClipboardImageDecoder"/> for
    /// the read-side counterpart.
    /// </summary>
    internal static class ClipboardImageWriter
    {
        public static void WriteImage(Image image)
        {
            Clipboard.SetDataObject(BuildDataObject(image), true);
        }

        public static DataObject BuildDataObject(Image image)
        {
            ArgumentNullException.ThrowIfNull(image);

            var data = new DataObject();
            data.SetData(DataFormats.Bitmap, true, image);

            using var pngStream = new MemoryStream();
            image.Save(pngStream, ImageFormat.Png);
            pngStream.Position = 0;
            data.SetData("PNG", false, pngStream);

            return data;
        }
    }
}
