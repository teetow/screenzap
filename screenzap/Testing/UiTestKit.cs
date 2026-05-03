using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using screenzap.Components;

namespace screenzap.Testing
{
    /// <summary>
    /// High-level driver for end-to-end UI testing. Wraps an ImageEditor (and optionally a
    /// ClipboardEditorHostForm) and exposes user-input primitives that fire real WinForms event
    /// args through the actual handlers — no diagnostic shortcuts. Capable of rendering the
    /// editor to a bitmap so tests can inspect what the user would actually see.
    ///
    /// Threading: must be created and used on an STA thread (WinForms requirement).
    ///
    /// Visibility: by default the form is created (CreateControl) but not Show()n — DrawToBitmap
    /// renders correctly without a visible window. Pass <see cref="Visible"/> to make it visible
    /// when a human wants to watch interactions live.
    /// </summary>
    internal sealed class UiTestKit : IDisposable
    {
        public ImageEditor Editor { get; }
        public ClipboardEditorHostForm? Host { get; }

        private readonly bool ownsHost;
        private bool disposed;

        public UiTestKit(Size editorSize, bool withHost = true, bool visible = false)
        {
            Editor = new ImageEditor();
            Editor.TestSetSize(editorSize.Width, editorSize.Height);

            if (withHost)
            {
                Host = new ClipboardEditorHostForm(Editor)
                {
                    SuppressActivation = true,
                    ShowInTaskbar = false,
                    ClientSize = editorSize,
                    StartPosition = FormStartPosition.Manual,
                    Location = visible ? new Point(40, 40) : new Point(-32000, -32000),
                };
                Host.Show();
                ownsHost = true;
            }
            else
            {
                Editor.ShowInTaskbar = false;
                Editor.StartPosition = FormStartPosition.Manual;
                Editor.Location = visible ? new Point(40, 40) : new Point(-32000, -32000);
                Editor.Show();
                ownsHost = false;
            }

            PumpUi();
            // Force the picturebox to a real layout/paint by invalidating + pumping again.
            Editor.PerformLayout();
            Editor.Refresh();
            PumpUi();
        }

        // ─── canvas setup ──────────────────────────────────────────────────────────────

        public ClipboardHistoryItem LoadCanvas(int width, int height, Color fill)
        {
            var bmp = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(fill);
            }

            ClipboardHistoryItem item;
            if (Host != null)
            {
                item = Host.HistoryStore.AddObservedImage(bmp);
                Host.ActivateHistoryItem(item);
            }
            else
            {
                Editor.LoadImage(bmp);
                item = ClipboardHistoryItem.FromImage(bmp);
            }
            bmp.Dispose();
            PumpUi();
            return item;
        }

        // ─── input primitives ──────────────────────────────────────────────────────────

        public void Click(Point imagePixel, MouseButtons button = MouseButtons.Left)
        {
            Editor.TestFireMouseDownAtImagePixel(imagePixel, button);
            PumpUi();
            Editor.TestFireMouseUpAtImagePixel(imagePixel, button);
            PumpUi();
        }

        public void DoubleClick(Point imagePixel, MouseButtons button = MouseButtons.Left)
        {
            Editor.TestFireDoubleClickAtImagePixel(imagePixel, button);
            PumpUi();
        }

        public void Drag(Point fromImagePixel, Point toImagePixel, MouseButtons button = MouseButtons.Left, int steps = 8)
        {
            Editor.TestFireMouseDownAtImagePixel(fromImagePixel, button);
            PumpUi();

            steps = Math.Max(1, steps);
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                int ix = (int)Math.Round(fromImagePixel.X + (toImagePixel.X - fromImagePixel.X) * t);
                int iy = (int)Math.Round(fromImagePixel.Y + (toImagePixel.Y - fromImagePixel.Y) * t);
                Editor.TestFireMouseMoveAtImagePixel(new Point(ix, iy), button);
                PumpUi();
            }

            Editor.TestFireMouseUpAtImagePixel(toImagePixel, button);
            PumpUi();
        }

        public void DragInClient(Point fromClient, Point toClient, MouseButtons button = MouseButtons.Left, int steps = 8)
        {
            Editor.TestFireMouseDownAtClientPoint(fromClient, button);
            PumpUi();

            steps = Math.Max(1, steps);
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                int x = (int)Math.Round(fromClient.X + (toClient.X - fromClient.X) * t);
                int y = (int)Math.Round(fromClient.Y + (toClient.Y - fromClient.Y) * t);
                Editor.TestFireMouseMoveAtClientPoint(new Point(x, y), button);
                PumpUi();
            }

            Editor.TestFireMouseUpAtClientPoint(toClient, button);
            PumpUi();
        }

        public bool Press(Keys keyData)
        {
            // Try ProcessCmdKey first (handles Ctrl+Z, Ctrl+V, etc.). Fall back to KeyDown event.
            bool handled = Editor.TestFireProcessCmdKey(keyData);
            if (!handled)
            {
                Editor.TestFireKeyDown(keyData);
            }
            PumpUi();
            return handled;
        }

        public void PasteImage(Bitmap source)
        {
            Editor.SetInternalClipboardImageForDiagnostics(source);
            // Fire Ctrl+V through the real key pipeline.
            Press(Keys.Control | Keys.V);
        }

        // ─── rendering / sampling ──────────────────────────────────────────────────────

        public Bitmap CaptureForm()
        {
            return Editor.TestRenderToBitmap();
        }

        public Bitmap CapturePictureBox()
        {
            return Editor.TestRenderPictureBoxToBitmap();
        }

        public string SaveScreenshot(string label)
        {
            var dir = Path.Combine(Path.GetTempPath(), "screenzap-uitests");
            Directory.CreateDirectory(dir);
            var safe = string.Concat(label.Split(Path.GetInvalidFileNameChars()));
            var ts = DateTime.Now.ToString("HHmmss-fff");
            var path = Path.Combine(dir, $"{ts}-{safe}.png");
            using (var bmp = CaptureForm())
            {
                bmp.Save(path, ImageFormat.Png);
            }
            return path;
        }

        public Color SamplePixelInForm(Point formPoint)
        {
            using var bmp = CaptureForm();
            if (formPoint.X < 0 || formPoint.Y < 0 || formPoint.X >= bmp.Width || formPoint.Y >= bmp.Height)
            {
                return Color.Transparent;
            }
            return bmp.GetPixel(formPoint.X, formPoint.Y);
        }

        public Color SamplePixelAtImage(Point imagePixel)
        {
            var pbBounds = Editor.TestPictureBoxBoundsInForm();
            var clientPoint = Editor.TestImagePixelToClient(imagePixel);
            var formPoint = new Point(pbBounds.X + clientPoint.X, pbBounds.Y + clientPoint.Y);
            return SamplePixelInForm(formPoint);
        }

        // ─── lifecycle ─────────────────────────────────────────────────────────────────

        public void PumpUi()
        {
            // Drain the message queue so paints, BeginInvokes, and follow-up work complete.
            for (int i = 0; i < 4; i++)
            {
                Application.DoEvents();
                Thread.Sleep(0);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { if (ownsHost) Host?.Dispose(); else Editor.Dispose(); }
            catch { /* best-effort */ }
        }
    }
}
