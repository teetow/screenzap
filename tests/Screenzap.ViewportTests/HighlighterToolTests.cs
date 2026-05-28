using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Xunit;

namespace Screenzap.ViewportTests
{
    /// <summary>
    /// Covers the highlighter pen: the pure polyline decimation/smoothing helpers, freehand
    /// capture through the real input pipeline, hit-test/move, and the dedicated thickness combo.
    /// </summary>
    public class HighlighterToolTests
    {
        private static screenzap.ImageEditor PrepareEditor()
        {
            var editor = new screenzap.ImageEditor();
            var canvas = new Bitmap(200, 120);
            using (var g = Graphics.FromImage(canvas))
                g.Clear(Color.White);
            editor.LoadImage(canvas);
            canvas.Dispose();
            return editor;
        }

        // A roughly-horizontal scribble with sub-pixel jitter the decimator should remove.
        private static List<Point> SampleStroke()
        {
            return new List<Point>
            {
                new Point(20, 40), new Point(30, 41), new Point(40, 40), new Point(50, 41),
                new Point(60, 40), new Point(70, 41), new Point(80, 40), new Point(90, 40),
                new Point(100, 41), new Point(110, 40)
            };
        }

        private static int DistanceFromWhite(Color color)
        {
            return (255 - color.R) + (255 - color.G) + (255 - color.B);
        }

        private static Point FindMostTintedPoint(Bitmap bitmap, Rectangle searchArea)
        {
            Point best = searchArea.Location;
            int bestInk = -1;

            for (int x = searchArea.Left; x < searchArea.Right; x++)
            {
                for (int y = searchArea.Top; y < searchArea.Bottom; y++)
                {
                    int ink = DistanceFromWhite(bitmap.GetPixel(x, y));
                    if (ink > bestInk)
                    {
                        bestInk = ink;
                        best = new Point(x, y);
                    }
                }
            }

            return best;
        }

        [Fact]
        public void SimplifyPolyline_CollapsesStraightLine_ToEndpoints()
        {
            var points = new List<Point>();
            for (int x = 0; x <= 100; x += 5)
                points.Add(new Point(x, 50));

            var simplified = screenzap.ImageEditor.SimplifyPolyline(points, 1.5);

            Assert.Equal(2, simplified.Count);
            Assert.Equal(new Point(0, 50), simplified[0]);
            Assert.Equal(new Point(100, 50), simplified[^1]);
        }

        [Fact]
        public void SimplifyPolyline_PreservesEndpoints_AndCorner()
        {
            // An L-shape: the corner at (50,50) must survive decimation.
            var points = new List<Point>
            {
                new Point(0, 50), new Point(25, 50), new Point(50, 50),
                new Point(50, 75), new Point(50, 100)
            };

            var simplified = screenzap.ImageEditor.SimplifyPolyline(points, 1.5);

            Assert.Contains(new Point(50, 50), simplified);
            Assert.Equal(new Point(0, 50), simplified[0]);
            Assert.Equal(new Point(50, 100), simplified[^1]);
        }

        [Fact]
        public void SmoothPolyline_PreservesEndpoints_AndDampsSpike()
        {
            var points = new List<Point>
            {
                new Point(0, 0), new Point(10, 0), new Point(20, 30), new Point(30, 0), new Point(40, 0)
            };

            var smoothed = screenzap.ImageEditor.SmoothPolyline(points, 3);

            Assert.Equal(points[0], smoothed[0]);
            Assert.Equal(points[^1], smoothed[^1]);
            // The spike at index 2 is averaged with its zero-height neighbours → much lower.
            Assert.True(smoothed[2].Y < 30);
        }

        [Fact]
        public void DrawHighlighter_CreatesShape_WithDecimatedPath()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();
                editor.TestToggleHighlighterTool();
                Assert.Equal(screenzap.DrawingTool.Highlighter, editor.TestActiveDrawingTool);

