using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using screenzap.Components.Shared;
using screenzap.lib;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class PerspectiveStraightenTests
    {
        private static void Drag(screenzap.ImageDocumentEditor editor, Point start, Point end)
        {
            editor.TestFireMouseDownAtImagePixel(start, MouseButtons.Left);
            editor.TestFireMouseMoveAtImagePixel(end, MouseButtons.Left);
            editor.TestFireMouseUpAtImagePixel(end, MouseButtons.Left);
        }

        [Fact]
        public void RectangleDrag_ReverseDirection_CreatesOrderedCorners_AndCornerDragAdjustsOnlyOne()
        {
            StaTest.Run(() =>
            {
                using var editor = EditorFixture.WithCanvas(160, 120);
                editor.ActivateStraightenTool();
                Drag(editor, new Point(130, 100), new Point(20, 10));
                Assert.Equal(new[] { new Point(20, 10), new Point(130, 10), new Point(130, 100), new Point(20, 100) }, editor.TestStraightenCorners);

                Drag(editor, new Point(130, 10), new Point(110, 25));
                Assert.Equal(new[] { new Point(20, 10), new Point(110, 25), new Point(130, 100), new Point(20, 100) }, editor.TestStraightenCorners);
                Assert.True(editor.TestStraightenApplyEnabled);
            });
        }

        [Fact]
        public void CrossedCorners_AndEmptySelection_CannotApplyWithEnter()
        {
            StaTest.Run(() =>
            {
                using var editor = EditorFixture.WithCanvas(160, 120);
                editor.ActivateStraightenTool();
                editor.TestFireKeyDown(Keys.Enter);
                Assert.True(editor.TestIsStraightenToolActive);
                Assert.False(editor.TestStraightenApplyEnabled);

                Drag(editor, new Point(20, 20), new Point(130, 100));
                Drag(editor, new Point(20, 20), new Point(140, 110));
                Assert.False(editor.TestStraightenApplyEnabled);
                editor.TestFireKeyDown(Keys.Enter);
                Assert.True(editor.TestIsStraightenToolActive);
                Assert.Equal(new Size(160, 120), editor.ViewportDiagnostics.ImagePixelSize);
                Assert.Contains("canUndo=False", editor.TestDescribeUndoStack());

                editor.TestFireKeyDown(Keys.Escape);
                Assert.False(editor.TestIsStraightenToolActive);
            });
        }

        [Fact]
        public void SelectionInitializesCorners_ApplyFlattensVisibleContent_UndoRestoresEditableObjects()
        {
            StaTest.Run(() =>
            {
                using var editor = EditorFixture.WithCanvas(160, 120);
                editor.TestAddTextAnnotation(new Point(30, 40), "perspective");
                using var pasted = EditorFixture.Canvas(10, 10, Color.Red);
                editor.SetInternalClipboardImageForDiagnostics(pasted);
                Assert.True(editor.PasteFromClipboardForDiagnostics());
                var originalLayerFrame = editor.GetImageLayerFrameForTests(0);
                var selection = new Rectangle(20, 20, 110, 80);
                editor.SetSelectionForDiagnostics(selection);
                using var compositeBefore = editor.BuildCompositeImageForTests();

                editor.ActivateStraightenTool();
                Assert.True(editor.TestStraightenApplyEnabled);
                editor.TestFireKeyDown(Keys.Enter);
                Assert.False(editor.TestIsStraightenToolActive);
                Assert.Equal(new Size(110, 80), editor.ViewportDiagnostics.ImagePixelSize);
                Assert.Equal(Rectangle.Empty, editor.SelectionDiagnostics.Selection);
                Assert.Equal(0, editor.TestTextAnnotationCount);
                Assert.Equal(0, editor.ImageLayerCountForTests);
                using var after = editor.CloneBaseBitmapForTests()!;
                // An axis-aligned warp is exactly the visible crop, including the pasted layer.
                Assert.Equal(compositeBefore.GetPixel(80, 60), after.GetPixel(60, 40));
                Assert.Equal(Color.Red.ToArgb(), after.GetPixel(60, 40).ToArgb());

                var presenter = (IClipboardDocumentPresenter)editor;
                Assert.True(presenter.TryExecute(EditorCommandId.Undo));
                Assert.Equal(new Size(160, 120), editor.ViewportDiagnostics.ImagePixelSize);
                Assert.Equal(selection, editor.SelectionDiagnostics.Selection);
                Assert.Equal(1, editor.TestTextAnnotationCount);
                Assert.Equal(1, editor.ImageLayerCountForTests);
                Assert.Equal(originalLayerFrame, editor.GetImageLayerFrameForTests(0));
                Assert.True(presenter.TryExecute(EditorCommandId.Redo));
                Assert.Equal(new Size(110, 80), editor.ViewportDiagnostics.ImagePixelSize);
                Assert.Equal(0, editor.TestTextAnnotationCount);
                Assert.Equal(0, editor.ImageLayerCountForTests);
            });
        }

        [Fact]
        public void CornerDragAtZoom_ClampsToImage_AndEscapeDoesNotChangeImage()
        {
            StaTest.Run(() =>
            {
                using var editor = EditorFixture.WithCanvas(160, 120);
                editor.TestSetZoom(2m);
                editor.ActivateStraightenTool();
                Drag(editor, new Point(20, 20), new Point(130, 100));
                Drag(editor, new Point(20, 20), new Point(-30, -40));
                Assert.Equal(Point.Empty, editor.TestStraightenCorners![0]);
                Assert.True(editor.TestStraightenApplyEnabled);
                editor.TestFireKeyDown(Keys.Escape);
                Assert.Equal(new Size(160, 120), editor.ViewportDiagnostics.ImagePixelSize);
                Assert.Contains("canUndo=False", editor.TestDescribeUndoStack());
            });
        }

        [Fact]
        public void PerspectiveWarp_MapsAllFourSkewedCornersToOutputCorners()
        {
            StaTest.Run(() =>
            {
                using var source = new Bitmap(160, 120, PixelFormat.Format32bppArgb);
                var corners = new[] { new Point(25, 15), new Point(120, 25), new Point(140, 100), new Point(10, 90) };
                var colors = new[] { Color.Red, Color.Lime, Color.Blue, Color.Yellow };
                using (var graphics = Graphics.FromImage(source))
                {
                    graphics.Clear(Color.White);
                    for (int i = 0; i < 4; i++)
                    {
                        using var brush = new SolidBrush(colors[i]);
                        graphics.FillRectangle(brush, corners[i].X - 3, corners[i].Y - 3, 7, 7);
                    }
                }
                using var result = ImageStraightener.CorrectPerspective(source, corners);
                Assert.Equal(colors[0].ToArgb(), result.GetPixel(0, 0).ToArgb());
                Assert.Equal(colors[1].ToArgb(), result.GetPixel(result.Width - 1, 0).ToArgb());
                Assert.Equal(colors[2].ToArgb(), result.GetPixel(result.Width - 1, result.Height - 1).ToArgb());
                Assert.Equal(colors[3].ToArgb(), result.GetPixel(0, result.Height - 1).ToArgb());
                Assert.InRange(result.Width, 131, 132);
                Assert.InRange(result.Height, 77, 79);
            });
        }

        [Fact]
        public void AxisAlignedWarp_PreservesPixelsAndAlpha_WithoutOffByOneResize()
        {
            StaTest.Run(() =>
            {
                using var source = new Bitmap(80, 60, PixelFormat.Format32bppArgb);
                for (int y = 0; y < source.Height; y++)
                for (int x = 0; x < source.Width; x++)
                    source.SetPixel(x, y, Color.FromArgb(80 + x, x * 3, y * 4, 30));
                using var result = ImageStraightener.CorrectPerspective(source,
                    new[] { new Point(10, 5), new Point(49, 5), new Point(49, 34), new Point(10, 34) });
                Assert.Equal(new Size(40, 30), result.Size);
                for (int y = 0; y < result.Height; y++)
                for (int x = 0; x < result.Width; x++)
                    Assert.Equal(source.GetPixel(x + 10, y + 5), result.GetPixel(x, y));
            });
        }

        [Fact]
        public void WarpRejectsDegenerateCrossedAndOutOfBoundsCorners()
        {
            StaTest.Run(() =>
            {
                using var source = new Bitmap(40, 40);
                Assert.Throws<ArgumentException>(() => ImageStraightener.CorrectPerspective(source,
                    new[] { new Point(0, 0), new Point(30, 30), new Point(30, 0), new Point(0, 30) }));
                Assert.Throws<ArgumentException>(() => ImageStraightener.CorrectPerspective(source,
                    new[] { new Point(0, 0), new Point(30, 0), new Point(30, 0), new Point(0, 0) }));
                Assert.Throws<ArgumentOutOfRangeException>(() => ImageStraightener.CorrectPerspective(source,
                    new[] { new Point(-1, 0), new Point(30, 0), new Point(30, 30), new Point(-1, 30) }));
            });
        }
    }
}
