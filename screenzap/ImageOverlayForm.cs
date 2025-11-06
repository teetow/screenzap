using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace screenzap
{
    class ImageOverlayForm : Form
    {
        private Point mouseHit;
        private Point formPosition;
        private bool isMouseDown;
        private bool isCtrlDown;
        private decimal zoomLevel = 1m;
        private static readonly decimal[] zoomLevels = new decimal[]
        {
            0.1m, 0.25m, 1m / 3m, 0.5m, 2m / 3m, 0.75m, 1.0m, 1.25m, 4m / 3m, 1.5m, 5m / 3m,
            2m, 3m, 4m, 6m, 8m, 10m, 15m, 20m, 30m, 40m,
        };

        public ImageOverlayForm()
        {
            this.Cursor = Cursors.Cross;
            this.TopMost = true;
            //this.ShowInTaskbar = false;
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Color.Gray;
            this.TransparencyKey = Color.Red;
            this.Opacity = 0.5;
            var screen = ResolveScreen();
            this.Width = screen.WorkingArea.Width;
            this.Height = screen.WorkingArea.Height;
            //this.WindowState = FormWindowState.Maximized;
            this.DoubleBuffered = true;
            this.BackgroundImageLayout = ImageLayout.Stretch;
        }

        private static Screen ResolveScreen()
        {
            var primary = Screen.PrimaryScreen;
            if (primary != null)
            {
                return primary;
            }

            var screens = Screen.AllScreens;
            if (screens.Length > 0)
            {
                return screens[0];
            }

            throw new InvalidOperationException("No display devices detected.");
        }

        public void setImage(Bitmap image)
        {
            ArgumentNullException.ThrowIfNull(image);

            this.BackgroundImage = image;
            this.Width = image.Width;
            this.Height = image.Height;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            this.isCtrlDown = e.Control;

            base.OnKeyDown(e);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Escape)
                this.Hide();

            base.OnKeyPress(e);
        }
        protected override void OnKeyUp(KeyEventArgs e)
        {
            this.isCtrlDown = e.Control;

            base.OnKeyUp(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (TopLevelControl is not Form parentForm)
            {
                base.OnMouseDown(e);
                return;
            }

            isMouseDown = true;
            mouseHit = e.Location;
            formPosition = parentForm.Location;

            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            isMouseDown = false;

            base.OnMouseUp(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (isMouseDown)
            {
                int dx = e.Location.X - mouseHit.X;
                int dy = e.Location.Y - mouseHit.Y;
                if (TopLevelControl is Form parentForm)
                {
                    Point newLocation = new Point(formPosition.X + dx, formPosition.Y + dy);
                    parentForm.Location = newLocation;
                    formPosition = newLocation;
                }
                else
                {
                    isMouseDown = false;
                }
            }

            base.OnMouseMove(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (BackgroundImage == null)
            {
                base.OnMouseWheel(e);
                return;
            }

            if (this.isCtrlDown)
            {
                var delta = e.Delta > 0 ? 0.1m : -0.1m;
                var minZoom = zoomLevels[0];
                var maxZoom = zoomLevels[^1];
                setZoom(Math.Clamp(zoomLevel + delta, minZoom, maxZoom));
            }
            else
            {
                var targetZoom = TryGetStepZoom(e.Delta > 0);
                if (targetZoom.HasValue)
                {
                    setZoom(targetZoom.Value);
                }
            }

            base.OnMouseWheel(e);
        }

        private decimal? TryGetStepZoom(bool zoomIn)
        {
            if (zoomIn)
            {
                foreach (var level in zoomLevels)
                {
                    if (level > zoomLevel)
                    {
                        return level;
                    }
                }

                return zoomLevels[^1];
            }

            for (int i = zoomLevels.Length - 1; i >= 0; i--)
            {
                if (zoomLevels[i] < zoomLevel)
                {
                    return zoomLevels[i];
                }
            }

            return zoomLevels[0];
        }

        private void setZoom(decimal zoomLevel)
        {
            if (this.BackgroundImage == null)
            {
                return;
            }

            zoomLevel = Math.Clamp(zoomLevel, zoomLevels[0], zoomLevels[^1]);
            Point oldCenter = new Point(this.Left + this.Width / 2, this.Top + this.Height / 2);
            this.Width = (int)(this.BackgroundImage.Width * zoomLevel);
            this.Height = (int)(this.BackgroundImage.Height * zoomLevel);
            this.Left = oldCenter.X - this.Width / 2;
            this.Top = oldCenter.Y - this.Height / 2;
            this.zoomLevel = zoomLevel;
        }

    }
}
