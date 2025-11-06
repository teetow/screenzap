using System.Drawing;

namespace TextDetection
{
    public readonly struct DetectedTextRegion
    {
        public DetectedTextRegion(Rectangle bounds, float confidence)
        {
            Bounds = bounds;
            Confidence = confidence;
        }

        public Rectangle Bounds { get; }

        public float Confidence { get; }
    }
}
