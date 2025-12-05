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
        public void PanBy_ClampsLargeImageWithinViewport()
        {
            using var control = new ImageViewportControl
            {
                ClientSize = new Size(200, 200)
            };

            control.Image = new Bitmap(500, 400);
            control.ZoomLevel = 1m;

            control.PanBy(new Size(500, 500));
            var metrics = control.Metrics;

            Assert.True(metrics.ImageClientRectangle.Right >= control.ClientSize.Width - 0.01f);
            Assert.True(metrics.ImageClientRectangle.Bottom >= control.ClientSize.Height - 0.01f);

            control.PanBy(new Size(-1000, -1000));
            metrics = control.Metrics;

            Assert.True(metrics.ImageClientRectangle.Left <= 0.01f);
            Assert.True(metrics.ImageClientRectangle.Top <= 0.01f);
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
