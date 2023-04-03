using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Media;
using System.Reflection;
using System.Windows.Forms;

namespace screenzap
{
    public partial class Screenzap : Form
    {
        KeyboardHook rectCaptureHook = new KeyboardHook();
        KeyboardHook seqCaptureHook = new KeyboardHook();
        string autostartAppName = "Screenzap";
        string assemblyLocation = Assembly.GetExecutingAssembly().Location;  // Or the EXE path.
        KeyCombo rectCaptureCombo;
        KeyCombo seqCaptureCombo;
        bool isCapturing = false;
        ImageEditor ImageEditor;

        public Screenzap()
        {
            InitializeComponent();
            rectCaptureCombo = new KeyCombo(Properties.Settings.Default.currentCombo);
            seqCaptureCombo = new KeyCombo(Properties.Settings.Default.seqCaptureCombo);
            startWhenLoggedInToolStripMenuItem.Checked = Util.IsAutoStartEnabled(autostartAppName, assemblyLocation);
            showBalloonMenuItem.Checked = Properties.Settings.Default.showBalloon;


            updateTooltips(rectCaptureCombo);
            if (Properties.Settings.Default.showBalloon == true)
            {
                notifyIcon1.ShowBalloonTip(2000, "Screenzap is running!", $"Press {rectCaptureCombo} to take a screenshot.", ToolTipIcon.Info);
            }

            rectCaptureHook.KeyPressed += new EventHandler<KeyPressedEventArgs>(DoCapture);
            try
            {
                rectCaptureHook.RegisterHotKey(rectCaptureCombo.getModifierKeys(), rectCaptureCombo.Key);
            }
            catch
            {
                MessageBox.Show("Can't register the windowed capture hotkey. Please pick a better one.");
            }

            seqCaptureHook.KeyPressed += new EventHandler<KeyPressedEventArgs>(DoInstantCapture);
            try
            {
                seqCaptureHook.RegisterHotKey(seqCaptureCombo.getModifierKeys(), seqCaptureCombo.Key);
            }
            catch
            {
                MessageBox.Show("Can't register the instant capture hotkey. Please pick a better one.");
            }

            ImageEditor = new ImageEditor();

        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Hide();
            Opacity = 0;
            ShowInTaskbar = false;
        }

        void updateTooltips(KeyCombo keyCombo)
        {
            notifyIcon1.Text = $"Screenzap is running! \n\nPress {keyCombo}.";
        }


        void setClipboard(Bitmap bitmap)
        {
            using (MemoryStream pngMemStream = new MemoryStream())
            {
                DataObject data = new DataObject();

                data.SetData(DataFormats.Bitmap, true, bitmap);

                bitmap.Save(pngMemStream, ImageFormat.Png);
                data.SetData("PNG", false, pngMemStream);

                Clipboard.SetDataObject(data, true);
            }
        }

        void DoCapture(object sender, KeyPressedEventArgs e)
        {
            if (isCapturing) return;
            isCapturing = true;
            try
            {
                Overlay ovl = new Overlay();
                var captureRect = ovl.CaptureRect();

                if (captureRect.Width <= 0 || captureRect.Height <= 0)
                {
                    throw new Exception($"Invalid capture area {captureRect.Width}x{captureRect.Height}");
                }

                Bitmap bmpScreenshot = new Bitmap(captureRect.Width, captureRect.Height, PixelFormat.Format32bppArgb);
                Graphics gfxScreenshot = Graphics.FromImage(bmpScreenshot);

                gfxScreenshot.CopyFromScreen(captureRect.Location, new Point(0, 0), captureRect.Size, CopyPixelOperation.SourceCopy);

                setClipboard(bmpScreenshot);

                SoundPlayer audio = new SoundPlayer(Properties.Resources.zap);
                audio.Play();

            }
            catch (Exception ex)
            {
                Console.Write(ex.ToString());
            }

            isCapturing = false;
        }

