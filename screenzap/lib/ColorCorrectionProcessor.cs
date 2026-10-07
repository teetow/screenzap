using System.Drawing;
using System.Drawing.Imaging;
namespace screenzap.lib;
internal static class ColorCorrectionProcessor
{
    internal static Bitmap Apply(Bitmap source, Rectangle selection, float stops, float contrastPercent, float saturationPercent, float gammaValue)
    {
        float exposure = (float)Math.Pow(2, stops), contrast = contrastPercent / 100, saturation = saturationPercent / 100, gamma = 1 / gammaValue;
        float[][] finalMat = new float[][] {
                new float[] { 1, 0, 0, 0, 0 },
                new float[] { 0, 1, 0, 0, 0 },
                new float[] { 0, 0, 1, 0, 0 },
                new float[] { 0, 0, 0, 1, 0 },
                new float[] { 0, 0, 0, 0, 1 }
            };

        float s = saturation;
        float rw = 0.3086f, gw = 0.6094f, bw = 0.0820f;
        float[][] satMat = new float[][] {
                new float[] { (1 - s) * rw + s, (1 - s) * rw,     (1 - s) * rw,     0, 0 },
                new float[] { (1 - s) * gw,     (1 - s) * gw + s, (1 - s) * gw,     0, 0 },
                new float[] { (1 - s) * bw,     (1 - s) * bw,     (1 - s) * bw + s, 0, 0 },
                new float[] { 0,                0,                0,                1, 0 },
                new float[] { 0,                0,                0,                0, 1 }
            };
        finalMat = MultiplyMatrix(finalMat, satMat);

        float c = contrast + 1.0f;
        float t = 0.5f * (1.0f - c);
        float[][] conMat = new float[][] {
                new float[] { c, 0, 0, 0, 0 },
                new float[] { 0, c, 0, 0, 0 },
                new float[] { 0, 0, c, 0, 0 },
                new float[] { 0, 0, 0, 1, 0 },
                new float[] { t, t, t, 0, 1 }
            };
        finalMat = MultiplyMatrix(finalMat, conMat);

        float[][] expMat = new float[][] {
                new float[] { exposure, 0,        0,        0, 0 },
                new float[] { 0,        exposure, 0,        0, 0 },
                new float[] { 0,        0,        exposure, 0, 0 },
                new float[] { 0,        0,        0,        1, 0 },
                new float[] { 0,        0,        0,        0, 1 }
            };
        finalMat = MultiplyMatrix(finalMat, expMat);


        var result = source.Clone(new Rectangle(Point.Empty, source.Size), PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix(finalMat));
        attributes.SetGamma(Math.Max(.01f, gamma));
        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        var target = selection.IsEmpty ? new Rectangle(Point.Empty, source.Size) : Rectangle.Intersect(selection, new Rectangle(Point.Empty, source.Size));
        if (target.Width > 0 && target.Height > 0) g.DrawImage(source, target, target.X, target.Y, target.Width, target.Height, GraphicsUnit.Pixel, attributes);
        return result;
    }
    private static float[][] MultiplyMatrix(float[][] a, float[][] b)
    {
        var result = new float[5][];
        for (int i = 0; i < 5; i++) { result[i] = new float[5]; for (int j = 0; j < 5; j++) for (int k = 0; k < 5; k++) result[i][j] += a[i][k] * b[k][j]; }
        return result;
    }
}
