using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace screenzap
{
    public partial class ImageEditor
    {
        private readonly List<ImageLayer> imageLayers = new List<ImageLayer>();

        internal int ImageLayerCountForTests => imageLayers.Count;

        internal RectangleF GetImageLayerFrameForTests(int index) => imageLayers[index].Frame;

        internal Bitmap BuildCompositeImageForTests() => BuildCompositeImage();

        internal Bitmap? CloneBaseBitmapForTests() => pictureBox1?.Image is Bitmap b ? new Bitmap(b) : null;

        internal void RenderScreenLayersForTests(Graphics graphics) => DrawImageLayers(graphics, AnnotationSurface.Screen);

        private List<ImageLayer> CloneLayers()
        {
            return imageLayers.Select(layer => layer.Clone()).ToList();
        }

        private void ApplyLayerState(List<ImageLayer>? source)
        {
            // Convention (mirrors ApplyAnnotationState): null means "not tracked, leave live state alone".
            // An empty list means "set live layers to empty".
            if (source == null)
            {
                return;
            }

            ClearImageLayers();

            foreach (var layer in source)
            {
                imageLayers.Add(layer.Clone());
            }
        }

        private void ClearImageLayers()
        {
            foreach (var layer in imageLayers)
            {
                layer.Dispose();
            }
            imageLayers.Clear();
        }

        private void DrawImageLayers(Graphics graphics, AnnotationSurface surface)
        {
            if (imageLayers.Count == 0)
            {
                return;
            }

            float zoom = 1f;
            PointF pan = PointF.Empty;
            if (surface == AnnotationSurface.Screen && pictureBox1 != null)
            {
                zoom = (float)pictureBox1.ZoomLevel;
                pan = pictureBox1.Metrics.PanOffset;
            }

            foreach (var layer in imageLayers)
            {
                var dest = new RectangleF(
                    pan.X + layer.Frame.X * zoom,
                    pan.Y + layer.Frame.Y * zoom,
                    layer.Frame.Width * zoom,
                    layer.Frame.Height * zoom);

                graphics.DrawImage(layer.Source, dest, layer.Fill, GraphicsUnit.Pixel);
            }
        }
    }
}
