using System;
using System.Drawing;
using System.Windows.Forms;
using screenzap;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests;

public class ArrowInteractionTests
{
    private static ImageDocumentEditor Arrow(double zoom = 1, decimal scale = 1m)
    {
        var editor = EditorFixture.WithCanvas(640, 400);

        editor.ResizeSurface(new Size(640, 400));
        editor.SurfaceSetZoom((decimal)zoom);
        ((IClipboardDocumentPresenter)editor).TryExecute(EditorCommandId.ArrowTool);
        editor.SurfaceStyle("Width", 16f);
        editor.SurfaceStyle("Color", Color.Black);
        editor.SurfaceStyle("Arrow", scale);
        Gesture(editor, new Point(80, 200), new Point(480, 200));
        return editor;
    }

    private static void Gesture(ImageDocumentEditor editor, Point from, Point to)
    {
        var start = editor.TestImagePixelToClient(from);
        var finish = editor.TestImagePixelToClient(to);
        editor.SurfacePointer(0, start, MouseButtons.Left);
        editor.SurfacePointer(1, finish, MouseButtons.Left);
        editor.SurfacePointer(2, finish, MouseButtons.Left);
    }

    [Theory]
    [InlineData(.5, 0)] [InlineData(1, 0)] [InlineData(2, 0)]
    [InlineData(.5, 1)] [InlineData(1, 1)] [InlineData(2, 1)]
    [InlineData(.5, 2)] [InlineData(1, 2)] [InlineData(2, 2)]
    public void ShaftAndHeadWingsMoveTheWholeArrowWhileToolStaysActive(double zoom, int target)
    {
        StaTest.Run(() =>
        {
            using var editor = Arrow(zoom, target == 2 ? 5m : 1m);
            var grab = target switch { 0 => new Point(220, 206), 1 => new Point(400, 220), _ => new Point(380, 280) };
            using var image = editor.BuildCompositeImageForTests();
            Assert.True(image.GetPixel(grab.X, grab.Y).R < 200, "Grab must land on the painted arrow");
            Gesture(editor, grab, new Point(grab.X + 24, grab.Y + 16));
            Assert.Equal(1, editor.TestAnnotationShapeCount);
            Assert.Equal(new Point(104, 216), editor.TestSelectedAnnotation!.Start);
            Assert.Equal(new Point(504, 216), editor.TestSelectedAnnotation.End);
            Assert.Equal(DrawingTool.Arrow, editor.TestActiveDrawingTool);
            editor.SurfaceKey(Keys.Control | Keys.Z);
            Assert.Equal(new Point(80, 200), editor.TestSelectedAnnotation!.Start);
            Assert.Equal(new Point(480, 200), editor.TestSelectedAnnotation.End);
        });
    }

    [Fact]
    public void ClickingAnArrowSelectsItKeepsTheToolAndDoesNotAddAnUndoStep()
    {
        StaTest.Run(() =>
        {
            using var editor = Arrow();
            editor.TestClearAnnotationSelection();
            Gesture(editor, new Point(400, 220), new Point(400, 220));
            Assert.Equal(1, editor.TestSelectedShapeCount);
            Assert.Equal(DrawingTool.Arrow, editor.TestActiveDrawingTool);
            editor.SurfaceKey(Keys.Control | Keys.Z);
            Assert.Equal(0, editor.TestAnnotationShapeCount);
        });
    }

    [Fact]
    public void ClickingOutsideTheArrowExitsItsToolWithoutCreatingAnything()
    {
        StaTest.Run(() =>
        {
            using var editor = Arrow(scale: 5m);
            Gesture(editor, new Point(200, 270), new Point(200, 270));
            Assert.Equal(1, editor.TestAnnotationShapeCount);
            Assert.Equal(0, editor.TestSelectedShapeCount);
            Assert.Equal(DrawingTool.None, editor.TestActiveDrawingTool);
            Assert.Equal(ActiveTool.None, editor.CurrentTool);
        });
    }

    [Theory]
    [InlineData(.5)] [InlineData(1)] [InlineData(2)]
    public void EnlargedEndpointTargetMovesOnlyThatVertexWithoutJumping(double zoom)
    {
        StaTest.Run(() =>
        {
            using var editor = Arrow(zoom);
            var endpoint = editor.TestImagePixelToClient(new Point(480, 200));
            var grab = new Point(endpoint.X, endpoint.Y + 6);
            editor.SurfacePointer(0, grab, MouseButtons.Left);
            var finish = new Point(grab.X + (int)(24 * zoom), grab.Y + (int)(16 * zoom));
            editor.SurfacePointer(1, finish, MouseButtons.Left);
            editor.SurfacePointer(2, finish, MouseButtons.Left);
            Assert.Equal(1, editor.TestAnnotationShapeCount);
            Assert.Equal(new Point(80, 200), editor.TestSelectedAnnotation!.Start);
            Assert.Equal(new Point(504, 216), editor.TestSelectedAnnotation.End);
            Assert.Equal(DrawingTool.Arrow, editor.TestActiveDrawingTool);
        });
    }

