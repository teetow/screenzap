using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace screenzap.lib;

// Preserve native pixels and restore the source alpha after RGB restoration.
internal sealed class DeJpegBitmap : IDisposable
{
    private readonly Bitmap original;
    internal Size InputSize { get; }
    public byte[] Png { get; }

    public DeJpegBitmap(Image source)
    {
        original = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(original))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImageUnscaled(source, 0, 0);
        }
        InputSize = source.Size;
        using var input = new Bitmap(InputSize.Width, InputSize.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(input))
        {
            g.Clear(Color.White);
            g.DrawImageUnscaled(original, 0, 0);
        }
        using var stream = new MemoryStream();
        input.Save(stream, ImageFormat.Png);
        Png = stream.ToArray();
    }

    public Bitmap Restore(byte[] result)
    {
        using var stream = new MemoryStream(result);
        using var decoded = Image.FromStream(stream);
        if (decoded.Size != InputSize)
            throw new InvalidOperationException($"ComfyUI returned {decoded.Width}×{decoded.Height}; expected {InputSize.Width}×{InputSize.Height}. Check the workflow dimensions.");
        var restored = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);
        try
        {
            using (var g = Graphics.FromImage(restored))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImageUnscaled(decoded, 0, 0);
            }
            var bounds = new Rectangle(Point.Empty, original.Size);
            var before = original.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var after = restored.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                try
                {
                    var sourceRow = new byte[original.Width * 4];
                    var resultRow = new byte[sourceRow.Length];
                    for (int y = 0; y < original.Height; y++)
                    {
                        Marshal.Copy(before.Scan0 + y * before.Stride, sourceRow, 0, sourceRow.Length);
                        Marshal.Copy(after.Scan0 + y * after.Stride, resultRow, 0, resultRow.Length);
                        for (int x = 0; x < sourceRow.Length; x += 4)
                        {
                            resultRow[x + 3] = sourceRow[x + 3];
                            if (sourceRow[x + 3] == 0) Array.Copy(sourceRow, x, resultRow, x, 3);
                        }
                        Marshal.Copy(resultRow, 0, after.Scan0 + y * after.Stride, resultRow.Length);
                    }
                }
                finally { restored.UnlockBits(after); }
            }
            finally { original.UnlockBits(before); }
            return restored;
        }
        catch { restored.Dispose(); throw; }
    }

    public void Dispose() => original.Dispose();
}
