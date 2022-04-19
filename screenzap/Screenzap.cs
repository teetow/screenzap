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
        KeyboardHook hook = new KeyboardHook();
        string autostartAppName = "Screenzap";
        string assemblyLocation = Assembly.GetExecutingAssembly().Location;  // Or the EXE path.
        KeyCombo currentCombo;
        bool isCapturing = false;

        public Screenzap()
        {
            InitializeComponent();
            this.currentCombo = new KeyCombo(Properties.Settings.Default.currentCombo);
            this.startWhenLoggedInToolStripMenuItem.Checked = Util.IsAutoStartEnabled(autostartAppName, assemblyLocation);
            this.showBalloonMenuItem.Checked = Properties.Settings.Default.showBalloon;


            updateTooltips(currentCombo);
            if (Properties.Settings.Default.showBalloon == true)
            {
                this.notifyIcon1.ShowBalloonTip(2000, "Screenzap is running!", $"Press {currentCombo} to take a screenshot.", ToolTipIcon.Info);
            }

            hook.KeyPressed += new EventHandler<KeyPressedEventArgs>(DoCapture);
            try
            {
                hook.RegisterHotKey(currentCombo.getModifierKeys(), currentCombo.Key);
            }
            catch
            {
                MessageBox.Show("Can't register the hotkey. Please pick a better one.");
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
            ShortcutEditor shortcutEditor = new ShortcutEditor(currentCombo);
            var rslt = shortcutEditor.ShowDialog();
            if (rslt == DialogResult.OK)
            {
                currentCombo = shortcutEditor.currentCombo;
                try
                {
                    hook.UnregisterHotkey(1);
                    hook.RegisterHotKey(currentCombo.getModifierKeys(), currentCombo.Key);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                    return;
                }

                updateTooltips(currentCombo);
                screenzap.Properties.Settings.Default.currentCombo = currentCombo.ToString();
                screenzap.Properties.Settings.Default.Save();
            }
        }

        private void showBalloonMenuItem_Click(object sender, EventArgs e)
        {
            showBalloonMenuItem.Checked = !showBalloonMenuItem.Checked;
            Properties.Settings.Default.showBalloon = showBalloonMenuItem.Checked;
            Properties.Settings.Default.Save();
        }
    }
}
