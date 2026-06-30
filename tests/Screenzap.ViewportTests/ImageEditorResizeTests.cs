using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class ImageEditorResizeTests
    {
        [Fact]
        public void ResizeDialog_ConstrainedTyping_RecalculatesOppositeDimensionLive()
        {
            StaTest.Run(() =>
            {
                using var dialog = new screenzap.ResizeImageDialog(new Size(100, 50));

                dialog.SetWidthTextForTests("200");

                Assert.Equal(200m, dialog.WidthValueForTests);
                Assert.Equal(100m, dialog.HeightValueForTests);
                Assert.Equal(new Size(200, 100), dialog.TargetSize);

                dialog.SetHeightTextForTests("25");

                Assert.Equal(50m, dialog.WidthValueForTests);
                Assert.Equal(25m, dialog.HeightValueForTests);
                Assert.Equal(new Size(50, 25), dialog.TargetSize);
            });
        }

        [Fact]
        public void ResizeDialog_ReEnablingConstraint_ReconcilesLastEditedDimension()
        {
            StaTest.Run(() =>
            {
                using var dialog = new screenzap.ResizeImageDialog(new Size(100, 50));
                dialog.ConstrainProportionsForTests = false;
                dialog.SetWidthTextForTests("300");

                Assert.Equal(50m, dialog.HeightValueForTests);

                dialog.ConstrainProportionsForTests = true;

                Assert.Equal(150m, dialog.HeightValueForTests);
                Assert.Equal(new Size(300, 150), dialog.TargetSize);
            });
        }

        [Fact]
        public void ResizeImage_ScalesFullCanvasSelection_AndSupportsUndoRedo()
        {
            StaTest.Run(() =>
            {
                using var editor = new screenzap.ImageEditor();
                using var source = new Bitmap(4, 2);
                source.SetPixel(0, 0, Color.Red);
                source.SetPixel(3, 1, Color.Blue);
                editor.LoadImage(source);
                editor.SetSelectionForDiagnostics(new Rectangle(1, 0, 2, 2));

                Assert.True(editor.ResizeImageCommandAvailableForTests);
                Assert.True(editor.ExecuteResizeImageForDiagnostics(
                    new Size(8, 4),
                    InterpolationMode.NearestNeighbor));

                Assert.Equal(new Size(8, 4), editor.ViewportDiagnostics.ImagePixelSize);
                Assert.Equal(
                    new Rectangle(2, 0, 4, 4),
                    editor.SelectionDiagnostics.Selection);

                using (var resized = editor.CloneBaseBitmapForTests())
                {
                    Assert.NotNull(resized);
                    Assert.Equal(Color.Red.ToArgb(), resized!.GetPixel(0, 0).ToArgb());
                    Assert.Equal(Color.Blue.ToArgb(), resized.GetPixel(7, 3).ToArgb());
                }

                var presenter = (IClipboardDocumentPresenter)editor;
                Assert.True(presenter.TryExecute(EditorCommandId.Undo));
                Assert.Equal(new Size(4, 2), editor.ViewportDiagnostics.ImagePixelSize);
                Assert.Equal(
                    new Rectangle(1, 0, 2, 2),
                    editor.SelectionDiagnostics.Selection);

                Assert.True(presenter.TryExecute(EditorCommandId.Redo));
                Assert.Equal(new Size(8, 4), editor.ViewportDiagnostics.ImagePixelSize);
            });
        }

        [Fact]
        public void ResizeImage_ScalesFloatingClipboardLayersWithCanvas()
        {
            StaTest.Run(() =>
            {
                using var editor = new screenzap.ImageEditor();
                using var canvas = new Bitmap(80, 60);
                editor.LoadImage(canvas);

                using var pasted = new Bitmap(20, 10);
                editor.SetInternalClipboardImageForDiagnostics(pasted);
                Assert.True(editor.PasteFromClipboardForDiagnostics());
                Assert.Equal(new RectangleF(30f, 25f, 20f, 10f), editor.GetImageLayerFrameForTests(0));

                Assert.True(editor.ExecuteResizeImageForDiagnostics(new Size(160, 120)));

                Assert.Equal(
                    new RectangleF(60f, 50f, 40f, 20f),
                    editor.GetImageLayerFrameForTests(0));

                var presenter = (IClipboardDocumentPresenter)editor;
                Assert.True(presenter.TryExecute(EditorCommandId.Undo));
                Assert.Equal(
                    new RectangleF(30f, 25f, 20f, 10f),
                    editor.GetImageLayerFrameForTests(0));
            });
        }

        [Fact]
        public void ResizeImage_ScalesEditableAnnotationsAndText()
        {
            StaTest.Run(() =>
            {
                using var editor = new screenzap.ImageEditor();
                using var canvas = new Bitmap(100, 50);
                editor.LoadImage(canvas);

                var shape = new screenzap.AnnotationShape
                {
                    Type = screenzap.AnnotationType.Rectangle,
                    Start = new Point(10, 5),
                    End = new Point(30, 15),
                    LineThickness = 3f
                };
                var text = new screenzap.TextAnnotation
                {
                    Position = new Point(12, 8),
                    Text = "Resize me",
                    FontSize = 10f,
                    OutlineThickness = 1f
                };
                GetPrivateList<screenzap.AnnotationShape>(editor, "annotationShapes").Add(shape);
                GetPrivateList<screenzap.TextAnnotation>(editor, "textAnnotations").Add(text);

                Assert.True(editor.ExecuteResizeImageForDiagnostics(new Size(200, 100)));

                Assert.Equal(new Point(20, 10), shape.Start);
                Assert.Equal(new Point(60, 30), shape.End);
                Assert.Equal(6f, shape.LineThickness);
                Assert.Equal(new Point(24, 16), text.Position);
                Assert.Equal(20f, text.FontSize);
                Assert.Equal(2f, text.OutlineThickness);

                var presenter = (IClipboardDocumentPresenter)editor;
                Assert.True(presenter.TryExecute(EditorCommandId.Undo));

                var restoredShape = Assert.Single(
                    GetPrivateList<screenzap.AnnotationShape>(editor, "annotationShapes"));
                var restoredText = Assert.Single(
                    GetPrivateList<screenzap.TextAnnotation>(editor, "textAnnotations"));
                Assert.Equal(new Point(10, 5), restoredShape.Start);
                Assert.Equal(3f, restoredShape.LineThickness);
                Assert.Equal(new Point(12, 8), restoredText.Position);
                Assert.Equal(10f, restoredText.FontSize);
            });
        }

        private static List<T> GetPrivateList<T>(screenzap.ImageEditor editor, string fieldName)
        {
            var field = typeof(screenzap.ImageEditor).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            return Assert.IsType<List<T>>(field?.GetValue(editor));
        }
    }
}