    [Fact]
    public void ClickingNearAnEndpointDoesNotMoveItOrPolluteUndo()
    {
        StaTest.Run(() =>
        {
            using var editor = Arrow();
            var grab = editor.TestImagePixelToClient(new Point(480, 206));
            editor.SurfacePointer(0, grab, MouseButtons.Left);
            editor.SurfacePointer(1, grab, MouseButtons.Left);
            editor.SurfacePointer(2, grab, MouseButtons.Left);
            Assert.Equal(new Point(480, 200), editor.TestSelectedAnnotation!.End);
            Assert.Equal(DrawingTool.Arrow, editor.TestActiveDrawingTool);
            editor.SurfaceKey(Keys.Control | Keys.Z);
            Assert.Equal(0, editor.TestAnnotationShapeCount);
        });
    }

    [Theory]
    [InlineData(.5, true)] [InlineData(1, true)] [InlineData(2, true)]
    [InlineData(.5, false)] [InlineData(1, false)] [InlineData(2, false)]
    public void SelectedArrowHasAClearContourWithOrWithoutItsToolActive(double zoom, bool toolActive)
    {
        StaTest.Run(() =>
        {
            using var editor = Arrow(zoom);
            if (!toolActive) editor.TestDeactivateDrawingTool();
            editor.SurfacePointerExit();
            using var frame = Frame(editor);
            var shaft = editor.TestImagePixelToClient(new Point(220, 200));
            var color = frame.GetPixel(shaft.X, shaft.Y + (int)(8 * zoom) + 1);
            Assert.True(color.B > color.R + 100, "Selected shaft needs a clear persistent blue rim");
            Assert.Equal(Color.Black.ToArgb(), frame.GetPixel(shaft.X, shaft.Y).ToArgb());
        });
    }

    [Fact]
    public void HoveringAnotherArrowUsesAWeakerContourAndKeepsTheSelectedArrowEmphasized()
    {
        StaTest.Run(() =>
        {
            using var editor = Arrow();
            Gesture(editor, new Point(80, 320), new Point(480, 320));
            editor.SurfacePointerExit();
            using var selected = Frame(editor);
            var first = editor.TestImagePixelToClient(new Point(220, 200));
            var second = editor.TestImagePixelToClient(new Point(220, 320));
            var strong = selected.GetPixel(second.X, second.Y + 9);
            editor.SurfacePointer(1, first, MouseButtons.None);
            using var hovered = Frame(editor);
            var weak = hovered.GetPixel(first.X, first.Y + 9);
            Assert.True(strong.B - strong.R > 150);
            Assert.InRange(weak.B - weak.R, 10, 130);
            Assert.Equal(strong, hovered.GetPixel(second.X, second.Y + 9));
            Assert.Equal(1, editor.TestSelectedShapeCount);
            Assert.Equal(new Point(80, 320), editor.TestSelectedAnnotation!.Start);
        });
    }

    [Fact]
    public void UnselectedHoverDoesNotDrawOverTheShaftOrInsideTheHead()
    {
        StaTest.Run(() =>
        {
            using var editor = Arrow(scale: 5m);
            editor.TestClearAnnotationSelection();
            editor.SurfacePointerExit();
            using var idle = Frame(editor);
            editor.SurfacePointer(1, editor.TestImagePixelToClient(new Point(380, 280)), MouseButtons.None);
            using var hovered = Frame(editor);
            int interior = 0;
            for (int y = 0; y < idle.Height; y++) for (int x = 0; x < idle.Width; x++)
            {
                if (idle.GetPixel(x, y).ToArgb() != Color.Black.ToArgb()) continue;
                interior++;
                Assert.Equal(idle.GetPixel(x, y), hovered.GetPixel(x, y));
            }
            Assert.True(interior > 5000, "Check the filled head as well as the shaft");
        });
    }

    private static Bitmap Frame(ImageDocumentEditor editor)
    {
        var image = new Bitmap(640, 400);
        using var graphics = Graphics.FromImage(image);
        editor.RenderSurface(graphics);
        return image;
    }

    [Fact]
    public void EndpointHoverEnlargesAndHighlightsTheHandleAndUsesADifferentCursor()
    {
        StaTest.Run(() =>
        {
            using var editor = Arrow();
            var body = editor.TestImagePixelToClient(new Point(220, 200));
            var endpoint = editor.TestImagePixelToClient(new Point(480, 200));
            editor.SurfacePointer(1, body, MouseButtons.None);
            Assert.Equal("SizeAll", editor.SurfaceCursor);
            using var idle = Frame(editor);
            editor.SurfacePointer(1, new Point(endpoint.X, endpoint.Y + 6), MouseButtons.None);
            Assert.Equal("Cross", editor.SurfaceCursor);
            using var hover = Frame(editor);
            var color = hover.GetPixel(endpoint.X, endpoint.Y);
            Assert.True(color.B > color.R + 80, "Hovered vertex must be visibly highlighted");
            int changed = 0;
            for (int y = endpoint.Y - 9; y <= endpoint.Y + 9; y++)
                for (int x = endpoint.X - 9; x <= endpoint.X + 9; x++)
                    if (idle.GetPixel(x, y) != hover.GetPixel(x, y)) changed++;
            Assert.True(changed > 100, "Handle hover must react beyond the original eight-pixel grip");
            editor.SurfacePointerExit();
            Assert.Equal("Arrow", editor.SurfaceCursor);
            using var left = Frame(editor);
            Assert.Equal(idle.GetPixel(endpoint.X, endpoint.Y), left.GetPixel(endpoint.X, endpoint.Y));
        });
    }
}
