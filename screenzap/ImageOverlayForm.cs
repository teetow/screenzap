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
        private decimal[] zoomLevels = { 0.1m, 0.25m, 1 / 3m, 0.5m, 2 / 3m, 0.75m, 1.0m, 1.25m, 4 / 3m, 1.5m, 5 / 3m, 2m, 3m, 4m, 6m, 8m, 10m, 15m, 20m, 30m, 40m, };

        public ImageOverlayForm()
        {
            this.Cursor = Cursors.Cross;
            this.TopMost = true;
            //this.ShowInTaskbar = false;
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Color.Gray;
            this.TransparencyKey = Color.Red;
            this.Opacity = 0.5;
            this.Width = Screen.PrimaryScreen.WorkingArea.Width;
            this.Height = Screen.PrimaryScreen.WorkingArea.Height;
            //this.WindowState = FormWindowState.Maximized;
            this.DoubleBuffered = true;
            this.BackgroundImageLayout = ImageLayout.Stretch;
        }

        public void setImage(Bitmap image)
        {
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
            isMouseDown = true;
            mouseHit = e.Location;
            formPosition = ((Form)TopLevelControl).Location;

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
                Point newLocation = new Point(formPosition.X + dx, formPosition.Y + dy);
                ((Form)TopLevelControl).Location = newLocation;
                formPosition = newLocation;
            }

            base.OnMouseMove(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            try
            {
                if (this.isCtrlDown)
                {
                    if (e.Delta > 0)
                        setZoom(zoomLevel + 0.1m);
                    else
                        setZoom(zoomLevel - 0.1m);
                }
                else
                {
                    if (e.Delta > 0) // pos, zoom in
                        setZoom(zoomLevels.Where(x => x > this.zoomLevel).First());
                    else
                        setZoom(zoomLevels.Where(x => x < this.zoomLevel).Last());
                }
            }
            catch (Exception zoomException)
            {
                Console.WriteLine("zoomException");
            }

            base.OnMouseWheel(e);
        }

        private void setZoom(decimal zoomLevel)
        {
            Point oldCenter = new Point(this.Left + this.Width / 2, this.Top + this.Height / 2);
            this.Width = (int)(this.BackgroundImage.Width * zoomLevel);
            this.Height = (int)(this.BackgroundImage.Height * zoomLevel);
            this.Left = oldCenter.X - this.Width / 2;
            this.Top = oldCenter.Y - this.Height / 2;
            this.zoomLevel = zoomLevel;
        }

    }
}
