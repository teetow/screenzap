using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace screenzap
{
    public partial class Screenzap : Form
    {
        KeyboardHook hook = new KeyboardHook();
        string autostartAppName = "Screenzap";
        string assemblyLocation = Assembly.GetExecutingAssembly().Location;  // Or the EXE path.
        string sndPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + Path.DirectorySeparatorChar + "zap.wav";
        KeyCombo currentCombo;

        public Screenzap()
        {
            InitializeComponent();
            this.currentCombo = new KeyCombo(screenzap.Properties.Settings.Default.currentCombo);
            this.startWhenLoggedInToolStripMenuItem.Checked = Util.IsAutoStartEnabled(autostartAppName, assemblyLocation);
            updateTooltips(currentCombo);
            this.notifyIcon1.ShowBalloonTip(2000, "Screenzap is running!", $"Press {currentCombo.ToString()} to take a screenshot.", ToolTipIcon.Info);


            hook.KeyPressed += new EventHandler<KeyPressedEventArgs>(doCapture);
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
            this.notifyIcon1.Text = $"Screenzap is running! \n\nPress {keyCombo.ToString()}.";
        }

        void doCapture(object sender, KeyPressedEventArgs e)
        {
            Overlay ovl = new Overlay();
            var captureRect = ovl.CaptureRect();
            if (captureRect == Rectangle.Empty)
                return;

            var bmpScreenshot = new Bitmap(captureRect.Width, captureRect.Height, PixelFormat.Format32bppArgb);
            var gfxScreenshot = Graphics.FromImage(bmpScreenshot);

            gfxScreenshot.CopyFromScreen(captureRect.Location, new Point(0, 0), captureRect.Size, CopyPixelOperation.SourceCopy);
            var rslt = bmpScreenshot.PhysicalDimension.ToString();

            Clipboard.SetImage(bmpScreenshot);

            SoundPlayer audio = new SoundPlayer(screenzap.Properties.Resources.zap);
            audio.Play();
        }

        private void startWhenLoggedInToolStripMenuItem_CheckStateChanged(object sender, EventArgs e)
        {
            if (this.startWhenLoggedInToolStripMenuItem.Checked)
                Util.SetAutoStart(autostartAppName, assemblyLocation);
            else {
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
    }
}
