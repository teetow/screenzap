using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Windows.Forms;

namespace screenzap
{
    public partial class ImageDocumentEditor
    {
        internal bool ExecuteResizeImageForDiagnostics(Size targetSize, InterpolationMode interpolationMode = InterpolationMode.HighQualityBicubic)
        {
            return ExecuteResizeImage(targetSize, interpolationMode);
        }

        private bool ExecuteResizeImage(Size targetSize, InterpolationMode interpolationMode)
        {
            if (!HasEditableImage || viewport.Image == null || targetSize.Width < 1 || targetSize.Height < 1 || targetSize == viewport.Image.Size)
            {
                return false;
            }

            FinalizeActiveTextAnnotation();
            var sourceImage = viewport.Image;
            var sourceSize = sourceImage.Size;
            float scaleX = (float)targetSize.Width / sourceSize.Width;
            float scaleY = (float)targetSize.Height / sourceSize.Height;
            float contentScale = (float)Math.Sqrt(scaleX * scaleY);
            var beforeImage = new Bitmap(sourceImage);
            var selectionBefore = Selection;
            var shapesBefore = CloneAnnotations();
            var textsBefore = CloneTextAnnotations();
            var layersBefore = CloneLayers();
            var afterImage = new Bitmap(targetSize.Width, targetSize.Height, PixelFormat.Format32bppArgb);
            CopyImageResolution(sourceImage, afterImage);
            using (var graphics = Graphics.FromImage(afterImage))
            using (var attributes = new ImageAttributes())
            {
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = interpolationMode;
                graphics.PixelOffsetMode = interpolationMode == InterpolationMode.NearestNeighbor ? PixelOffsetMode.Half : PixelOffsetMode.HighQuality;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.DrawImage(sourceImage, new Rectangle(Point.Empty, targetSize), 0, 0, sourceSize.Width, sourceSize.Height, GraphicsUnit.Pixel, attributes);
            }

            viewport.Image = new Bitmap(afterImage);
            ScaleEditableContent(scaleX, scaleY, contentScale, targetSize);
            isPlaceholderImage = false;
            var selectionAfter = Selection;
            var shapesAfter = CloneAnnotations();
            var textsAfter = CloneTextAnnotations();
            var layersAfter = CloneLayers();
            PushUndoStep(Rectangle.Empty, beforeImage, afterImage, selectionBefore, selectionAfter, replacesImage: true, shapesBefore: shapesBefore, shapesAfter: shapesAfter, textsBefore: textsBefore, textsAfter: textsAfter, layersBefore: layersBefore, layersAfter: layersAfter);
            RecenterViewportAfterImageChange(resizeWindow: true);
            if (selectedTextAnnotation != null)
            {
            }

            viewport.Invalidate();
            return true;
        }

        private void ScaleEditableContent(float scaleX, float scaleY, float contentScale, Size targetSize)
        {
            Selection = ScaleRectangle(Selection, scaleX, scaleY);
            Selection = Rectangle.Intersect(new Rectangle(Point.Empty, targetSize), Selection);
            foreach (var shape in annotationShapes)
            {
                shape.Start = ScalePoint(shape.Start, scaleX, scaleY);
                shape.End = ScalePoint(shape.End, scaleX, scaleY);
                shape.LineThickness = Math.Max(0.1f, shape.LineThickness * contentScale);
                if (shape.Points != null)
                {
                    for (int index = 0; index < shape.Points.Count; index++)
                    {
                        shape.Points[index] = ScalePoint(shape.Points[index], scaleX, scaleY);
                    }

                    if (shape.Points.Count > 0)
                    {
                        shape.Start = shape.Points[0];
                        shape.End = shape.Points[shape.Points.Count - 1];
                    }
                }
            }

            foreach (var annotation in textAnnotations)
            {
                annotation.Position = ScalePoint(annotation.Position, scaleX, scaleY);
                annotation.FontSize = Math.Max(0.1f, annotation.FontSize * contentScale);
                annotation.OutlineThickness = Math.Max(0f, annotation.OutlineThickness * contentScale);
            }

            foreach (var layer in imageLayers)
            {
                var frame = layer.Frame;
                layer.Frame = new RectangleF(frame.X * scaleX, frame.Y * scaleY, frame.Width * scaleX, frame.Height * scaleY);
            }

            SyncSelectedAnnotation();
            SyncSelectedTextAnnotation();
        }

        private static Point ScalePoint(Point point, float scaleX, float scaleY)
        {
            return new Point((int)Math.Round(point.X * scaleX, MidpointRounding.AwayFromZero), (int)Math.Round(point.Y * scaleY, MidpointRounding.AwayFromZero));
        }

        private static Rectangle ScaleRectangle(Rectangle rectangle, float scaleX, float scaleY)
        {
            if (rectangle.IsEmpty)
            {
                return Rectangle.Empty;
            }

            int left = (int)Math.Round(rectangle.Left * scaleX, MidpointRounding.AwayFromZero);
            int top = (int)Math.Round(rectangle.Top * scaleY, MidpointRounding.AwayFromZero);
            int right = (int)Math.Round(rectangle.Right * scaleX, MidpointRounding.AwayFromZero);
            int bottom = (int)Math.Round(rectangle.Bottom * scaleY, MidpointRounding.AwayFromZero);
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private static void CopyImageResolution(Image source, Bitmap target)
        {
            if (source.HorizontalResolution <= 0f || source.VerticalResolution <= 0f)
            {
                return;
            }

            try
            {
                target.SetResolution(source.HorizontalResolution, source.VerticalResolution);
            }
            catch (ArgumentException)
            {
            // Some clipboard providers publish invalid DPI metadata. Pixel dimensions
            // are authoritative for this operation, so keep the bitmap default DPI.
            }
        }
    }
}
