using System.Drawing;

namespace screenzap.Components.Shared
{
    public readonly struct ReplaceBackgroundSourceEdges
    {
        public ReplaceBackgroundSourceEdges(bool useLeft, bool useTop, bool useRight, bool useBottom)
        {
            UseLeft = useLeft;
            UseTop = useTop;
            UseRight = useRight;
            UseBottom = useBottom;
        }

        public bool UseLeft { get; }

        public bool UseTop { get; }

        public bool UseRight { get; }

        public bool UseBottom { get; }

        public bool HasAnySource => UseLeft || UseTop || UseRight || UseBottom;
    }

    public static class ReplaceBackgroundInterpolation
    {
        public static ReplaceBackgroundSourceEdges DetermineSourceEdges(Rectangle selection, Size imageSize)
        {
            if (imageSize.Width <= 0 || imageSize.Height <= 0 || selection.Width <= 0 || selection.Height <= 0)
            {
                return default;
            }

            return new ReplaceBackgroundSourceEdges(
                useLeft: selection.Left > 0,
                useTop: selection.Top > 0,
                useRight: selection.Right < imageSize.Width,
                useBottom: selection.Bottom < imageSize.Height);
        }
    }
}