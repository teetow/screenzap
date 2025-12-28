using screenzap.Components;
using screenzap.lib;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using HotkeyModifierKeys = global::ModifierKeys;

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
        private TextEditor? textEditor;
        private ClipboardEditorHostForm? clipboardEditorHost;
        private DateTime lastErrorNotificationUtc;
        private readonly List<int> rectCaptureHotkeyIds = new();
        private readonly List<int> seqCaptureHotkeyIds = new();
        private static bool zapResourceUnavailable;

        private ClipboardMonitor? clipboardMonitor;
        private string? lastQrPayload;
        private DateTime lastQrNotificationUtc;

        public Screenzap()
        {
            Logger.StartNewSession(clearExisting: true);
            Logger.Log($"Startup directories: base='{AppContext.BaseDirectory}', current='{Environment.CurrentDirectory}'");
            InitializeComponent();

            AddBuildInfoMenuItem();

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
            RegisterRectCaptureHotkeys();

            seqCaptureHook.KeyPressed += new EventHandler<KeyPressedEventArgs>(DoInstantCapture);
            RegisterSeqCaptureHotkeys();

            InitializeQrClipboardMonitor();

            Logger.Log("Screenzap initialized");
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);

            if (clipboardMonitor != null)
            {
                clipboardMonitor.OnUpdateImage -= ClipboardMonitor_OnUpdateImageForQr;
                clipboardMonitor.Dispose();
                clipboardMonitor = null;
            }
        }

        private void InitializeQrClipboardMonitor()
        {
            try
            {
                clipboardMonitor = new ClipboardMonitor();
                clipboardMonitor.OnUpdateImage += ClipboardMonitor_OnUpdateImageForQr;
                clipboardMonitor.isListening = true;
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to initialize QR clipboard monitor: {ex.Message}");
            }
        }

        private void ClipboardMonitor_OnUpdateImageForQr(object? sender, Bitmap image)
        {
            if (image == null)
            {
                return;
            }

            // Clone so we can decode off-thread safely.
            Bitmap? clone = null;
            try
            {
                clone = (Bitmap)image.Clone();
            }
            catch
            {
                return;
            }

            _ = System.Threading.Tasks.Task.Run(() =>
            {
                using (clone)
                {
                    var payload = QrCodeDecoder.TryDecode(clone);
                    if (string.IsNullOrWhiteSpace(payload))
                    {
                        return;
                    }

                    try
                    {
                        BeginInvoke(new Action(() => ShowQrBalloon(payload)));
                    }
                    catch
                    {
                        // Ignore if app is shutting down.
                    }
                }
            });
        }

        private void ShowQrBalloon(string payload)
        {
            var now = DateTime.UtcNow;

            if (string.Equals(payload, lastQrPayload, StringComparison.Ordinal) && (now - lastQrNotificationUtc).TotalSeconds < 30)
            {
                return;
            }

            lastQrPayload = payload;
            lastQrNotificationUtc = now;

            var title = "QR code detected";
            var message = TruncateForBalloon(payload, 200);

            if (Uri.TryCreate(payload, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Scheme))
            {
                // Prefer showing the URL (possibly shortened) rather than raw payload.
                message = TruncateForBalloon(uri.ToString(), 200);
            }

            AttachShellLauncher(payload);
            notifyIcon1.ShowBalloonTip(4000, title, message, ToolTipIcon.Info);
        }

        private static string TruncateForBalloon(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength - 1) + "…";
        }

        private void AttachShellLauncher(string payload)
        {
            EventHandler? handler = null;

            handler = (s, ev) =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = payload,
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to launch QR payload: {ex.Message}");
                    notifyIcon1.ShowBalloonTip(3000, "Unable to open", "Could not launch QR code content.", ToolTipIcon.Error);
                }
                finally
                {
                    notifyIcon1.BalloonTipClicked -= handler;
                }
            };

            notifyIcon1.BalloonTipClicked += handler;
        }

        private void AddBuildInfoMenuItem()
        {
            try
            {
                var exePath = Application.ExecutablePath;
                var lastWriteLocal = File.GetLastWriteTime(exePath);

                var assembly = Assembly.GetExecutingAssembly();
                var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                var version = assembly.GetName().Version?.ToString();
                var versionText = informational ?? version ?? "unknown";

                var itemText = $"Build: {lastWriteLocal:yyyy-MM-dd HH:mm:ss}   v{versionText}";
                var buildInfoItem = new ToolStripMenuItem(itemText)
                {
                    Enabled = false
                };

                var insertIndex = contextMenuStrip1.Items.IndexOf(toolStripSeparator3);
                if (insertIndex >= 0)
                {
                    contextMenuStrip1.Items.Insert(insertIndex + 1, buildInfoItem);
                }
                else
                {
                    contextMenuStrip1.Items.Add(buildInfoItem);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to add build info menu item: {ex}");
            }
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
                Logger.Log("DoCapture triggered");
                var cursorScreen = Screen.FromPoint(Cursor.Position);
                using Bitmap frozenScreen = CaptureScreenBitmap(cursorScreen);
                Overlay ovl = new Overlay(cursorScreen, frozenScreen);
                var captureRect = ovl.CaptureRect();

                if (captureRect.Width <= 0 || captureRect.Height <= 0)
                {
                    isCapturing = false;
                    return;
                }

                Bitmap bmpScreenshot = frozenScreen.Clone(captureRect, PixelFormat.Format32bppArgb);

                setClipboard(bmpScreenshot);

                var audio = CreateZapSoundPlayer();
                audio?.Play();
                Logger.Log("DoCapture completed");

            }
            catch (Exception ex)
            {
                Console.Write(ex.ToString());
                Logger.Log($"DoCapture failed: {ex}");
                NotifyCaptureFailure("Screen capture failed", ex.Message);
            }

            isCapturing = false;
        }

        void DoInstantCapture(object? sender, KeyPressedEventArgs e)
        {
            if (isCapturing) return;
            isCapturing = true;
            try
            {
                Logger.Log("DoInstantCapture triggered");
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
                audio?.Play();
                Logger.Log("DoInstantCapture completed");
            }
            catch (Exception ex)
            {
                Console.Write(ex.ToString());
                Logger.Log($"DoInstantCapture failed: {ex}");
                NotifyCaptureFailure("Instant capture failed", ex.Message);
            }

            isCapturing = false;
        }

        private void sanitizeClipboardToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            ShowClipboardEditorForCurrentData();
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
            if (img != null)
            {
                var fname = FileUtils.SaveImage(img);
                notifyIcon1.ShowBalloonTip(2000, "Image saved", $"Saved to {fname}", ToolTipIcon.Info);
                AttachExplorerLauncher(fname);
                return;
            }

            var text = TryGetClipboardText();
            if (!string.IsNullOrEmpty(text))
            {
                var fname = FileUtils.SaveText(text);
                notifyIcon1.ShowBalloonTip(2000, "Text saved", $"Saved to {fname}", ToolTipIcon.Info);
                AttachExplorerLauncher(fname);
                return;
            }

            notifyIcon1.ShowBalloonTip(2000, "Clipboard empty", "No image or text data available to save.", ToolTipIcon.Warning);
        }

        private void AttachExplorerLauncher(string path)
        {
            var pStartInfo = new ProcessStartInfo

            {
                FileName = "explorer",
                Arguments = $"/e, /select,\"{path}\""
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
                    RegisterRectCaptureHotkeys();
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
            ShowClipboardEditorForCurrentData();
        }

        private void ShowClipboardEditorForCurrentData()
        {
            IDataObject? dataObject = null;
            try
            {
                dataObject = Clipboard.GetDataObject();
            }
            catch (ExternalException ex)
            {
                Logger.Log($"Failed to access clipboard: {ex.Message}");
                notifyIcon1.ShowBalloonTip(2000, "Clipboard error", "Unable to read clipboard contents.", ToolTipIcon.Error);
                return;
            }

            if (dataObject == null)
            {
                notifyIcon1.ShowBalloonTip(2000, "Clipboard empty", "Clipboard does not contain text or image data.", ToolTipIcon.Info);
                return;
            }

            var host = EnsureClipboardHost();
            if (host.TryShowClipboardData(dataObject))
            {
                return;
            }

            notifyIcon1.ShowBalloonTip(2000, "Clipboard empty", "Clipboard does not contain text or image data.", ToolTipIcon.Info);
        }

        private ClipboardEditorHostForm EnsureClipboardHost()
        {
            if (clipboardEditorHost == null || clipboardEditorHost.IsDisposed)
            {
                var imagePresenter = EnsureImageEditor();
                var textPresenter = EnsureTextEditor();
                clipboardEditorHost = new ClipboardEditorHostForm(imagePresenter, textPresenter);
                clipboardEditorHost.FormClosed += OnClipboardHostClosed;
            }

            return clipboardEditorHost;
        }

        private void OnClipboardHostClosed(object? sender, FormClosedEventArgs e)
        {
            if (sender is ClipboardEditorHostForm host)
            {
                host.FormClosed -= OnClipboardHostClosed;
            }

            clipboardEditorHost = null;
            imageEditor = null;
            textEditor = null;
        }

        private ImageEditor EnsureImageEditor()
        {
            if (imageEditor == null || imageEditor.IsDisposed)
            {
                imageEditor = new ImageEditor();
            }

            imageEditor.RequestTextEditor = EnsureTextEditor;
            return imageEditor;
        }

        private TextEditor EnsureTextEditor()
        {
            if (textEditor == null || textEditor.IsDisposed)
            {
                textEditor = new TextEditor();
            }

            textEditor.RequestImageEditor = EnsureImageEditor;
            return textEditor;
        }

        private static string? TryGetClipboardText()
        {
            try
            {
                if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
                {
                    var text = Clipboard.GetText(TextDataFormat.UnicodeText);
                    ClipboardMetadata.LastTextCaptureTimestamp = DateTime.Now;
                    return text;
                }
            }
            catch (ExternalException ex)
            {
                Logger.Log($"Failed to read text from clipboard: {ex.Message}");
            }

            return null;
        }

        private void quitToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            Close();
        }

        private static Bitmap CaptureScreenBitmap(Screen screen)
        {
            var bounds = screen.Bounds;
            Bitmap screenshot = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            if (!TryBitBltCapture(bounds, screenshot))
            {
                throw new InvalidOperationException("BitBlt capture failed.");
            }

            return screenshot;
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
            IntPtr hdcSrc, int nXSrc, int nYSrc, CopyPixelOperation dwRop);

        private static bool TryBitBltCapture(Rectangle bounds, Bitmap screenshot)
        {
            try
            {
                using Graphics gDest = Graphics.FromImage(screenshot);
                using Graphics gSrc = Graphics.FromHwnd(IntPtr.Zero);
                IntPtr hdcDest = gDest.GetHdc();
                IntPtr hdcSrc = gSrc.GetHdc();
                try
                {
                    var op = CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt;
                    if (BitBlt(hdcDest, 0, 0, bounds.Width, bounds.Height, hdcSrc, bounds.Left, bounds.Top, op))
                    {
                        return true;
                    }
                }
                finally
                {
                    gSrc.ReleaseHdc(hdcSrc);
                    gDest.ReleaseHdc(hdcDest);
                }
            }
            catch (Exception ex) when (ex is ExternalException or Win32Exception or InvalidOperationException)
            {
                Logger.Log($"BitBlt capture failed: {ex.Message}");
            }

            return false;
        }

        private void NotifyCaptureFailure(string title, string message)
        {
            var now = DateTime.UtcNow;
            if ((now - lastErrorNotificationUtc).TotalSeconds < 5)
            {
                return;
            }

            lastErrorNotificationUtc = now;
            Logger.Log($"Capture failure: {title} - {message}");
            notifyIcon1.ShowBalloonTip(3000, title, message, ToolTipIcon.Error);
        }

        private void RegisterRectCaptureHotkeys()
        {
            RegisterHotkeys(rectCaptureHook, rectCaptureCombo, rectCaptureHotkeyIds, "Can't register the windowed capture hotkey. Please pick a better one.");
        }

        private void RegisterSeqCaptureHotkeys()
        {
            RegisterHotkeys(seqCaptureHook, seqCaptureCombo, seqCaptureHotkeyIds, "Can't register the instant capture hotkey. Please pick a better one.");
        }

        private static void RegisterHotkeys(KeyboardHook hook, KeyCombo combo, List<int> storage, string failureMessage)
        {
            ClearHotkeys(hook, storage);
            HotkeyModifierKeys baseModifiers = combo.getModifierKeys();

            foreach (var modifiers in EnumerateModifierVariants(baseModifiers))
            {
                try
                {
                    var id = hook.RegisterHotKey(modifiers, combo.Key);
                    storage.Add(id);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to register hotkey {modifiers}+{combo.Key}: {ex.Message}");
                    if (modifiers == baseModifiers)
                    {
                        MessageBox.Show(failureMessage);
                        break;
                    }
                }
            }
        }

        private static IEnumerable<HotkeyModifierKeys> EnumerateModifierVariants(HotkeyModifierKeys baseModifiers)
        {
            yield return baseModifiers;

            if (!baseModifiers.HasFlag(HotkeyModifierKeys.Alt))
            {
                yield return baseModifiers | HotkeyModifierKeys.Alt;
            }
        }

        private static void ClearHotkeys(KeyboardHook hook, List<int> storage)
        {
            foreach (var id in storage)
            {
                try
                {
                    hook.UnregisterHotkey(id);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to unregister hotkey {id}: {ex.Message}");
                }
            }

            storage.Clear();
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

        private static SoundPlayer? CreateZapSoundPlayer()
        {
            var soundPath = Path.Combine(AppContext.BaseDirectory, "res", "zap.wav");
            if (File.Exists(soundPath))
            {
                try
                {
                    return new SoundPlayer(soundPath);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to load zap sound from '{soundPath}': {ex.Message}");
                }
            }

            if (!zapResourceUnavailable)
            {
                try
                {
                    var data = Properties.Resources.zap;
                    if (data is { Length: > 0 })
                    {
                        var stream = new MemoryStream(data, writable: false);
                        var player = new SoundPlayer(stream);
                        stream.Position = 0;
                        return player;
                    }

                    zapResourceUnavailable = true;
                }
                catch (Exception ex) when (ex is MissingMethodException or TypeInitializationException)
                {
                    Logger.Log($"Zap sound resource unavailable on this runtime: {ex.Message}");
                    zapResourceUnavailable = true;
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to load zap sound from resources: {ex.Message}");
                    zapResourceUnavailable = true;
                }
            }

            Logger.Log("Zap sound missing; using system notification instead.");
            SystemSounds.Asterisk.Play();
            return null;
        }
    }
}
