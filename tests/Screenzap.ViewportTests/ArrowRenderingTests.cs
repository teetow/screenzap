using System;
using System.Drawing;
using System.Windows.Forms;
using screenzap;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests;

public class ArrowRenderingTests
{
    private static ImageEditor Arrow(float size, bool diagonal = false)
    {
        var editor = EditorFixture.WithCanvas(520, 320);
        ((IClipboardDocumentPresenter)editor).TryExecute(EditorCommandId.ArrowTool);
        editor.TestFireMouseDownAtImagePixel(new Point(40, 160), MouseButtons.Left);
        var end = new Point(470, diagonal ? 70 : 160);
        editor.TestFireMouseMoveAtImagePixel(end, MouseButtons.Left);
        editor.TestFireMouseUpAtImagePixel(end, MouseButtons.Left);
        editor.SurfaceStyle("Width", 16f);
        editor.SurfaceStyle("Color", Color.Black);
        editor.SurfaceStyle("Arrow", size);
        return editor;
    }

    [Theory]
    [InlineData(0f, false)] [InlineData(.1f, false)] [InlineData(.2f, false)]
    [InlineData(.3f, false)] [InlineData(1f, false)]
    [InlineData(0f, true)] [InlineData(.1f, true)] [InlineData(.2f, true)]
    [InlineData(.3f, true)] [InlineData(1f, true)]
    public void ThickArrowRemainsConnectedThroughTheHead(float scale, bool diagonal)
    {
        StaTest.Run(() =>
        {
            using var editor = Arrow(scale, diagonal);
            using var image = editor.BuildCompositeImageForTests();
            var arrow = editor.TestSelectedAnnotation!;
            double dx = arrow.End.X - arrow.Start.X, dy = arrow.End.Y - arrow.Start.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            for (int distance = 4; distance < length - 4; distance++)
            {
                int x = (int)Math.Round(arrow.Start.X + dx * distance / length);
                int y = (int)Math.Round(arrow.Start.Y + dy * distance / length);
                Assert.True(image.GetPixel(x, y).R < 200, $"Detached arrow at {x},{y}, head scale {scale}");
            }
        });
    }

    [Fact]
    public void ZeroScaleMatchesShaftWidthAndStillHasAPointedTip()
    {
        StaTest.Run(() =>
        {
            using var editor = Arrow(0);
            using var image = editor.BuildCompositeImageForTests();
            int shaft = InkHeight(image, 100);
            int widestHead = 0;
            for (int x = 435; x < 470; x++) widestHead = Math.Max(widestHead, InkHeight(image, x));
            Assert.Equal(shaft, widestHead);
            Assert.InRange(InkHeight(image, 466), 1, shaft - 1);
        });
    }

    [Theory]
    [InlineData(.1f)] [InlineData(.5f)] [InlineData(1f)] [InlineData(5f)]
    public void ZoomScalesHeadAndShaftEqually(float scale)
    {
        StaTest.Run(() =>
        {
            using var editor = Arrow(scale);
            editor.TestSelectedAnnotation!.Selected = false;
            editor.AttachExternalSurface();
            editor.ResizeSurface(new Size(1040, 640));
            editor.SurfaceSetZoom(1);
            using var normal = new Bitmap(1040, 640);
            using (var graphics = Graphics.FromImage(normal)) editor.RenderSurface(graphics);
            var tip = editor.TestImagePixelToClient(new Point(470, 160));
            int normalHeight = MaxInkHeight(normal, tip.X - 120, tip.X, tip.Y - 150, tip.Y + 150);
            editor.SurfaceSetZoom(2);
            using var zoomed = new Bitmap(1040, 640);
            using (var graphics = Graphics.FromImage(zoomed)) editor.RenderSurface(graphics);
            tip = editor.TestImagePixelToClient(new Point(470, 160));
            int zoomedHeight = MaxInkHeight(zoomed, tip.X - 240, tip.X, tip.Y - 300, tip.Y + 300);
            Assert.InRange(zoomedHeight, normalHeight * 2 - 2, normalHeight * 2 + 2);
        });
    }

    [Fact]
    public void LargerHeadsGrowWiderFasterThanTheyGrowLonger()
    {
        StaTest.Run(() =>
        {
            using var normalEditor = Arrow(1);
            using var largeEditor = Arrow(5);
            using var normal = normalEditor.BuildCompositeImageForTests();
            using var large = largeEditor.BuildCompositeImageForTests();
            int shaft = InkHeight(normal, 100);
            int normalBase = 470, largeBase = 470;
            for (int x = 40; x < 470; x++)
            {
                if (InkHeight(normal, x) > shaft + 2) normalBase = Math.Min(normalBase, x);
                if (InkHeight(large, x) > shaft + 2) largeBase = Math.Min(largeBase, x);
            }
            int normalWidth = MaxInkHeight(normal, 40, 470, 0, 320);
            int largeWidth = MaxInkHeight(large, 40, 470, 0, 320);
            Assert.True(largeWidth > normalWidth * 3);
            Assert.InRange(470 - largeBase, 470 - normalBase, (470 - normalBase) * 3 / 2);
            Assert.Equal(shaft, InkHeight(large, 255));
        });
    }

    [Theory]
    [InlineData(1f, false)] [InlineData(5f, false)]
    [InlineData(1f, true)] [InlineData(5f, true)]
    public void ShortThickArrowsKeepAShaftAndDoNotOverflowTheirEndpoints(float scale, bool diagonal)
    {
        StaTest.Run(() =>
        {
            using var editor = Arrow(scale);
            var arrow = editor.TestSelectedAnnotation!;
            arrow.Start = new Point(220, 160);
            arrow.End = diagonal ? new Point(276, 104) : new Point(300, 160);
            using var image = editor.BuildCompositeImageForTests();
            double dx = arrow.End.X - arrow.Start.X, dy = arrow.End.Y - arrow.Start.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            double ux = dx / length, uy = dy / length;
            int shaftX = (int)Math.Round(arrow.Start.X + dx * .25);
            int shaftY = (int)Math.Round(arrow.Start.Y + dy * .25);
            Assert.True(image.GetPixel(shaftX, shaftY).R < 200);
            int outsideX = (int)Math.Round(shaftX - uy * 12);
            int outsideY = (int)Math.Round(shaftY + ux * 12);
            Assert.True(image.GetPixel(outsideX, outsideY).R > 240, "Head consumed the first half of the shaft");
            for (int y = 0; y < image.Height; y++) for (int x = 0; x < image.Width; x++)
            {
                if (image.GetPixel(x, y).R >= 200) continue;
                double along = (x - arrow.Start.X) * ux + (y - arrow.Start.Y) * uy;
                Assert.InRange(along, -2, length + 2);
            }
        });
    }

    private static int MaxInkHeight(Bitmap image, int left, int right, int top, int bottom)
    {
        int maximum = 0;
        for (int x = left; x < right; x++)
        {
            int count = 0;
            for (int y = top; y < bottom; y++) if (image.GetPixel(x, y).R < 128) count++;
            maximum = Math.Max(maximum, count);
        }
        return maximum;
    }
    private static int InkHeight(Bitmap image, int x) => MaxInkHeight(image, x, x + 1, 100, 220);
}
