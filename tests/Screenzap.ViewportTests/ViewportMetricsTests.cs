using System.Drawing;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class ViewportMetricsTests
    {
        [Fact]
        public void ResetView_CentersImageWithinClient()
        {
            using var control = new ImageViewportControl
            {
                ClientSize = new Size(400, 300)
            };

            control.Image = new Bitmap(100, 80);

            var metrics = control.Metrics;
            Assert.True(metrics.HasImage);
            Assert.Equal(1m, metrics.ZoomLevel);
            Assert.Equal(new Size(100, 80), metrics.ImagePixelSize);

            var expectedLeft = (metrics.ClientSize.Width - metrics.ScaledImageSize.Width) / 2f;
            var expectedTop = (metrics.ClientSize.Height - metrics.ScaledImageSize.Height) / 2f;

            Assert.Equal(expectedLeft, metrics.ImageClientRectangle.Left);
            Assert.Equal(expectedTop, metrics.ImageClientRectangle.Top);
        }

        [Fact]
        public void ZoomLevel_ClampsToSixtyFourTimes()
        {
            using var control = new ImageViewportControl { ClientSize = new Size(200, 200) };
            control.Image = new Bitmap(50, 50);

            control.ZoomLevel = 100m;               // request way past the ceiling
            Assert.Equal(64m, control.ZoomLevel);   // 6400%

            control.ZoomLevel = 0m;                 // and past the floor
            Assert.Equal(0.025m, control.ZoomLevel);
        }

        [Fact]
        public void PanBy_LargeImageOverscrollsPastEdges_UpToVisibleMargin()
        {
            using var control = new ImageViewportControl
            {
                ClientSize = new Size(200, 200)
            };

            control.Image = new Bitmap(500, 400);
            control.ZoomLevel = 1m;
            var margin = ImageViewportControl.OverscrollVisibleMargin;

            // Panning right/down pulls the image's top-left corner into the viewport
            // (Photoshop-style overscroll), stopping when only the margin remains visible.
            control.PanBy(new Size(5000, 5000));
            var metrics = control.Metrics;

            Assert.Equal(200 - margin, metrics.ImageClientRectangle.Left);
            Assert.Equal(200 - margin, metrics.ImageClientRectangle.Top);

            // Panning left/up likewise keeps the margin of the far edge visible.
            control.PanBy(new Size(-10000, -10000));
            metrics = control.Metrics;

            Assert.Equal(margin - 500, metrics.ImageClientRectangle.Left);
            Assert.Equal(margin - 400, metrics.ImageClientRectangle.Top);
        }

        [Fact]
        public void PanBy_SmallImageOverpansPastEdges_UpToVisibleMargin()
        {
            using var control = new ImageViewportControl
            {
                ClientSize = new Size(400, 300)
            };

            control.Image = new Bitmap(100, 80);
            var margin = ImageViewportControl.OverscrollVisibleMargin;

            control.PanBy(new Size(5000, 5000));
            var metrics = control.Metrics;

            Assert.Equal(400 - margin, metrics.ImageClientRectangle.Left);
            Assert.Equal(300 - margin, metrics.ImageClientRectangle.Top);

            control.PanBy(new Size(-10000, -10000));
            metrics = control.Metrics;

            Assert.Equal(margin - 100, metrics.ImageClientRectangle.Left);
            Assert.Equal(margin - 80, metrics.ImageClientRectangle.Top);
        }

        [Fact]
        public void PanBy_ImageNarrowerThanViewportStillOverpansHorizontally()
        {
            using var control = new ImageViewportControl
            {
                ClientSize = new Size(300, 200)
            };

            control.Image = new Bitmap(120, 400);
            var margin = ImageViewportControl.OverscrollVisibleMargin;

            control.PanBy(new Size(5000, 5000));
            var metrics = control.Metrics;

            Assert.Equal(300 - margin, metrics.ImageClientRectangle.Left);
            Assert.Equal(200 - margin, metrics.ImageClientRectangle.Top);

            control.PanBy(new Size(-10000, -10000));
            metrics = control.Metrics;

            Assert.Equal(margin - 120, metrics.ImageClientRectangle.Left);
            Assert.Equal(margin - 400, metrics.ImageClientRectangle.Top);
        }

        [Fact]
        public void SelectionMetrics_DetectsOutOfBounds()
        {
            var imageBounds = new Rectangle(0, 0, 200, 100);
            var selection = new Rectangle(-50, -10, 120, 80);

            var metrics = SelectionMetrics.From(selection, imageBounds);

            Assert.False(metrics.IsWithinBounds);
            Assert.True(metrics.HasSelection);
            Assert.Equal(new Rectangle(0, 0, 70, 70), metrics.ClampedSelection);
        }
    }
}
