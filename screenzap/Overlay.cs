using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace screenzap
{
    class OverlayForm : Form
    {
        public OverlayForm()
        {
            this.Cursor = Cursors.Cross;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Color.Gray;
            this.TransparencyKey = Color.Red;
            this.Opacity = 0.5;
            this.Width = Screen.PrimaryScreen.WorkingArea.Width;
            this.Height = Screen.PrimaryScreen.WorkingArea.Height;
            this.WindowState = FormWindowState.Maximized;
            this.DoubleBuffered = true;
        }
    }
    class Overlay
    {
        private Form form;
        private Point start;
        private Point end;

        private int left
        {
            get { return Math.Min(start.X, end.X); }
        }

        private int top
        {
            get { return Math.Min(start.Y, end.Y); }
        }

        private int width
        {
            get { return Math.Abs(end.X - start.X); }
        }

        private int height
        {
            get { return Math.Abs(end.Y - start.Y); }
        }

        private Rectangle captureArea
        {
            get
            {
                var captureAreaLeft = Math.Max(left, 0);
                var captureAreaTop = Math.Max(top, 0);
                var captureAreaWidth = Math.Min(width, Screen.PrimaryScreen.Bounds.Width - left);
                var captureAreaHeight = Math.Min(height, Screen.PrimaryScreen.Bounds.Height - top);
                return new Rectangle(captureAreaLeft, captureAreaTop, captureAreaWidth, captureAreaHeight);
            }
        }

        private bool doPan = false;
        private bool doSquare = false;
        private bool doCenter = false;

        public Rectangle CaptureRect()
        {
            var rslt = form.ShowDialog();
            if (rslt == DialogResult.OK)
                return captureArea;
            return Rectangle.Empty;
        }

        public Overlay()
        {
            form = new OverlayForm();
            form.MouseDown += Form_MouseDown;
            form.MouseMove += Form_MouseMove;
            form.MouseUp += Form_MouseUp;
            form.KeyDown += Form_KeyDown;
            form.KeyUp += Form_KeyUp;
            form.Paint += Form_Paint;
        }

        private void drawRect(PaintEventArgs e, Brush brush, Rectangle drawArea)
        {
            e.Graphics.FillRectangle(brush, drawArea.Left, drawArea.Top, drawArea.Width, drawArea.Height);
            e.Graphics.DrawRectangle(Pens.White, drawArea.Left - 1, drawArea.Top - 1, drawArea.Width + 2, drawArea.Height + 2);
            var coords = $"{drawArea.Width} x {drawArea.Height}";
            var textPos = new PointF(drawArea.Right - e.Graphics.MeasureString(coords, SystemFonts.CaptionFont).Width, drawArea.Bottom + 4);
            e.Graphics.DrawString(coords, SystemFonts.CaptionFont, Brushes.White, textPos);
        }

        private void Form_Paint(object sender, PaintEventArgs e)
        {
            drawRect(e, Brushes.Red, captureArea);
        }

        private void Form_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Modifiers.HasFlag(Keys.Shift))
                doSquare = true;

            if (e.KeyCode == Keys.Space)
            {
                doPan = true;
                if (Cursor.Position != end)
                    Cursor.Position = end;
            }

            if (e.Modifiers.HasFlag(Keys.Alt))
            {
                doCenter = true;
                if (Cursor.Position != end)
                    Cursor.Position = end;
            }

            if (e.KeyCode == Keys.Escape)
            {
                form.DialogResult = DialogResult.Cancel;
                form.Close();
            }
        }

        private void Form_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
                doPan = false;

            if (!e.Modifiers.HasFlag(Keys.Alt))
                doCenter = false;

            if (!e.Modifiers.HasFlag(Keys.Shift))
                doSquare = false;
        }

        private Point getDiff(Point oldEnd, Point end)
        {
            return new Point(oldEnd.X - end.X, oldEnd.Y - end.Y);
        }

        private void Form_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button.HasFlag(MouseButtons.Left))
            {
                var oldEnd = end;
                end = new Point(e.X, e.Y);
                var diff = getDiff(oldEnd, end);

                if (doPan)
                {
                    start = new Point(start.X - diff.X, start.Y - diff.Y);
                }
                else
                {
                    if (doCenter)
                    {
                        start = new Point(start.X + diff.X, start.Y + diff.Y);
                    }
                    if (doSquare)
                    {
                        var squared = Math.Max(width, height);
                        var polX = (end.X > start.X) ? 1 : -1;
                        var polY = (end.Y > start.Y) ? 1 : -1;
                        end.X = start.X + squared * polX;
                        end.Y = start.Y + squared * polY;
                    }
                }

            }
            form.Invalidate();
        }

        private void Form_MouseDown(object sender, MouseEventArgs e)
        {
            start = new Point(e.X, e.Y);
            //Cursor.Hide();
        }

        private void Form_MouseUp(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            Cursor.Show();
            form.DialogResult = DialogResult.OK;
            form.Close();
        }
    }
}
