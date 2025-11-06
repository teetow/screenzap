using screenzap.lib;
using System;
using System.Diagnostics;
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
        private readonly KeyboardHook rectCaptureHook = new KeyboardHook();
        private readonly KeyboardHook seqCaptureHook = new KeyboardHook();
        private readonly string autostartAppName = "Screenzap";
        private readonly string assemblyLocation = Assembly.GetExecutingAssembly().Location;  // Or the EXE path.
        private KeyCombo rectCaptureCombo;
        private KeyCombo seqCaptureCombo;
        private bool isCapturing;
        private ImageEditor? imageEditor;

        public Screenzap()
        {
            InitializeComponent();
            rectCaptureCombo = ParseKeyCombo(Properties.Settings.Default.currentCombo);
            seqCaptureCombo = ParseKeyCombo(Properties.Settings.Default.seqCaptureCombo);
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

            imageEditor = new ImageEditor();
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
            ArgumentNullException.ThrowIfNull(bitmap);

            using (MemoryStream pngMemStream = new MemoryStream())
            {
                DataObject data = new DataObject();

                data.SetData(DataFormats.Bitmap, true, bitmap);

                bitmap.Save(pngMemStream, ImageFormat.Png);
                data.SetData("PNG", false, pngMemStream);

                Clipboard.SetDataObject(data, true);
                ClipboardMetadata.LastCaptureTimestamp = DateTime.Now;
            }
        }

        void DoCapture(object? sender, KeyPressedEventArgs e)
        {
            if (isCapturing) return;
            isCapturing = true;
            try
            {
                Overlay ovl = new Overlay();
                var captureRect = ovl.CaptureRect();

                if (captureRect.Width <= 0 || captureRect.Height <= 0)
                {
                    isCapturing = false;
                    return;
                }

                Bitmap bmpScreenshot = new Bitmap(captureRect.Width, captureRect.Height, PixelFormat.Format32bppArgb);
                using (Graphics gfxScreenshot = Graphics.FromImage(bmpScreenshot))
                {
                    gfxScreenshot.CopyFromScreen(captureRect.Location, new Point(0, 0), captureRect.Size, CopyPixelOperation.SourceCopy);
                }

                setClipboard(bmpScreenshot);

                var audio = CreateZapSoundPlayer();
                audio.Play();

            }
            catch (Exception ex)
            {
                Console.Write(ex.ToString());
            }

            isCapturing = false;
        }

        void DoInstantCapture(object? sender, KeyPressedEventArgs e)
        {
            if (isCapturing) return;
            isCapturing = true;
            try
            {
                var captureAreaLeft = 0;
                var captureAreaTop = 0;
                var primaryScreen = ResolveScreen();
                var captureAreaWidth = primaryScreen.Bounds.Width;
                var captureAreaHeight = primaryScreen.Bounds.Height;
                var captureRect = new Rectangle(captureAreaLeft, captureAreaTop, captureAreaWidth, captureAreaHeight);

                if (captureRect.Width <= 0 || captureRect.Height <= 0)
                {
                    throw new Exception($"Invalid capture area {captureRect.Width}x{captureRect.Height}");
                }

                Bitmap bmpScreenshot = new Bitmap(captureRect.Width, captureRect.Height, PixelFormat.Format32bppArgb);
                using (Graphics gfxScreenshot = Graphics.FromImage(bmpScreenshot))
                {
                    gfxScreenshot.CopyFromScreen(captureRect.Location, new Point(0, 0), captureRect.Size, CopyPixelOperation.SourceCopy);
                }

                var dateStr = DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ss") + (".png");
                var userPath = Environment.ExpandEnvironmentVariables(Properties.Settings.Default.captureFolder);
                var filePath = Path.Combine(userPath, dateStr);

                using (FileStream pngFileStream = new FileStream(filePath, FileMode.Create))
                {
                    bmpScreenshot.Save(pngFileStream, ImageFormat.Png);
                }

                setClipboard(bmpScreenshot);

                var audio = CreateZapSoundPlayer();
                audio.Play();
            }
            catch (Exception ex)
            {
                Console.Write(ex.ToString());
            }

            isCapturing = false;
        }

        private void sanitizeClipboardToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            Image? imgData = Clipboard.GetImage();

            if (imgData == null)
                return;
            imageEditor = new ImageEditor();
            imageEditor.LoadImage(imgData);
            imageEditor.ShowDialog();
        }

        private void startWhenLoggedInToolStripMenuItem_CheckStateChanged(object? sender, EventArgs e)
        {
            if (startWhenLoggedInToolStripMenuItem.Checked)
                Util.SetAutoStart(autostartAppName, assemblyLocation);
            else
            {
                if (Util.IsAutoStartEnabled(autostartAppName, assemblyLocation))
                    Util.UnSetAutoStart(autostartAppName);
            }
        }

        private void saveClipboardToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            var img = Clipboard.GetImage();
            if (img == null) return;

            var fname = FileUtils.SaveImage(img);
            notifyIcon1.ShowBalloonTip(2000, $"Image saved", $"Saved to {fname}", ToolTipIcon.Info);


            var pStartInfo = new ProcessStartInfo

            {
                FileName = "explorer",
                Arguments = $"/e, /select,\"{fname}\""
            };

            EventHandler? handler = null;

            handler = (s, ev) =>
            {
                Process.Start(pStartInfo);
                notifyIcon1.BalloonTipClicked -= handler;
            };

            notifyIcon1.BalloonTipClicked += handler;
        }

        private void setKeyboardShortcutToolStripMenuItem_Click(object? sender, EventArgs e)
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

        private void showBalloonMenuItem_Click(object? sender, EventArgs e)
        {
            showBalloonMenuItem.Checked = !showBalloonMenuItem.Checked;
            Properties.Settings.Default.showBalloon = showBalloonMenuItem.Checked;
            Properties.Settings.Default.Save();
        }

        private void setFolderToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                Properties.Settings.Default.captureFolder = dialog.SelectedPath;
                Properties.Settings.Default.Save();
            }
        }

        private void notifyIcon1_DoubleClick(object? sender, EventArgs e)
        {
            if (imageEditor == null || imageEditor.IsDisposed)
            {
                imageEditor = new ImageEditor();
            }
            Image? imgData = Clipboard.GetImage();
            if (imgData != null)
            {
                imageEditor.LoadImage(imgData);
            }
            imageEditor.ShowAndFocus();
        }

        private void quitToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            Close();
        }

        private static KeyCombo ParseKeyCombo(string? combo)
        {
            if (string.IsNullOrWhiteSpace(combo))
            {
                return new KeyCombo(Keys.Control, Keys.PrintScreen);
            }

            try
            {
                return new KeyCombo(combo);
            }
            catch (ArgumentException)
            {
                return new KeyCombo(Keys.Control, Keys.PrintScreen);
            }
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

        private static SoundPlayer CreateZapSoundPlayer()
        {
            var data = Properties.Resources.zap;
            if (data == null || data.Length == 0)
            {
                throw new InvalidOperationException("Zap sound resource is missing.");
            }

            var stream = new MemoryStream(data, writable: false);
            return new SoundPlayer(stream);
        }
    }
}
