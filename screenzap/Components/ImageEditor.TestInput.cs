using System.Drawing;
using System.Windows.Forms;

namespace screenzap
{
    public partial class ImageEditor
    {
        // Real input simulation for tests. These methods construct real WinForms event-args and
        // fire them through the actual editor pipeline (pictureBox1_MouseDown / _MouseMove /
        // _MouseUp, ProcessCmdKey, ImageEditor_KeyDown). Tests using these helpers exercise the
        // exact code path that user input takes — no diagnostic shortcuts.

        internal void TestFireMouseDownAtImagePixel(Point imagePixel, MouseButtons button)
        {
            var clientPoint = pictureBox1?.PixelToClient(imagePixel) ?? imagePixel;
            var args = new MouseEventArgs(button, 1, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseDown(pictureBox1!, args);
        }

        internal void TestFireMouseMoveAtImagePixel(Point imagePixel, MouseButtons heldButton)
        {
            var clientPoint = pictureBox1?.PixelToClient(imagePixel) ?? imagePixel;
            var args = new MouseEventArgs(heldButton, 0, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseMove(pictureBox1!, args);
        }

        internal void TestFireMouseUpAtImagePixel(Point imagePixel, MouseButtons button)
        {
            var clientPoint = pictureBox1?.PixelToClient(imagePixel) ?? imagePixel;
            var args = new MouseEventArgs(button, 1, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseUp(pictureBox1!, args);
        }

        internal void TestFireMouseDownAtClientPoint(Point clientPoint, MouseButtons button)
        {
            var args = new MouseEventArgs(button, 1, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseDown(pictureBox1!, args);
        }

        internal void TestFireMouseMoveAtClientPoint(Point clientPoint, MouseButtons heldButton)
        {
            var args = new MouseEventArgs(heldButton, 0, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseMove(pictureBox1!, args);
        }

        internal void TestFireMouseUpAtClientPoint(Point clientPoint, MouseButtons button)
        {
            var args = new MouseEventArgs(button, 1, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseUp(pictureBox1!, args);
        }

        internal void TestFireDoubleClickAtImagePixel(Point imagePixel, MouseButtons button)
        {
            var clientPoint = pictureBox1?.PixelToClient(imagePixel) ?? imagePixel;
            // Double-click in WinForms is signalled by Clicks=2 on the second MouseDown.
            var down1 = new MouseEventArgs(button, 1, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseDown(pictureBox1!, down1);
            var up1 = new MouseEventArgs(button, 1, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseUp(pictureBox1!, up1);
            var down2 = new MouseEventArgs(button, 2, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseDown(pictureBox1!, down2);
            // Fire the picturebox DoubleClick event, which any handler hooked to it depends on.
            pictureBox1?.GetType()
                .GetMethod("OnDoubleClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(pictureBox1, new object[] { System.EventArgs.Empty });
            var up2 = new MouseEventArgs(button, 2, clientPoint.X, clientPoint.Y, 0);
            pictureBox1_MouseUp(pictureBox1!, up2);
        }

        internal bool TestFireProcessCmdKey(Keys keyData)
        {
            var msg = new Message();
            return ProcessCmdKey(ref msg, keyData);
        }

        internal void TestFireKeyDown(Keys keyData)
        {
            var args = new KeyEventArgs(keyData);
            ImageEditor_KeyDown(this, args);
        }

        internal Point TestImagePixelToClient(Point imagePixel)
        {
            return pictureBox1?.PixelToClient(imagePixel) ?? imagePixel;
        }

        internal Point TestClientToImagePixel(Point clientPoint)
        {
            return pictureBox1?.ClientToPixel(clientPoint) ?? clientPoint;
        }

        internal Bitmap TestRenderToBitmap()
        {
            // Render the entire editor form to a bitmap. Forces a full paint pass.
            if (Width <= 0 || Height <= 0)
            {
                return new Bitmap(1, 1);
            }
            var bmp = new Bitmap(Width, Height);
            DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
            return bmp;
        }

        internal Bitmap TestRenderPictureBoxToBitmap()
        {
            if (pictureBox1 == null || pictureBox1.Width <= 0 || pictureBox1.Height <= 0)
            {
                return new Bitmap(1, 1);
            }
            var bmp = new Bitmap(pictureBox1.Width, pictureBox1.Height);
            pictureBox1.DrawToBitmap(bmp, new Rectangle(0, 0, pictureBox1.Width, pictureBox1.Height));
            return bmp;
        }

        internal Rectangle TestPictureBoxBoundsInForm()
        {
            if (pictureBox1 == null) return Rectangle.Empty;
            var topLeft = pictureBox1.PointToScreen(Point.Empty);
            var formTopLeft = PointToScreen(Point.Empty);
            return new Rectangle(
                topLeft.X - formTopLeft.X,
                topLeft.Y - formTopLeft.Y,
                pictureBox1.Width,
                pictureBox1.Height);
        }

        internal void TestSetSize(int width, int height)
        {
            ClientSize = new Size(width, height);
        }

        internal string TestDescribeState()
        {
            var pb = pictureBox1;
            var imgInfo = pb?.Image == null ? "null" : $"{pb.Image.Width}x{pb.Image.Height}";
            var pbBounds = pb == null ? "null" : $"{pb.Width}x{pb.Height} visible={pb.Visible}";
            var formInfo = $"{Width}x{Height} visible={Visible} created={IsHandleCreated}";
            var zoom = pb == null ? "n/a" : $"zoom={pb.ZoomLevel} pan={pb.Metrics.PanOffset}";
            var layerInfo = "[]";
            if (imageLayers.Count > 0)
            {
                var sb = new System.Text.StringBuilder("[");
                for (int i = 0; i < imageLayers.Count; i++)
                {
                    var l = imageLayers[i];
                    if (i > 0) sb.Append(", ");
                    sb.Append($"#{i} src={l.Source.Width}x{l.Source.Height} frame={l.Frame} fill={l.Fill}");
                }
                sb.Append("]");
                layerInfo = sb.ToString();
            }
            return $"form={formInfo} picturebox={pbBounds} image={imgInfo} {zoom} layers={imageLayers.Count} selected={selectedLayerIndex} {layerInfo}";
        }
    }
}
