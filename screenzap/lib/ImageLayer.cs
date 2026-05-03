using System;
using System.Drawing;

namespace screenzap
{
    /// <summary>
    /// Non-destructive image layer ("smart object"). The Source bitmap is owned by the layer
    /// and never mutated by transforms; Frame/Fill/RotationDeg/Mask are sidecars applied at render time.
    /// </summary>
    internal sealed class ImageLayer : IDisposable
    {
        public ImageLayer(Bitmap source, RectangleF frame)
            : this(source, frame, new RectangleF(0f, 0f, source?.Width ?? 0, source?.Height ?? 0), 0f, null)
        {
        }

        public ImageLayer(Bitmap source, RectangleF frame, RectangleF fill, float rotationDeg, Bitmap? mask)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Frame = frame;
            Fill = fill;
            RotationDeg = rotationDeg;
            Mask = mask;
        }

        public Bitmap Source { get; }
        public RectangleF Frame { get; set; }
        public RectangleF Fill { get; set; }
        public float RotationDeg { get; set; }
        public Bitmap? Mask { get; }

        public ImageLayer Clone()
        {
            return new ImageLayer(
                new Bitmap(Source),
                Frame,
                Fill,
                RotationDeg,
                Mask == null ? null : new Bitmap(Mask));
        }

        public void Dispose()
        {
            Source.Dispose();
            Mask?.Dispose();
        }
    }
}
