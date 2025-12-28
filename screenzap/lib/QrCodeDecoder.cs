using System;
using System.Collections.Generic;
using System.Drawing;
using ZXing;
using ZXing.Common;
using ZXing.Windows.Compatibility;

namespace screenzap.lib
{
    public static class QrCodeDecoder
    {
        private static readonly BarcodeReader Reader = CreateReader();

        public static string? TryDecode(Bitmap bitmap)
        {
            ArgumentNullException.ThrowIfNull(bitmap);

            try
            {
                var result = Reader.Decode(bitmap);
                return string.IsNullOrWhiteSpace(result?.Text) ? null : result.Text;
            }
            catch
            {
                return null;
            }
        }

        private static BarcodeReader CreateReader()
        {
            return new BarcodeReader
            {
                AutoRotate = true,
                Options = new DecodingOptions
                {
                    TryHarder = true,
                    TryInverted = true,
                    PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE }
                }
            };
        }
    }
}
