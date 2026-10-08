using screenzap.Components;
using screenzap.lib;
using screenzap.Native;
using Microsoft.UI.Dispatching;
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
    internal sealed class ScreenzapBackground : IDisposable
    {
        private readonly DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
        private readonly TrayIcon tray;
        private CaptureOverlay? captureOverlay;
        internal event Action? Closed;
        private readonly KeyboardHook rectCaptureHook = new KeyboardHook();
        private readonly KeyboardHook seqCaptureHook = new KeyboardHook();
        private readonly string autostartAppName = "Screenzap";
        private readonly string autoStartCommand = BuildAutoStartCommand();
        private KeyCombo rectCaptureCombo;
        private KeyCombo seqCaptureCombo;
        private bool isCapturing;
        private ImageDocumentEditor? imageEditor;
        private ClipboardDocumentHost? clipboardEditorHost;
        private DispatcherQueueTimer? clipboardEditorWarmupTimer;
        private bool isShuttingDown;
        private DateTime lastErrorNotificationUtc;
        private readonly List<int> rectCaptureHotkeyIds = new();
        private readonly List<int> seqCaptureHotkeyIds = new();
        private static bool zapResourceUnavailable;

        private ClipboardMonitor? clipboardMonitor;
        private string? lastQrPayload;
        private DateTime lastQrNotificationUtc;

        public ScreenzapBackground()
        {
            Logger.StartNewSession(clearExisting: true);
            Logger.Log($"Startup directories: base='{AppContext.BaseDirectory}', current='{Environment.CurrentDirectory}'");
            tray = new TrayIcon
            {
                OpenRequested = ShowClipboardEditorForCurrentData,
                SaveRequested = SaveClipboard,
                QuitRequested = Close
            };

            AddBuildInfoMenuItem();

            rectCaptureCombo = ParseKeyCombo(Properties.Settings.Default.currentCombo);
            seqCaptureCombo = ParseKeyCombo(Properties.Settings.Default.seqCaptureCombo);
            Util.RepairAutoStartTarget(autostartAppName, autoStartCommand);

            updateTooltips(rectCaptureCombo);
            if (Properties.Settings.Default.showBalloon == true)
            {
                tray.ShowNotification("Screenzap is running!", $"Press {rectCaptureCombo} to take a screenshot.", TrayNotificationKind.Info);
            }

            rectCaptureHook.KeyPressed += new EventHandler<KeyPressedEventArgs>(DoCapture);
            RegisterRectCaptureHotkeys();

            seqCaptureHook.KeyPressed += new EventHandler<KeyPressedEventArgs>(DoInstantCapture);
            RegisterSeqCaptureHotkeys();

            InitializeQrClipboardMonitor();

            Logger.Log("Screenzap initialized");
        }

        public void Dispose()
        {
            if (isShuttingDown) return;
            isShuttingDown = true;
            captureOverlay?.Dispose();
            captureOverlay = null;
            rectCaptureHook.Dispose();
            seqCaptureHook.Dispose();
            tray.Dispose();
            clipboardEditorWarmupTimer?.Stop();
            clipboardEditorWarmupTimer = null;
            systemHistoryService?.Dispose();
            systemHistoryService = null;
            clipboardEditorHost?.Dispose();
            clipboardEditorHost = null;
            imageEditor = null;

            if (clipboardMonitor != null)
            {
                clipboardMonitor.OnUpdateImage -= ClipboardMonitor_OnUpdateImageForQr;
                clipboardMonitor.OnUpdate -= ClipboardMonitor_CaptureLiveAlpha;
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
                clipboardMonitor.OnUpdate += ClipboardMonitor_CaptureLiveAlpha;
                clipboardMonitor.isListening = true;
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to initialize QR clipboard monitor: {ex.Message}");
            }
        }

        /// <summary>
        /// Fires on the UI thread when the live clipboard gains an image. We re-read the clipboard
        /// with the alpha-preserving decoder and hand it to the history service as the "live alpha
        /// candidate" for the item Windows is about to add to clipboard history — which would
        /// otherwise be alpha-stripped. Runs regardless of whether the editor window is open so the
        /// candidate is ready by the time the WinRT HistoryChanged refresh decodes the new entry.
        /// </summary>
        private void ClipboardMonitor_CaptureLiveAlpha(object? sender, EventArgs e)
        {
            var service = systemHistoryService;
            var host = clipboardEditorHost;
            if (service == null && host == null)
            {
                return;
            }

            try
            {
                // Ignore our own writes: they're already the item the WinRT service is tracking, and
                // re-publishing would only race the internal-write suppression window.
                if (clipboardEditorHost?.IsInternalClipboardWriteWindow() == true)
                {
                    return;
                }

                // TryRead returns null for non-image clipboards, which clears any stale candidate so
                // it can't attach to a later unrelated history item.
                using var alpha = ClipboardImageDecoder.TryRead(Clipboard.GetDataObject());
                service?.SetLiveAlphaCandidate(alpha);

                // Some Chrome image copies are deliberately excluded from Windows clipboard
                // history. Observe the live clipboard directly so those images still enter
                // Screenzap history; the seeded marker lets a later WinRT item absorb this fallback.
                if (alpha != null && host != null)
                {
                    var (observed, added) = host.HistoryStore.EnsureTopObservedImage(alpha);
                    if (added)
                    {
                        observed.IsSeededFallback = true;
                        host.OnObservedClipboardItem(observed);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to capture live alpha candidate: {ex.Message}");
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
                        dispatcher.TryEnqueue(() => { if (!isShuttingDown) ShowQrBalloon(payload); });
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

            tray.ShowNotification(title, message, TrayNotificationKind.Info, () => LaunchQrPayload(payload));
        }

        private static string TruncateForBalloon(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength - 1) + "…";
        }

        private void LaunchQrPayload(string payload)
        {
            try { Process.Start(new ProcessStartInfo { FileName = payload, UseShellExecute = true }); }
            catch (Exception ex)
            {
                Logger.Log($"Failed to launch QR payload: {ex.Message}");
                tray.ShowNotification("Unable to open", "Could not launch QR code content.", TrayNotificationKind.Error);
            }
        }

        private void AddBuildInfoMenuItem()
        {
            try
            {
                var exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Screenzap.exe");
                var lastWriteLocal = File.GetLastWriteTime(exePath);
                var buildConfiguration = GetBuildConfigurationName();

                var assembly = Assembly.GetExecutingAssembly();
                var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                var version = assembly.GetName().Version?.ToString();
                var versionText = informational ?? version ?? "unknown";

                var itemText = $"Build ({buildConfiguration}): {lastWriteLocal:yyyy-MM-dd HH:mm:ss}   v{versionText}";
                tray.BuildDescription = itemText;
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to add build info menu item: {ex}");
            }
        }

        private static string GetBuildConfigurationName()
        {
#if DEBUG
            return "Debug";
#else
            return "Release";
#endif
        }

        void updateTooltips(KeyCombo keyCombo)
        {
            tray.Tooltip = $"Screenzap is running! \n\nPress {keyCombo}.";
        }


        void setClipboard(Bitmap bitmap)
        {
            ClipboardImageWriter.WriteImage(bitmap);
            ClipboardMetadata.LastCaptureTimestamp = DateTime.Now;
        }

        async void DoCapture(object? sender, KeyPressedEventArgs e)
        {
            if (isCapturing) return;
            isCapturing = true;
            try
            {
                Logger.Log("DoCapture triggered");
                var cursorScreen = Screen.FromPoint(Cursor.Position);
                using Bitmap frozenScreen = CaptureScreenBitmap(cursorScreen);
                using var overlay = new CaptureOverlay(cursorScreen.Bounds, frozenScreen);
                captureOverlay = overlay;
                var captureRect = await overlay.SelectAsync();
                captureOverlay = null;
                if (isShuttingDown) return;

                if (captureRect.Width <= 0 || captureRect.Height <= 0)
                {
                    isCapturing = false;
                    return;
                }

                using Bitmap bmpScreenshot = frozenScreen.Clone(captureRect, PixelFormat.Format32bppArgb);

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

            finally { captureOverlay = null; isCapturing = false; }
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

                using Bitmap bmpScreenshot = new Bitmap(captureRect.Width, captureRect.Height, PixelFormat.Format32bppArgb);
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

        private bool GetStartOnLogin() => Util.IsAutoStartEnabled(autostartAppName);

        private void SetStartOnLogin(bool enabled)
        {
            if (enabled)
                Util.SetAutoStart(autostartAppName, autoStartCommand);
            else
                Util.UnSetAutoStart(autostartAppName);
        }

        private static string BuildAutoStartCommand()
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                processPath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Screenzap.exe");
            }

            return $"\"{processPath}\"";
        }

        private void SaveClipboard()
        {
            using var img = ClipboardImageDecoder.TryRead(Clipboard.GetDataObject());
            if (img != null)
            {
                var fname = FileUtils.SaveImage(img);
                tray.ShowNotification("Image saved", $"Saved to {fname}", TrayNotificationKind.Info, () => RevealInExplorer(fname));
                return;
            }

            var text = TryGetClipboardText();
            if (!string.IsNullOrEmpty(text))
            {
                var fname = FileUtils.SaveText(text);
                tray.ShowNotification("Text saved", $"Saved to {fname}", TrayNotificationKind.Info, () => RevealInExplorer(fname));
                return;
            }

            tray.ShowNotification("Clipboard empty", "No image or text data available to save.", TrayNotificationKind.Warning);
        }

        private static void RevealInExplorer(string path)
        {
            Process.Start(new ProcessStartInfo { FileName = "explorer", Arguments = $"/e, /select,\"{path}\"" });
        }

        private bool TrySetNativeCaptureShortcut(Keys keys)
        {
            var proposed = new KeyCombo(keys);
            if (proposed.Key == Keys.None || proposed.Key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return false;
            if (proposed.Equals(rectCaptureCombo)) return true;
            try
            {
                using var validation = new KeyboardHook();
                validation.RegisterHotKey(proposed.getModifierKeys(), proposed.Key);
            }
            catch (Exception ex) { Logger.Log($"Capture shortcut unavailable: {ex.Message}"); return false; }
            rectCaptureCombo = proposed;
            RegisterRectCaptureHotkeys();
            updateTooltips(rectCaptureCombo);
            Properties.Settings.Default.currentCombo = rectCaptureCombo.ToString();
            Properties.Settings.Default.Save();
            return true;
        }

        private static bool GetStartupNotificationEnabled() => Properties.Settings.Default.showBalloon;

        private static void SetStartupNotificationEnabled(bool enabled)
        {
            Properties.Settings.Default.showBalloon = enabled;
            Properties.Settings.Default.Save();
        }



        private void ShowClipboardEditorForCurrentData()
        {
            using var perf = PerfTrace.Scope(
                "Screenzap.ShowClipboardEditor",
                () => $"items={clipboardEditorHost?.HistoryStore.Items.Count ?? 0} warmed={clipboardEditorHost != null}",
                slowMs: 50,
                summaryEvery: 1);

            var host = EnsureClipboardHost();

            // Always consult the live clipboard. Chrome can publish a valid image without adding it
            // to Windows clipboard history, so relying on WinRT whenever our list is non-empty leaves
            // the editor stuck on an older item. EnsureTopObservedImage deduplicates unchanged opens.
            try
            {
                using var img = ClipboardImageDecoder.TryRead(Clipboard.GetDataObject());
                if (img != null)
                {
                    var (observed, added) = host.HistoryStore.EnsureTopObservedImage(img);
                    if (added)
                    {
                        observed.IsSeededFallback = true;
                        host.OnObservedClipboardItem(observed);
                    }
                }
            }
            catch (ExternalException ex)
            {
                Logger.Log($"Failed to observe live clipboard image: {ex.Message}");
            }

            var top = host.HistoryStore.TopItem;
            if (top == null)
            {
                tray.ShowNotification("Clipboard empty", "Clipboard does not contain image data.", TrayNotificationKind.Info);
                return;
            }

            host.ActivatePreferredHistoryItem();

            host.ShowAndActivate();
        }

        private void ScheduleClipboardEditorWarmup()
        {
            if (isShuttingDown || (clipboardEditorHost != null && !clipboardEditorHost.IsDisposed))
            {
                return;
            }

            if (clipboardEditorWarmupTimer == null)
            {
                clipboardEditorWarmupTimer = dispatcher.CreateTimer();
                clipboardEditorWarmupTimer.Interval = TimeSpan.FromMilliseconds(250);
                clipboardEditorWarmupTimer.IsRepeating = false;
                clipboardEditorWarmupTimer.Tick += (_, _) =>
                {
                    clipboardEditorWarmupTimer?.Stop();
                    WarmClipboardEditor();
                };
            }

            clipboardEditorWarmupTimer.Stop();
            clipboardEditorWarmupTimer.Start();
        }

        private void WarmClipboardEditor()
        {
            if (isShuttingDown || (clipboardEditorHost != null && !clipboardEditorHost.IsDisposed))
            {
                return;
            }

            using var perf = PerfTrace.Scope(
                "Screenzap.WarmClipboardEditor",
                () => $"items={clipboardEditorHost?.HistoryStore.Items.Count ?? 0}",
                slowMs: 100,
                summaryEvery: 1);

            try
            {
                var host = EnsureClipboardHost();

            }
            catch (Exception ex)
            {
                Logger.Log($"Clipboard editor warmup failed: {ex.Message}");
            }
        }

        private ClipboardDocumentHost EnsureClipboardHost()
        {
            if (clipboardEditorHost == null || clipboardEditorHost.IsDisposed)
            {
                var imagePresenter = EnsureImageEditor();
                clipboardEditorHost = new ClipboardDocumentHost(imagePresenter);

                EditorHostCreated?.Invoke(clipboardEditorHost, imagePresenter);
                WireHostAppMenuHooks(clipboardEditorHost);
                InitializeSystemClipboardHistoryForHost(clipboardEditorHost);
            }

            return clipboardEditorHost;
        }

        /// <summary>
        /// Wires the editor window's menu bar (Settings/File items) to the tray host's app-level
        /// actions. These moved off the tray context menu when it was slimmed to essentials.
        /// </summary>
        private void WireHostAppMenuHooks(ClipboardDocumentHost host)
        {
            host.GetStartOnLogin = GetStartOnLogin;
            host.SetStartOnLogin = SetStartOnLogin;
            host.GetStartupNotificationEnabled = GetStartupNotificationEnabled;
            host.SetStartupNotificationEnabled = SetStartupNotificationEnabled;
            host.GetCaptureShortcut = () => rectCaptureCombo.Key | rectCaptureCombo.Modifiers;
            host.TrySetCaptureShortcut = TrySetNativeCaptureShortcut;
            host.SaveClipboardImageRequested = SaveClipboard;
        }

        private SystemClipboardHistoryService? systemHistoryService;

        private void InitializeSystemClipboardHistoryForHost(ClipboardDocumentHost host)
        {
            try
            {
                systemHistoryService?.Dispose();
                systemHistoryService = new SystemClipboardHistoryService(
                    host.HistoryStore,
                    action => dispatcher.TryEnqueue(() => { if (!isShuttingDown) action(); }),
                    onItemObserved: host.OnObservedClipboardItem,
                    tryBindPendingCommittedItem: host.TryBindPendingCommittedSystemItem,
                    isInternalWriteWindow: host.IsInternalClipboardWriteWindow);

                host.RefreshSystemHistoryAsync = () =>
                    systemHistoryService?.RefreshAsync() ?? System.Threading.Tasks.Task.CompletedTask;

                if (systemHistoryService.IsAvailable)
                {
                    systemHistoryService.Start();
                }
                else
                {
                    Logger.Log("Windows clipboard history is disabled or unavailable; Screenzap history will be populated on demand only.");
                }

                host.TryDeleteFromSystemHistoryAsync = async systemHistoryId =>
                    systemHistoryService != null
                    && await systemHistoryService.TryDeleteSystemItemAsync(systemHistoryId);
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to initialize SystemClipboardHistoryService: {ex.Message}");
            }
        }



        internal Action<ClipboardDocumentHost, ImageDocumentEditor>? EditorHostCreated { get; set; }
        internal ClipboardDocumentHost EditorHost => EnsureClipboardHost();
        internal void OpenEditor() => ShowClipboardEditorForCurrentData();
        internal void StartBackgroundServices() => ScheduleClipboardEditorWarmup();

        private ImageDocumentEditor EnsureImageEditor()
        {
            if (imageEditor == null || imageEditor.IsDisposed)
            {
                imageEditor = new ImageDocumentEditor();
            }

            return imageEditor;
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

        internal void Close()
        {
            if (isShuttingDown) return;
            try { Closed?.Invoke(); }
            finally { Dispose(); }
        }

        private static Bitmap CaptureScreenBitmap(Screen screen)
        {
            var bounds = screen.Bounds;
            Bitmap screenshot = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            if (!TryBitBltCapture(bounds, screenshot))
            {
                screenshot.Dispose();
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
            tray.ShowNotification(title, message, TrayNotificationKind.Error);
        }

        private void RegisterRectCaptureHotkeys()
        {
            RegisterHotkeys(rectCaptureHook, rectCaptureCombo, rectCaptureHotkeyIds, "Can't register the windowed capture hotkey. Please pick a better one.");
        }

        private void RegisterSeqCaptureHotkeys()
        {
            RegisterHotkeys(seqCaptureHook, seqCaptureCombo, seqCaptureHotkeyIds, "Can't register the instant capture hotkey. Please pick a better one.");
        }

        private void RegisterHotkeys(KeyboardHook hook, KeyCombo combo, List<int> storage, string failureMessage)
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
                        tray.ShowNotification("Capture shortcut unavailable", failureMessage, TrayNotificationKind.Error);
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
