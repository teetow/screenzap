using System;
using System.Drawing;
using System.Runtime.InteropServices;
using screenzap.Native;
using Xunit;

namespace Screenzap.ViewportTests;

public class CaptureOverlayRendererTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void PartialPaintMatchesFullPaint_AfterResizePanCaptionFlipAndCoalescedMoves(int batch)
    {
        using var source = new Bitmap(240, 160);
        using (var graphics = Graphics.FromImage(source))
        {
            graphics.Clear(Color.LightSteelBlue);
            graphics.FillEllipse(Brushes.DarkOrange, 10, 15, 160, 120);
        }
        using var incremental = new CaptureOverlayRenderer(source);
        using var reference = new CaptureOverlayRenderer(source);
        using var actual = new Bitmap(source.Width, source.Height);
        using var expected = new Bitmap(source.Width, source.Height);
        var whole = new Rectangle(Point.Empty, source.Size);
        nint full = CaptureOverlayRenderer.CreateRectRgn(0, 0, source.Width, source.Height);
        nint pending = CaptureOverlayRenderer.CreateRectRgn(0, 0, 0, 0);
        try
        {
            Paint(incremental, actual, whole, full, Rectangle.Empty);
            var areas = new[]
            {
                new Rectangle(20, 25, 170, 95), new Rectangle(20, 25, 172, 97),
                new Rectangle(20, 25, 50, 30), new Rectangle(60, 70, 50, 30),
                new Rectangle(70, 80, 165, 78), new Rectangle(0, 0, 240, 160),
                new Rectangle(0, 0, 1, 1), new Rectangle(200, 100, 1, 40), Rectangle.Empty
            };
            var previous = Rectangle.Empty;
            for (int index = 0; index < areas.Length; index++)
            {
                var area = areas[index];
                nint damage = incremental.CreateDamageRegion(previous, area);
                try { CombineRgn(pending, pending, damage, 2); }
                finally { CaptureOverlayRenderer.DeleteObject(damage); }
                previous = area;
                if ((index + 1) % batch != 0 && index != areas.Length - 1) continue;

                GetRgnBox(pending, out var box);
                var dirty = Rectangle.Intersect(whole, Rectangle.FromLTRB(box.Left, box.Top, box.Right, box.Bottom));
                Paint(incremental, actual, dirty, pending, area);
                Paint(reference, expected, whole, full, area);
                for (int y = 0; y < source.Height; y++)
                    for (int x = 0; x < source.Width; x++)
                        Assert.True(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(),
                            $"Frame {index}, batch {batch}: stale or incorrect pixel at ({x}, {y}).");
                SetRectRgn(pending, 0, 0, 0, 0);
            }
        }
        finally
        {
            CaptureOverlayRenderer.DeleteObject(pending);
            CaptureOverlayRenderer.DeleteObject(full);
        }
        Assert.Equal(Color.LightSteelBlue.ToArgb(), source.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void DragDamageExcludesUnchangedInteriorOfLargeSelection()
    {
        using var source = new Bitmap(3840, 2160);
        using var renderer = new CaptureOverlayRenderer(source);
        nint damage = renderer.CreateDamageRegion(new Rectangle(100, 100, 3300, 1800), new Rectangle(100, 100, 3302, 1801));
        try
        {
            Assert.False(PtInRegion(damage, 1500, 1000));
            Assert.False(PtInRegion(damage, 50, 50));
            Assert.True(PtInRegion(damage, 3401, 1000));
            Assert.True(PtInRegion(damage, 1500, 1900));
            Assert.True(PtInRegion(damage, 100, 1000));
        }
        finally { CaptureOverlayRenderer.DeleteObject(damage); }
    }

    [Fact]
    public void FrozenPixelsStayClearInsideSelection_AndDimOutside()
    {
        using var source = new Bitmap(200, 140);
        using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.White);
        using var renderer = new CaptureOverlayRenderer(source);
        using var target = new Bitmap(200, 140);
        nint full = CaptureOverlayRenderer.CreateRectRgn(0, 0, 200, 140);
        try { Paint(renderer, target, new Rectangle(0, 0, 200, 140), full, new Rectangle(40, 40, 80, 60)); }
        finally { CaptureOverlayRenderer.DeleteObject(full); }
        Assert.Equal(Color.White.ToArgb(), target.GetPixel(80, 70).ToArgb());
        var dim = target.GetPixel(10, 10);
        Assert.InRange(dim.R, 134, 136);
        Assert.Equal(dim.R, dim.G);
        Assert.Equal(dim.R, dim.B);
        renderer.Dispose();
        renderer.Dispose();
        Assert.Equal(Color.White.ToArgb(), source.GetPixel(0, 0).ToArgb());
    }

    private static void Paint(CaptureOverlayRenderer renderer, Bitmap target, Rectangle dirty, nint region, Rectangle area)
    {
        using var graphics = Graphics.FromImage(target);
        nint dc = graphics.GetHdc();
        try
        {
            SelectClipRgn(dc, region);
            renderer.Paint(dc, dirty, region, area);
            GdiFlush();
        }
        finally { SelectClipRgn(dc, 0); graphics.ReleaseHdc(dc); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("gdi32.dll")] private static extern bool PtInRegion(nint region, int x, int y);
    [DllImport("gdi32.dll")] private static extern int GetRgnBox(nint region, out NativeRect bounds);
    [DllImport("gdi32.dll")] private static extern bool SetRectRgn(nint region, int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(nint target, nint first, nint second, int mode);
    [DllImport("gdi32.dll")] private static extern int SelectClipRgn(nint dc, nint region);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
}
