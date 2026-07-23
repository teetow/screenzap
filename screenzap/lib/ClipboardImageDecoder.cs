using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace screenzap.lib
{
    /// <summary>
    /// Reads clipboard image data in the priority order that preserves alpha. CF_BITMAP (the
    /// classic <see cref="DataFormats.Bitmap"/> format) is a device-dependent bitmap with no
    /// alpha channel by construction, so anything read through it alone comes back opaque no
    /// matter what the copying app actually put on the clipboard. Chromium, Firefox, and many
    /// native Windows apps place a registered "PNG" format (real alpha, no conversion)
    /// specifically to work around this; others (XnView confirmed; MIME-style writers generally)
    /// use "image/png" instead - same raw bytes, different format name. Some producers also
    /// populate CF_DIBV5 with a real alpha mask. This class tries all of those first and only
    /// falls back to CF_BITMAP as a last resort.
    /// </summary>
    internal static class ClipboardImageDecoder
    {
        internal static readonly string[] PngFormatNames = { "PNG", "image/png" };
        private const string DibV5Format = "Format17"; // CF_DIBV5 = 17, no named DataFormats constant exists.

        public static bool HasAlphaCapableFormat(IDataObject? dataObject)
        {
            return dataObject != null &&
                (PngFormatNames.Any(dataObject.GetDataPresent) ||
                 dataObject.GetDataPresent(DibV5Format) ||
                 dataObject.GetDataPresent(DataFormats.Bitmap, true));
        }

        public static Bitmap? TryRead(IDataObject? dataObject)
        {
            if (dataObject == null)
            {
                return null;
            }

            return TryReadPng(dataObject)
                ?? TryReadDibV5(dataObject)
                ?? TryReadLegacyBitmap(dataObject);
        }

        private static Bitmap? TryReadPng(IDataObject dataObject)
        {
            foreach (var formatName in PngFormatNames)
            {
                if (!dataObject.GetDataPresent(formatName))
                {
                    continue;
                }

                try
                {
                    if (dataObject.GetData(formatName) is MemoryStream stream)
                    {
                        stream.Position = 0;
                        using var decoded = Image.FromStream(stream);
                        return new Bitmap(decoded);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Clipboard PNG-format decode failed ({formatName}): {ex.Message}");
                }
            }

            return null;
        }

        private static Bitmap? TryReadDibV5(IDataObject dataObject)
        {
            if (!dataObject.GetDataPresent(DibV5Format))
            {
                return null;
            }

            try
            {
                if (dataObject.GetData(DibV5Format) is MemoryStream stream)
                {
                    return DecodeDibV5(stream.ToArray());
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Clipboard CF_DIBV5 decode failed: {ex.Message}");
            }

            return null;
        }

        private static Bitmap? TryReadLegacyBitmap(IDataObject dataObject)
        {
            if (dataObject.GetDataPresent(DataFormats.Bitmap, true) &&
                dataObject.GetData(DataFormats.Bitmap, true) is Image legacyImage)
            {
                using (legacyImage)
                {
                    return new Bitmap(legacyImage);
                }
            }

            return null;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapV5HeaderPrefix
        {
            public uint Size;
            public int Width;
            public int Height;
            public ushort Planes;
            public ushort BitCount;
            public uint Compression;
            public uint SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public uint ClrUsed;
            public uint ClrImportant;
            public uint RedMask;
            public uint GreenMask;
            public uint BlueMask;
            public uint AlphaMask;
            // Remaining BITMAPV5HEADER fields (color space, endpoints, gamma, ICC profile info)
            // don't affect pixel decoding and are skipped over via Size below.
        }

        private static Bitmap? DecodeDibV5(byte[] bytes)
        {
            const uint BI_BITFIELDS = 3;
            // Standard byte-order masks matching PixelFormat.Format32bppArgb's in-memory layout.
            // Anything else would need per-pixel bit-shifting to normalize; bail out instead of
            // risking a silently wrong decode.
            const uint StandardBlueMask = 0x000000FF;
            const uint StandardGreenMask = 0x0000FF00;
            const uint StandardRedMask = 0x00FF0000;
            const uint StandardAlphaMask = 0xFF000000;

            int headerPrefixSize = Marshal.SizeOf<BitmapV5HeaderPrefix>();
            if (bytes == null || bytes.Length < headerPrefixSize)
            {
                return null;
            }

            BitmapV5HeaderPrefix header;
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                header = Marshal.PtrToStructure<BitmapV5HeaderPrefix>(handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }

            if (header.BitCount != 32 || header.Compression != BI_BITFIELDS || header.AlphaMask == 0)
            {
                return null;
            }

            if (header.RedMask != StandardRedMask || header.GreenMask != StandardGreenMask ||
                header.BlueMask != StandardBlueMask || header.AlphaMask != StandardAlphaMask)
            {
                return null;
            }

            int width = header.Width;
            bool bottomUp = header.Height > 0;
            int height = Math.Abs(header.Height);
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            int pixelDataOffset = (int)header.Size;
            int stride = width * 4;
            long requiredLength = (long)pixelDataOffset + (long)stride * height;
            if (pixelDataOffset < headerPrefixSize || requiredLength > bytes.Length)
            {
                return null;
            }

            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, width, height);
            var bits = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int row = 0; row < height; row++)
                {
                    int srcRow = bottomUp ? (height - 1 - row) : row;
                    int srcOffset = pixelDataOffset + srcRow * stride;
                    IntPtr destRow = IntPtr.Add(bits.Scan0, row * bits.Stride);
                    Marshal.Copy(bytes, srcOffset, destRow, stride);
                }
            }
            finally
            {
                bitmap.UnlockBits(bits);
            }

            return bitmap;
        }

        /// <summary>
        /// Decodes a packed DIB (a BITMAPINFOHEADER/V4/V5 header immediately followed by the pixel
        /// array, with no leading BITMAPFILEHEADER) by synthesizing the 14-byte file header GDI+
        /// requires and handing the result to the normal image decoder. Windows clipboard *history*
        /// (unlike the live clipboard) sometimes exposes an item's bitmap only as such a raw DIB via
        /// <c>GetBitmapAsync</c>, which <c>new Bitmap(stream)</c>/<c>Image.FromStream</c> reject
        /// outright ("Parameter is not valid"). Returns null when the bytes don't look like a DIB.
        /// </summary>
        public static Bitmap? TryDecodePackedDib(byte[]? dibBytes)
        {
            const int FileHeaderSize = 14;
            if (dibBytes == null || dibBytes.Length < 4)
            {
                return null;
            }

            uint headerSize = BitConverter.ToUInt32(dibBytes, 0);
            // Recognized DIB header sizes: BITMAPINFOHEADER(40), V2(52), V3(56), V4(108), V5(124).
            if (headerSize != 40 && headerSize != 52 && headerSize != 56 && headerSize != 108 && headerSize != 124)
            {
                return null;
            }
            if (dibBytes.Length <= headerSize)
            {
                return null;
            }

            ushort bitCount = dibBytes.Length >= 16 ? BitConverter.ToUInt16(dibBytes, 14) : (ushort)0;
            uint clrUsed = dibBytes.Length >= 36 ? BitConverter.ToUInt32(dibBytes, 32) : 0u;

            // Color table (if any) sits between the header and the pixel data. For <=8bpp images
            // it holds clrUsed (or 2^bitCount) RGBQUAD entries; higher bit depths have none unless
            // BI_BITFIELDS added 3 (or 4) mask DWORDs, which are already counted inside headerSize
            // for V4/V5 headers — so only add a palette here.
            int paletteEntries = 0;
            if (bitCount <= 8 && bitCount > 0)
            {
                paletteEntries = clrUsed != 0 ? (int)clrUsed : (1 << bitCount);
            }
            long pixelOffset = FileHeaderSize + (long)headerSize + (long)paletteEntries * 4;

            try
            {
                using var ms = new MemoryStream(FileHeaderSize + dibBytes.Length);
                using (var writer = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true))
                {
                    writer.Write((byte)'B');
                    writer.Write((byte)'M');
                    writer.Write((uint)(FileHeaderSize + dibBytes.Length)); // bfSize
                    writer.Write((ushort)0); // bfReserved1
                    writer.Write((ushort)0); // bfReserved2
                    writer.Write((uint)pixelOffset); // bfOffBits
                    writer.Write(dibBytes);
                }

                ms.Position = 0;
                using var decoded = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: false);
                return new Bitmap(decoded);
            }
            catch (Exception ex)
            {
                Logger.Log($"TryDecodePackedDib failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Cheap 16×16 RGB-only downsample used to decide whether two images are "the same picture"
        /// while ignoring alpha differences. This lets an alpha-carrying live-clipboard capture be
        /// matched to the alpha-stripped copy Windows keeps in clipboard history, so the alpha
        /// version can stand in for it. Returns 16*16*3 bytes (R,G,B per cell).
        /// </summary>
        public static byte[] ComputeRgbThumbprint(Bitmap bitmap)
        {
            ArgumentNullException.ThrowIfNull(bitmap);

            const int Cells = 16;
            var rgb = new byte[Cells * Cells * 3];
            if (bitmap.Width <= 0 || bitmap.Height <= 0)
            {
                return rgb;
            }

            // Downsample from the raw locked bytes with nearest-neighbor sampling instead of GDI+
            // DrawImage: GDI+ premultiplies a 32bppArgb source during any draw, collapsing a
            // transparent pixel's RGB to zero, which would make the same picture hash differently
            // depending on its alpha. Locking as Format32bppArgb yields straight (non-premultiplied)
            // BGRA — RGB intact even where alpha is 0 — and we simply never read the alpha byte, so
            // the thumbprint is deterministic and alpha-insensitive. Only used for "same picture?"
            // matching, so nearest-neighbor is sufficient as long as it is computed identically for
            // both images being compared.
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var src = new byte[data.Stride * bitmap.Height];
                Marshal.Copy(data.Scan0, src, 0, src.Length);

                int o = 0;
                for (int cy = 0; cy < Cells; cy++)
                {
                    int sy = (int)((long)cy * bitmap.Height / Cells);
                    int rowBase = sy * data.Stride;
                    for (int cx = 0; cx < Cells; cx++)
                    {
                        int sx = (int)((long)cx * bitmap.Width / Cells);
                        int p = rowBase + sx * 4; // B,G,R,A in memory
                        rgb[o++] = src[p];
                        rgb[o++] = src[p + 1];
                        rgb[o++] = src[p + 2];
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return rgb;
        }
    }
}
