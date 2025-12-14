using System;
using System.Drawing;
using System.Windows.Forms;

namespace screenzap
{
    class OverlayForm : Form
    {
        public OverlayForm(Rectangle bounds)
        {
            this.Cursor = Cursors.Cross;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Color.Black;
            this.StartPosition = FormStartPosition.Manual;
            this.Bounds = bounds;
            this.ClientSize = new Size(bounds.Width, bounds.Height);
            this.Opacity = 1.0;
            this.WindowState = FormWindowState.Normal;
            this.DoubleBuffered = true;
            this.KeyPreview = true;
        }
    }
    class Overlay
    {
        private readonly Form form;
        private readonly Bitmap background;
        private readonly Size canvasSize;
        private readonly Font captionFont;
        private Point start;
        private Point end;
        private bool isDragging;

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
                var captureAreaWidth = Math.Min(width, canvasSize.Width - captureAreaLeft);
                var captureAreaHeight = Math.Min(height, canvasSize.Height - captureAreaTop);
                captureAreaWidth = Math.Max(0, captureAreaWidth);
                captureAreaHeight = Math.Max(0, captureAreaHeight);
                return new Rectangle(captureAreaLeft, captureAreaTop, captureAreaWidth, captureAreaHeight);
            }
        }

        private bool doPan = false;
        private bool doSquare = false;
        private bool doCenter = false;
        private bool doSnapToGrid = false;

        public Rectangle CaptureRect()
        {
            try
            {
                var rslt = form.ShowDialog();
                if (rslt == DialogResult.OK)
                    return captureArea;
                return Rectangle.Empty;
            }
            finally
            {
                form.Dispose();
            }
        }

        public Overlay(Screen screen, Bitmap frozenBackground)
        {
            background = frozenBackground ?? throw new ArgumentNullException(nameof(frozenBackground));
            form = new OverlayForm(screen.Bounds);
            canvasSize = frozenBackground.Size;
            captionFont = SystemFonts.CaptionFont ?? SystemFonts.DefaultFont;
            form.MouseDown += Form_MouseDown;
            form.MouseMove += Form_MouseMove;
            form.MouseUp += Form_MouseUp;
            form.KeyDown += Form_KeyDown;
            form.KeyUp += Form_KeyUp;
            form.Paint += Form_Paint;
        }

        private void drawRect(PaintEventArgs e, Rectangle drawArea)
        {
            using var borderPen = new Pen(Color.DeepSkyBlue, 2f);
            e.Graphics.DrawRectangle(borderPen, drawArea.Left - 1, drawArea.Top - 1, drawArea.Width + 2, drawArea.Height + 2);
            var coords = $"{drawArea.Width} x {drawArea.Height}";
            var textSize = e.Graphics.MeasureString(coords, captionFont);
            var textPos = new PointF(drawArea.Right - textSize.Width, drawArea.Bottom + 4);
            e.Graphics.DrawString(coords, captionFont, Brushes.White, textPos);
        }

        private void Form_Paint(object? sender, PaintEventArgs e)
        {
            e.Graphics.DrawImageUnscaled(background, Point.Empty);

            var fullRect = new Rectangle(Point.Empty, canvasSize);
            using (var dimBrush = new SolidBrush(Color.FromArgb(120, Color.Black)))
            {
                if (captureArea.Width > 0 && captureArea.Height > 0)
                {
                    using Region dimRegion = new Region(fullRect);
                    dimRegion.Exclude(captureArea);
                    e.Graphics.FillRegion(dimBrush, dimRegion);
                }
                else
                {
                    e.Graphics.FillRectangle(dimBrush, fullRect);
                }
            }

            if (captureArea.Width > 0 && captureArea.Height > 0)
            {
                drawRect(e, captureArea);
            }
        }

        private void Form_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Modifiers.HasFlag(Keys.Shift))
                doSquare = true;

            if (e.KeyCode == Keys.Space)
            {
                doPan = true;
                if (isDragging)
                {
                    var screenPoint = form.PointToScreen(end);
                    if (Cursor.Position != screenPoint)
                        Cursor.Position = screenPoint;
                }
            }

            if (e.Modifiers.HasFlag(Keys.Alt))
            {
                doCenter = true;
                if (isDragging)
                {
                    var screenPoint = form.PointToScreen(end);
                    if (Cursor.Position != screenPoint)
                        Cursor.Position = screenPoint;
                }
            }

            if (e.KeyCode == Keys.Escape)
            {
                form.DialogResult = DialogResult.Cancel;
                form.Close();
            }

            if (e.KeyCode == Keys.ControlKey)
            {
                doSnapToGrid = true;

            }
        }

        private void Form_KeyUp(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
                doPan = false;

            if (!e.Modifiers.HasFlag(Keys.Alt))
                doCenter = false;

            if (!e.Modifiers.HasFlag(Keys.Shift))
                doSquare = false;

            if (!e.Modifiers.HasFlag(Keys.Control))
            {
                doSnapToGrid = false;
            }
        }

        private static Point getDiff(Point oldEnd, Point end)
        {
            return new Point(oldEnd.X - end.X, oldEnd.Y - end.Y);
        }

        private void Form_MouseMove(object? sender, MouseEventArgs e)
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

                if (doSnapToGrid)
                {
                    var grid = 16;
                    var delta = new Point(end.X - start.X, end.Y - start.Y);

                    delta.X = (int)Math.Round((double)delta.X / grid) * grid;
                    delta.Y = (int)Math.Round((double)delta.Y / grid) * grid;
                
                    end.X = start.X + delta.X;
                    end.Y = start.Y + delta.Y;
                }
            }
            form.Invalidate();
        }

        private void Form_MouseDown(object? sender, MouseEventArgs e)
        {
            start = new Point(e.X, e.Y);
            end = start;
            isDragging = true;
            //Cursor.Hide();
        }

        private void Form_MouseUp(object? sender, MouseEventArgs e)
        {
            Cursor.Show();
            isDragging = false;
            form.DialogResult = DialogResult.OK;
            form.Close();
        }
    }
}