        void DoInstantCapture(object sender, KeyPressedEventArgs e)
        {
            if (isCapturing) return;
            isCapturing = true;
            try
            {
                var captureAreaLeft = 0;
                var captureAreaTop = 0;
                var captureAreaWidth = Screen.PrimaryScreen.Bounds.Width;
                var captureAreaHeight = Screen.PrimaryScreen.Bounds.Height;
                var captureRect = new Rectangle(captureAreaLeft, captureAreaTop, captureAreaWidth, captureAreaHeight);

                if (captureRect.Width <= 0 || captureRect.Height <= 0)
                {
                    throw new Exception($"Invalid capture area {captureRect.Width}x{captureRect.Height}");
                }

                Bitmap bmpScreenshot = new Bitmap(captureRect.Width, captureRect.Height, PixelFormat.Format32bppArgb);
                Graphics gfxScreenshot = Graphics.FromImage(bmpScreenshot);

                gfxScreenshot.CopyFromScreen(captureRect.Location, new Point(0, 0), captureRect.Size, CopyPixelOperation.SourceCopy);

                var dateStr = DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ss") + (".png");
                var userPath = Environment.ExpandEnvironmentVariables(Properties.Settings.Default.captureFolder);
                var filePath = Path.Combine(userPath, dateStr);

                using (FileStream pngFileStream = new FileStream(filePath, FileMode.Create))
                {
                    bmpScreenshot.Save(pngFileStream, ImageFormat.Png);
                }

                setClipboard(bmpScreenshot);

                SoundPlayer audio = new SoundPlayer(Properties.Resources.zap);
                audio.Play();
            }
            catch (Exception ex)
            {
                Console.Write(ex.ToString());
            }

            isCapturing = false;
        }

        private void sanitizeClipboardToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Image imgData = Clipboard.GetImage();

            if (imgData == null)
                return;

            ImageEditor.LoadImage(imgData);
            ImageEditor.ShowDialog();
        }

        private void startWhenLoggedInToolStripMenuItem_CheckStateChanged(object sender, EventArgs e)
        {
            if (startWhenLoggedInToolStripMenuItem.Checked)
                Util.SetAutoStart(autostartAppName, assemblyLocation);
            else
            {
                if (Util.IsAutoStartEnabled(autostartAppName, assemblyLocation))
                    Util.UnSetAutoStart(autostartAppName);
            }
        }

        private void quitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void setKeyboardShortcutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShortcutEditor shortcutEditor = new ShortcutEditor(rectCaptureCombo);
            var rslt = shortcutEditor.ShowDialog();
            if (rslt == DialogResult.OK)
            {
                rectCaptureCombo = shortcutEditor.currentCombo;
                try
                {
                    rectCaptureHook.UnregisterHotkey(1);
                    rectCaptureHook.RegisterHotKey(rectCaptureCombo.getModifierKeys(), rectCaptureCombo.Key);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                    return;
                }

                updateTooltips(rectCaptureCombo);
                Properties.Settings.Default.currentCombo = rectCaptureCombo.ToString();
                Properties.Settings.Default.Save();
            }
        }

        private void showBalloonMenuItem_Click(object sender, EventArgs e)
        {
            showBalloonMenuItem.Checked = !showBalloonMenuItem.Checked;
            Properties.Settings.Default.showBalloon = showBalloonMenuItem.Checked;
            Properties.Settings.Default.Save();
        }

        private void setFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var b = new FolderBrowserDialog();
            if (b.ShowDialog() == DialogResult.OK)
            {
                Properties.Settings.Default.captureFolder = b.SelectedPath;
                Properties.Settings.Default.Save();
            }
        }

        private void notifyIcon1_DoubleClick(object sender, EventArgs e)
        {
            if (ImageEditor == null || ImageEditor.IsDisposed)
            {
                ImageEditor = new ImageEditor();
            }
            Image imgData = Clipboard.GetImage();
            ImageEditor.LoadImage(imgData);
            ImageEditor.Show();
        }
    }
}