                var stroke = SampleStroke();
                editor.TestDrawHighlighterStroke(stroke);

                Assert.Equal(1, editor.TestAnnotationShapeCount);
                Assert.NotNull(editor.TestSelectedAnnotation);
                Assert.Equal(screenzap.AnnotationType.Highlighter, editor.TestSelectedAnnotation!.Type);
                // Decimation should keep at least the endpoints but fewer than the raw samples.
                int count = editor.TestSelectedHighlighterPointCount;
                Assert.True(count >= 2);
                Assert.True(count < stroke.Count);
            });
        }

        [Fact]
        public void DrawHighlighter_TooShortStroke_IsDiscarded()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();
                editor.TestToggleHighlighterTool();

                // A 2px dab is below the 4px validity threshold.
                editor.TestDrawHighlighterStroke(new List<Point> { new Point(50, 50), new Point(51, 51) });

                Assert.Equal(0, editor.TestAnnotationShapeCount);
            });
        }

        [Fact]
        public void Highlighter_InMoveMode_SelectableByBodyClick()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();
                editor.TestToggleHighlighterTool();
                editor.TestDrawHighlighterStroke(SampleStroke());
                editor.TestDeactivateDrawingTool();

                // Deselect, then click on the stroke path.
                editor.TestFireMouseDownAtImagePixel(new Point(180, 110), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(180, 110), MouseButtons.Left);
                Assert.Null(editor.TestSelectedAnnotation);

                editor.TestFireMouseDownAtImagePixel(new Point(65, 40), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(65, 40), MouseButtons.Left);

                Assert.NotNull(editor.TestSelectedAnnotation);
                Assert.Equal(screenzap.AnnotationType.Highlighter, editor.TestSelectedAnnotation!.Type);
            });
        }

        [Fact]
        public void Highlighter_Drag_TranslatesEveryPathPoint()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();
                editor.TestToggleHighlighterTool();
                editor.TestDrawHighlighterStroke(SampleStroke());
                editor.TestDeactivateDrawingTool();

                var before = editor.TestSelectedAnnotation!.Points!.ToList();

                // Drag the body by (+10, +5).
                editor.TestFireMouseDownAtImagePixel(new Point(65, 40), MouseButtons.Left);
                editor.TestFireMouseMoveAtImagePixel(new Point(75, 45), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(75, 45), MouseButtons.Left);

                var after = editor.TestSelectedAnnotation!.Points!;
                Assert.Equal(before.Count, after.Count);
                for (int i = 0; i < before.Count; i++)
                {
                    Assert.Equal(before[i].X + 10, after[i].X);
                    Assert.Equal(before[i].Y + 5, after[i].Y);
                }
            });
        }

        // A diagonal zig-zag so the bounding box has real 2D extent (a flat horizontal stroke
        // would have zero height and nothing to scale vertically).
        private static List<Point> DiagonalStroke()
        {
            return new List<Point>
            {
                new Point(30, 30), new Point(60, 50), new Point(90, 32), new Point(110, 60)
            };
        }

        [Fact]
        public void Highlighter_CornerDrag_ScalesWholePolyline()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();
                editor.TestToggleHighlighterTool();
                editor.TestDrawHighlighterStroke(DiagonalStroke());
                editor.TestDeactivateDrawingTool();

                var ob = editor.TestSelectedAnnotation!.GetBounds();
                int pointsBefore = editor.TestSelectedAnnotation!.Points!.Count;

                // Grab the bottom-right corner handle and drag it out by (+20, +20).
                editor.TestFireMouseDownAtImagePixel(new Point(ob.Right, ob.Bottom), MouseButtons.Left);
                editor.TestFireMouseMoveAtImagePixel(new Point(ob.Right + 20, ob.Bottom + 20), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(ob.Right + 20, ob.Bottom + 20), MouseButtons.Left);

                var nb = editor.TestSelectedAnnotation!.GetBounds();
                // Anchor (top-left) stays put; the dragged corner grows by the drag delta.
                Assert.InRange(nb.Left, ob.Left - 1, ob.Left + 1);
                Assert.InRange(nb.Top, ob.Top - 1, ob.Top + 1);
                Assert.InRange(nb.Right, ob.Right + 20 - 2, ob.Right + 20 + 2);
                Assert.InRange(nb.Bottom, ob.Bottom + 20 - 2, ob.Bottom + 20 + 2);
                // Scaling maps every original vertex — the vertex count is unchanged.
                Assert.Equal(pointsBefore, editor.TestSelectedAnnotation!.Points!.Count);
            });
        }

        [Fact]
        public void Highlighter_CornerResize_IsUndoable()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();
                editor.TestToggleHighlighterTool();
                editor.TestDrawHighlighterStroke(DiagonalStroke());
                editor.TestDeactivateDrawingTool();

                var ob = editor.TestSelectedAnnotation!.GetBounds();
                editor.TestFireMouseDownAtImagePixel(new Point(ob.Right, ob.Bottom), MouseButtons.Left);
                editor.TestFireMouseMoveAtImagePixel(new Point(ob.Right + 20, ob.Bottom + 20), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(ob.Right + 20, ob.Bottom + 20), MouseButtons.Left);
                Assert.NotEqual(ob.Right, editor.TestSelectedAnnotation!.GetBounds().Right);

                editor.TestFireKeyDown(Keys.Control | Keys.Z);

                var restored = editor.TestSelectedAnnotation!.GetBounds();
                Assert.InRange(restored.Right, ob.Right - 1, ob.Right + 1);
                Assert.InRange(restored.Bottom, ob.Bottom - 1, ob.Bottom + 1);
            });
        }

        [Fact]
        public void HighlighterThickness_AppliesToSelection_AndIsUndoable()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();
                editor.TestToggleHighlighterTool();
                editor.TestDrawHighlighterStroke(SampleStroke());
                // Drawn with the 12 default; bump to 20.
                editor.TestSetHighlighterThickness(20f);
                Assert.Equal(20f, editor.TestSelectedAnnotation!.LineThickness);

                editor.TestFireKeyDown(Keys.Control | Keys.Z);
                Assert.Equal(12f, editor.TestSelectedAnnotation!.LineThickness);
            });
        }

        [Fact]
        public void HighlighterOpacity_AppliesToSelection_AndIsUndoable()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();
                editor.TestToggleHighlighterTool();
                editor.TestDrawHighlighterStroke(SampleStroke());

                editor.TestSetHighlighterOpacityPercent(70);

                Assert.InRange(editor.TestSelectedAnnotation!.Opacity, 0.699f, 0.701f);
                Assert.Equal("70%", editor.TestHighlighterOpacityValueLabelText);

                editor.TestFireKeyDown(Keys.Control | Keys.Z);

                Assert.InRange(editor.TestSelectedAnnotation!.Opacity, 0.399f, 0.401f);
            });
        }

        [Fact]
        public void DrawHighlighter_FlattensIntoComposite_AsTranslucentInk()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();
                editor.TestToggleHighlighterTool();
                editor.TestApplyColorToSelection(Color.Yellow); // set the tool default color
                editor.TestSetHighlighterThickness(24f);
                editor.TestDrawHighlighterStroke(SampleStroke());

                // BuildCompositeImage runs DrawHighlighter on the Image surface, exercising the
                // offscreen buffer + horizontal blur + value-noise post-process (LockBits path).
                using var composite = editor.BuildCompositeImageForTests();

                // Scan the band the stroke runs through for a pixel that is tinted toward yellow
                // but still partially transparent over white (i.e. neither untouched white nor
                // fully-opaque yellow) — the signature of a translucent highlighter wash.
                bool foundTranslucentInk = false;
                for (int x = 25; x < 110 && !foundTranslucentInk; x++)
                {
                    for (int y = 30; y <= 50; y++)
                    {
                        var px = composite.GetPixel(x, y);
                        bool tintedYellow = px.B < 250 && px.R > 200 && px.G > 200;
                        bool notFullyOpaque = px.B > 40; // pure yellow over white would push B near 0
                        if (tintedYellow && notFullyOpaque)
                        {
                            foundTranslucentInk = true;
                            break;
                        }
                    }
                }

                Assert.True(foundTranslucentInk, "expected a translucent yellow wash in the composite");
            });
        }

        [Fact]
        public void HighlighterOpacity_LowerValueLightensComposite()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();
                editor.TestToggleHighlighterTool();
                editor.TestApplyColorToSelection(Color.Yellow);
                editor.TestSetHighlighterThickness(24f);
                editor.TestDrawHighlighterStroke(SampleStroke());

                editor.TestSetHighlighterOpacityPercent(80);
                using var strongComposite = editor.BuildCompositeImageForTests();
                Point sample = FindMostTintedPoint(strongComposite, new Rectangle(25, 30, 85, 21));
                int strongInk = DistanceFromWhite(strongComposite.GetPixel(sample.X, sample.Y));

                editor.TestSetHighlighterOpacityPercent(10);
                using var lightComposite = editor.BuildCompositeImageForTests();
                int lightInk = DistanceFromWhite(lightComposite.GetPixel(sample.X, sample.Y));

                Assert.True(strongInk > 0, "expected to find a tinted highlighter pixel");
                Assert.True(lightInk < strongInk, $"expected lower opacity to lighten the wash at {sample}, but {lightInk} >= {strongInk}");
            });
        }

        [Fact]
        public void HighlighterThicknessCombo_ShowsBlank_WhenSelectionIsMixed()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();
                editor.TestToggleHighlighterTool();

                // Two strokes at the default thickness (12), in separate bands.
                editor.TestDrawHighlighterStroke(SampleStroke());
                editor.TestDrawHighlighterStroke(new List<Point>
                {
                    new Point(20, 90), new Point(60, 90), new Point(100, 91), new Point(140, 90)
                });
                editor.TestDeactivateDrawingTool();

                // Deselect, then select only the first stroke and bump it to 20 → the two
                // strokes now disagree (20 vs 12).
                editor.TestFireMouseDownAtImagePixel(new Point(180, 110), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(180, 110), MouseButtons.Left);
                editor.TestFireMouseDownAtImagePixel(new Point(65, 40), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(65, 40), MouseButtons.Left);
                editor.TestSetHighlighterThickness(20f);

                // Add the second stroke to the selection → mixed thickness → blank combo.
                editor.TestShiftClickAtImagePixel(new Point(65, 90));
                Assert.Equal(2, editor.TestSelectedShapeCount);

                Assert.Equal(-1, editor.TestHighlighterThicknessComboBoxSelectedIndex);
            });
        }

        [Fact]
        public void HighlighterOpacityValueLabel_ShowsMixed_WhenSelectionIsMixed()
        {
            StaTest.Run(() =>
            {
                using var editor = PrepareEditor();
                editor.TestToggleHighlighterTool();

                editor.TestDrawHighlighterStroke(SampleStroke());
                editor.TestDrawHighlighterStroke(new List<Point>
                {
                    new Point(20, 90), new Point(60, 90), new Point(100, 91), new Point(140, 90)
                });
                editor.TestDeactivateDrawingTool();

                editor.TestFireMouseDownAtImagePixel(new Point(180, 110), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(180, 110), MouseButtons.Left);
                editor.TestFireMouseDownAtImagePixel(new Point(65, 40), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(65, 40), MouseButtons.Left);
                editor.TestSetHighlighterOpacityPercent(70);

                editor.TestShiftClickAtImagePixel(new Point(65, 90));

                Assert.Equal(2, editor.TestSelectedShapeCount);
                Assert.Equal("Mixed", editor.TestHighlighterOpacityValueLabelText);
            });
        }
    }
}
