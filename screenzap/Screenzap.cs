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

        public Screenzap()
        {
            InitializeComponent();
            this.rectCaptureCombo = new KeyCombo(Properties.Settings.Default.currentCombo);
            this.seqCaptureCombo = new KeyCombo(Properties.Settings.Default.seqCaptureCombo);
            this.startWhenLoggedInToolStripMenuItem.Checked = Util.IsAutoStartEnabled(autostartAppName, assemblyLocation);
            this.showBalloonMenuItem.Checked = Properties.Settings.Default.showBalloon;


            updateTooltips(rectCaptureCombo);
            if (Properties.Settings.Default.showBalloon == true)
            {
                this.notifyIcon1.ShowBalloonTip(2000, "Screenzap is running!", $"Press {rectCaptureCombo} to take a screenshot.", ToolTipIcon.Info);
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
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            this.Hide();
            this.Opacity = 0;
            this.ShowInTaskbar = false;
        }

        void updateTooltips(KeyCombo keyCombo)
        {
            this.notifyIcon1.Text = $"Screenzap is running! \n\nPress {keyCombo}.";
        }

        void DoCapture(object sender, KeyPressedEventArgs e)
        {
            if (this.isCapturing) return;
            this.isCapturing = true;
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

                using (MemoryStream pngMemStream = new MemoryStream())
                {
                    DataObject data = new DataObject();

                    data.SetData(DataFormats.Bitmap, true, bmpScreenshot);

                    bmpScreenshot.Save(pngMemStream, ImageFormat.Png);
                    data.SetData("PNG", false, pngMemStream);

                    Clipboard.SetDataObject(data, true);

                    Console.Write("ayaya");

                    SoundPlayer audio = new SoundPlayer(Properties.Resources.zap);
                    audio.Play();
                }

            }
            catch (Exception ex)
            {
                Console.Write(ex.ToString());
            }

            this.isCapturing = false;
        }

        void DoInstantCapture(object sender, KeyPressedEventArgs e)
        {
            if (this.isCapturing) return;
            this.isCapturing = true;
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


                using (MemoryStream pngMemStream = new MemoryStream())
                {
                    DataObject data = new DataObject();

                    data.SetData(DataFormats.Bitmap, true, bmpScreenshot);

                    bmpScreenshot.Save(pngMemStream, ImageFormat.Png);
                    data.SetData("PNG", false, pngMemStream);

                    Clipboard.SetDataObject(data, true);

                    Console.Write("ayaya");

                    SoundPlayer audio = new SoundPlayer(Properties.Resources.zap);
                    audio.Play();
                }

            }
            catch (Exception ex)
            {
                Console.Write(ex.ToString());
            }

            this.isCapturing = false;
        }

        private void startWhenLoggedInToolStripMenuItem_CheckStateChanged(object sender, EventArgs e)
        {
            if (this.startWhenLoggedInToolStripMenuItem.Checked)
                Util.SetAutoStart(autostartAppName, assemblyLocation);
            else
            {
                if (Util.IsAutoStartEnabled(autostartAppName, assemblyLocation))
                    Util.UnSetAutoStart(autostartAppName);
            }
        }

        private void quitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
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
                screenzap.Properties.Settings.Default.currentCombo = rectCaptureCombo.ToString();
                screenzap.Properties.Settings.Default.Save();
            }
        }

        private void showBalloonMenuItem_Click(object sender, EventArgs e)
        {
            showBalloonMenuItem.Checked = !showBalloonMenuItem.Checked;
            Properties.Settings.Default.showBalloon = showBalloonMenuItem.Checked;
            Properties.Settings.Default.Save();
        }

        private void setfolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var b = new FolderBrowserDialog();
            if (b.ShowDialog() == DialogResult.OK)
            {
                Properties.Settings.Default.captureFolder = b.SelectedPath;
                Properties.Settings.Default.Save();
            }
        }
    }
}
