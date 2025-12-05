using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace screenzap.Components.Shared
{
    public class ImageViewportControl : Control
    {
        private Image? image;
        private decimal zoomLevel = 1m;
        private PointF panOffset = PointF.Empty;
        private InterpolationMode interpolationMode = InterpolationMode.NearestNeighbor;

        public event EventHandler<PaintEventArgs>? OverlayPaint;

        public ImageViewportControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = SystemColors.ControlDarkDark;
            TabStop = true;
        }

        public Image? Image
        {
            get => image;
            set
            {
                var oldSize = GetImagePixelSize();
                Size newSize;
                try
                {
                    newSize = value?.Size ?? Size.Empty;
                }
                catch (ArgumentException)
                {
                    newSize = Size.Empty;
                }
                LogDebug($"Image setter: old={oldSize}, new={newSize}, ClientSize={ClientSize}");
                
                if (image == value)
                {
                    LogDebug("Image setter: same reference, skipping");
                    return;
                }

                image = value;
                LogDebug($"Image setter: calling ResetView, panOffset before={panOffset}");
                ResetView();
                LogDebug($"Image setter: after ResetView, panOffset={panOffset}");
                Invalidate();
            }
        }

        public bool HasImage => image != null;

        public decimal ZoomLevel
        {
            get => zoomLevel;
            set
            {
                var clamped = Math.Max(0.25m, Math.Min(8m, value));
                if (clamped == zoomLevel)
                {
                    return;
                }

                zoomLevel = clamped;
                ClampPan();
                Invalidate();
            }
        }

        public InterpolationMode InterpolationMode
        {
            get => interpolationMode;
            set
            {
                if (interpolationMode == value)
                {
                    return;
                }

                interpolationMode = value;
                Invalidate();
            }
        }

        public ViewportMetrics Metrics => new ViewportMetrics(
            HasImage,
            zoomLevel,
            panOffset,
            GetImagePixelSize(),
            GetScaledImageSize(),
            GetImageClientRectangle(),
            ClientSize);

        public Size GetImagePixelSize()
        {
            if (image == null)
            {
                return Size.Empty;
            }

            try
            {
                return image.Size;
            }
            catch (ArgumentException)
            {
                // Image was disposed
                return Size.Empty;
            }
        }

        public RectangleF GetImageClientRectangle()
        {
            var scaled = GetScaledImageSize();
            return new RectangleF(panOffset.X, panOffset.Y, scaled.Width, scaled.Height);
        }

        public void ResetView()
        {
            zoomLevel = 1m;
            CenterImage();
        }

        public void CenterImage([CallerMemberName] string? caller = null)
        {
            var imageSize = GetImagePixelSize();
            LogDebug($"CenterImage called by {caller}: image={imageSize}, ClientSize={ClientSize}");
            
            if (imageSize.IsEmpty || ClientSize.Width <= 0 || ClientSize.Height <= 0)
            {
                LogDebug($"CenterImage: early exit (null/zero), setting panOffset=Empty");
                panOffset = PointF.Empty;
                Invalidate();
                return;
            }

            var scaled = GetScaledImageSize();
            var centeredX = (ClientSize.Width - scaled.Width) / 2f;
            var centeredY = (ClientSize.Height - scaled.Height) / 2f;
            var newPan = new PointF(centeredX, centeredY);
            LogDebug($"CenterImage: scaled={scaled}, centered=({centeredX:F1}, {centeredY:F1})");
            panOffset = newPan;
            Invalidate();
        }

        public void ClampPan([CallerMemberName] string? caller = null)
        {
            LogDebug($"ClampPan called by {caller}: panOffset before={panOffset}, ClientSize={ClientSize}");
            
            var scaled = GetScaledImageSize();
            if (scaled.IsEmpty)
            {
                LogDebug("ClampPan: no image or disposed, setting panOffset=Empty");
                panOffset = PointF.Empty;
                return;
            }

            float constrainedX;
            if (scaled.Width <= ClientSize.Width)
            {
                constrainedX = (ClientSize.Width - scaled.Width) / 2f;
            }
            else
            {
                var minLeft = ClientSize.Width - scaled.Width;
                constrainedX = Math.Min(0, Math.Max(minLeft, panOffset.X));
            }

            float constrainedY;
            if (scaled.Height <= ClientSize.Height)
            {
                constrainedY = (ClientSize.Height - scaled.Height) / 2f;
            }
            else
            {
                var minTop = ClientSize.Height - scaled.Height;
                constrainedY = Math.Min(0, Math.Max(minTop, panOffset.Y));
            }

            var oldPan = panOffset;
            panOffset = new PointF(constrainedX, constrainedY);
            LogDebug($"ClampPan: scaled={scaled}, old={oldPan}, new={panOffset}");
        }

        public void PanBy(Size delta)
        {
            if (image == null)
            {
                return;
            }

            panOffset = new PointF(panOffset.X + delta.Width, panOffset.Y + delta.Height);
            ClampPan();
            Invalidate();
        }

        public void ZoomAround(decimal newZoom, Point clientFocus)
        {
            if (!HasImage)
            {
                ZoomLevel = newZoom;
                return;
            }

            var focusPixel = ClientToPixel(clientFocus);
            ZoomLevel = newZoom;
            var focusAfter = PixelToClient(focusPixel);
            var correction = new Size(focusAfter.X - clientFocus.X, focusAfter.Y - clientFocus.Y);
            panOffset = new PointF(panOffset.X - correction.Width, panOffset.Y - correction.Height);
            ClampPan();
            Invalidate();
        }

        public Point PixelToClient(Point pixel)
        {
            var pt = PixelToClientF(pixel);
            return Point.Round(pt);
        }

        public Rectangle PixelToClient(Rectangle rect)
        {
            var topLeft = PixelToClient(rect.Location);
            var bottomRight = PixelToClient(new Point(rect.Right, rect.Bottom));
            return Rectangle.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
        }

        public PointF PixelToClientF(Point pixel)
        {
            if (!HasImage)
            {
                return PointF.Empty;
            }

            return new PointF(
                panOffset.X + (float)(pixel.X * (double)zoomLevel),
                panOffset.Y + (float)(pixel.Y * (double)zoomLevel));
        }

        public RectangleF PixelToClientF(Rectangle rect)
        {
            var topLeft = PixelToClientF(rect.Location);
            var bottomRight = PixelToClientF(new Point(rect.Right, rect.Bottom));
            return RectangleF.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
        }

        public Point ClientToPixel(Point point)
        {
            if (!HasImage || zoomLevel == 0)
            {
                return Point.Empty;
            }

            return new Point(
                (int)Math.Round((point.X - panOffset.X) / (double)zoomLevel),
                (int)Math.Round((point.Y - panOffset.Y) / (double)zoomLevel));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);

            if (image != null)
            {
                e.Graphics.InterpolationMode = interpolationMode;
                if (interpolationMode == InterpolationMode.NearestNeighbor)
                {
                    e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
                }

                e.Graphics.DrawImage(image, GetImageClientRectangle());
            }

            OverlayPaint?.Invoke(this, e);
            base.OnPaint(e);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            LogDebug($"OnSizeChanged: new ClientSize={ClientSize}");
            base.OnSizeChanged(e);
            ClampPan();
            Invalidate();
        }

        private static void LogDebug(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] [ImageViewport] {message}";
            Debug.WriteLine(line);
            try
            {
                var logPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Screenzap", "viewport-debug.log");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
                System.IO.File.AppendAllText(logPath, line + Environment.NewLine);
            }
            catch { }
        }

        private SizeF GetScaledImageSize()
        {
            if (image == null)
            {
                return SizeF.Empty;
            }

            try
            {
                float scale = (float)zoomLevel;
                return new SizeF(image.Width * scale, image.Height * scale);
            }
            catch (ArgumentException)
            {
                // Image was disposed
                return SizeF.Empty;
            }
        }
    }

    public readonly struct ViewportMetrics
    {
        public ViewportMetrics(bool hasImage, decimal zoomLevel, PointF panOffset, Size imagePixelSize, SizeF scaledImageSize, RectangleF imageClientRect, Size clientSize)
        {
            HasImage = hasImage;
            ZoomLevel = zoomLevel;
            PanOffset = panOffset;
            ImagePixelSize = imagePixelSize;
            ScaledImageSize = scaledImageSize;
            ImageClientRectangle = imageClientRect;
            ClientSize = clientSize;
        }

        public bool HasImage { get; }
        public decimal ZoomLevel { get; }
        public PointF PanOffset { get; }
        public Size ImagePixelSize { get; }
        public SizeF ScaledImageSize { get; }
        public RectangleF ImageClientRectangle { get; }
        public Size ClientSize { get; }
    }
}
