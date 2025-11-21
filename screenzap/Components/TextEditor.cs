using FontAwesome.Sharp;
using ScintillaNet.Abstractions.Classes;
using ScintillaNet.Abstractions.Enumerations;
using ScintillaNet.WinForms;
using ScintillaNet.WinForms.EventArguments;
using screenzap.lib;
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace screenzap
{
    public partial class TextEditor : Form
    {
        internal Func<ImageEditor>? RequestImageEditor { get; set; }
        private const string WindowTitleBase = "Screenzap Text Editor";
        private const string ThemeFileName = "text-editor-theme.json";
        private static readonly string PrimaryKeywords = string.Join(' ', new[]
        {
            "auto","break","case","catch","char","class","const","constexpr","continue","default","delete","do","double","else","enum","explicit","export","extern","for","friend","goto","if","inline","mutable","namespace","new","noexcept","nullptr","operator","private","protected","public","register","reinterpret_cast","return","signed","sizeof","static","static_cast","struct","switch","template","this","throw","try","typedef","typeid","typename","union","unsigned","using","virtual","void","volatile","while"
        });

        private static readonly string TypeKeywords = string.Join(' ', new[]
        {
            "bool","short","int","long","float","wchar_t","char16_t","char32_t","size_t","ptrdiff_t"
        });

        private static readonly string PreprocessorKeywords = string.Join(' ', new[]
        {
            "define","elif","else","endif","error","ifdef","ifndef","if","include","line","pragma","undef","warning"
        });
        private readonly UTF8Encoding utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private readonly JsonSerializerOptions themeSerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        private bool isDirty;
        private bool suppressTextChanged;
        private string? currentSavePath;
        private DateTime? bufferTimestamp;
        private TextEditorTheme currentTheme = TextEditorTheme.CreateDefault();
        private string? themeFilePath;
        private FileSystemWatcher? themeWatcher;
        private int themeReloadScheduled;
        private ClipboardMonitor? clipboardMonitor;
        private bool clipboardHasPendingReload;
        private bool ignoreNextClipboardUpdate;
        private string? pendingClipboardText;

        private static class CppStyles
        {
            public const int Default = 0;
            public const int Comment = 1;
            public const int CommentLine = 2;
            public const int CommentDoc = 3;
            public const int Number = 4;
            public const int Word = 5;
            public const int String = 6;
            public const int Character = 7;
            public const int Uuid = 8;
            public const int Preprocessor = 9;
            public const int Operator = 10;
            public const int Identifier = 11;
            public const int StringEol = 12;
            public const int Verbatim = 13;
            public const int Regex = 14;
            public const int CommentLineDoc = 15;
            public const int Word2 = 16;
            public const int CommentDocKeyword = 17;
            public const int CommentDocKeywordError = 18;
            public const int GlobalClass = 19;
            public const int RawString = 20;
            public const int HashQuotedString = 22;
            public const int PreprocessorComment = 23;
            public const int PreprocessorCommentDoc = 24;
            public const int UserLiteral = 25;
            public const int TaskMarker = 26;
        }

        public TextEditor()
        {
            InitializeComponent();
            ConfigureToolbarIcons();
            InitializeThemeConfiguration();
            ConfigureEditor();
            ConfigureThemeWatcher();
            InitializeClipboardMonitor();
            UpdateWindowTitle();
            UpdateStatusLabels();
            UpdateCommandStates();
        }

        internal void LoadText(string? text)
        {
            suppressTextChanged = true;
            editor.Text = text ?? string.Empty;
            editor.EmptyUndoBuffer();
            editor.SetSavePoint();
            suppressTextChanged = false;
            isDirty = false;
            currentSavePath = null;
            bufferTimestamp = ClipboardMetadata.LastTextCaptureTimestamp ?? DateTime.Now;
            UpdateWindowTitle();
            UpdateStatusLabels();
            UpdateCommandStates();
            ClearClipboardNotification(clearSnapshot: true);
            FocusEditor();
        }

        internal void ShowAndFocus()
        {
            if (!Visible)
            {
                Show();
            }
            else if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            Activate();
            FocusEditor();
        }

        internal void AdoptWindowGeometry(Form? source)
        {
            if (source == null)
            {
                return;
            }

            StartPosition = FormStartPosition.Manual;
            var referenceBounds = source.WindowState == FormWindowState.Normal
                ? source.Bounds
                : source.RestoreBounds;

            if (referenceBounds.Width > 0 && referenceBounds.Height > 0)
            {
                WindowState = FormWindowState.Normal;
                Bounds = referenceBounds;
            }

            if (source.WindowState == FormWindowState.Maximized)
            {
                WindowState = FormWindowState.Maximized;
            }
        }

        private void FocusEditor()
        {
            editor?.Focus();
        }

        private void ConfigureToolbarIcons()
        {
            ConfigureIconButton(saveToolStripButton, IconChar.FloppyDisk, "Save (Ctrl+S)");
            ConfigureIconButton(saveAsToolStripButton, IconChar.FilePen, "Save As (Ctrl+Shift+S)");
            ConfigureIconButton(copyToolStripButton, IconChar.Clipboard, "Save to Clipboard (Ctrl+Shift+C)");
            ConfigureIconButton(reloadToolStripButton, IconChar.Rotate, "Reload from Clipboard (Ctrl+R)");
            ConfigureIconButton(findToolStripButton, IconChar.MagnifyingGlass, "Find (Ctrl+F)");
            UpdateReloadIndicator();
        }

        private static void ConfigureIconButton(IconToolStripButton button, IconChar icon, string toolTip)
        {
            button.DisplayStyle = ToolStripItemDisplayStyle.Image;
            button.IconChar = icon;
            button.IconFont = IconFont.Auto;
            button.IconColor = SystemColors.ControlText;
            button.IconSize = 18;
            button.ToolTipText = toolTip;
            button.ImageScaling = ToolStripItemImageScaling.None;
        }

        private void ConfigureEditor()
        {
            editor.BorderStyle = BorderStyle.None;
            editor.WrapMode = WrapMode.None;
            editor.IndentationGuides = IndentView.LookBoth;
            editor.HScrollBar = true;
            editor.VScrollBar = true;
            editor.LexerName = "cpp";
            ConfigureKeywords();
            ApplyTheme(currentTheme);
            editor.CaretLineVisible = true;
            editor.Margins[0].Type = MarginType.Number;
            editor.Margins[0].Width = 50;
            editor.MultipleSelection = true;
            editor.AdditionalSelectionTyping = true;
            editor.MultiPaste = MultiPaste.Each;
            editor.VirtualSpaceOptions = VirtualSpace.RectangularSelection | VirtualSpace.UserAccessible;
            editor.MouseSelectionRectangularSwitch = true;
            editor.ViewWhitespace = WhitespaceMode.Invisible;
            editor.SetSelection(0, 0);
            editor.UpdateUi += Editor_UpdateUI;
            editor.TextChanged += Editor_TextChanged;
            editor.CharAdded += (_, __) => UpdateStatusLabels();
        }

        private void InitializeClipboardMonitor()
        {
            try
            {
                clipboardMonitor = new ClipboardMonitor();
                clipboardMonitor.OnUpdateText += ClipboardMonitor_OnUpdateText;
                clipboardMonitor.OnUpdateImage += ClipboardMonitor_OnUpdateImage;
                clipboardMonitor.isListening = true;
                CaptureClipboardSnapshotIfAvailable();
                UpdateReloadIndicator();
            }
            catch
            {
                clipboardMonitor = null;
            }
        }

        private void DisposeClipboardMonitor()
        {
            if (clipboardMonitor == null)
            {
                return;
            }

            clipboardMonitor.OnUpdateText -= ClipboardMonitor_OnUpdateText;
            clipboardMonitor.OnUpdateImage -= ClipboardMonitor_OnUpdateImage;
            clipboardMonitor.Dispose();
            clipboardMonitor = null;
        }

        private void CaptureClipboardSnapshotIfAvailable()
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    pendingClipboardText = Clipboard.GetText(TextDataFormat.UnicodeText);
                }
            }
            catch (ExternalException)
            {
                pendingClipboardText = null;
            }
        }

        private void ClipboardMonitor_OnUpdateText(object? sender, string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<object?, string>(ClipboardMonitor_OnUpdateText), sender, text);
                return;
            }

            if (ignoreNextClipboardUpdate)
            {
                ignoreNextClipboardUpdate = false;
                return;
            }

            pendingClipboardText = text;
            clipboardHasPendingReload = true;
            UpdateReloadIndicator();
        }

        private void ClipboardMonitor_OnUpdateImage(object? sender, Bitmap image)
        {
            void HandleUpdate()
            {
                if (ignoreNextClipboardUpdate)
                {
                    ignoreNextClipboardUpdate = false;
                    image.Dispose();
                    return;
                }

                pendingClipboardText = null;
                clipboardHasPendingReload = true;
                UpdateReloadIndicator();
                image.Dispose();
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(HandleUpdate));
            }
            else
            {
                HandleUpdate();
            }
        }

        private void UpdateReloadIndicator()
        {
            if (reloadToolStripButton == null || reloadNotificationLabel == null)
            {
                return;
            }

            var hasUpdate = clipboardHasPendingReload;
            reloadNotificationLabel.Visible = hasUpdate;
            reloadNotificationLabel.Text = hasUpdate ? "●" : string.Empty;
            reloadToolStripButton.IconColor = hasUpdate ? Color.OrangeRed : SystemColors.ControlText;
        }

        private void ClearClipboardNotification(bool clearSnapshot)
        {
            clipboardHasPendingReload = false;
            if (clearSnapshot)
            {
                pendingClipboardText = null;
            }

            UpdateReloadIndicator();
        }

        private bool TryGetClipboardText(out string text)
        {
            if (pendingClipboardText != null)
            {
                text = pendingClipboardText;
                return true;
            }

            try
            {
                if (Clipboard.ContainsText())
                {
                    text = Clipboard.GetText(TextDataFormat.UnicodeText);
                    return true;
                }
            }
            catch (ExternalException)
            {
            }

            text = string.Empty;
            return false;
        }

        private void ReloadFromClipboard()
        {
            if (!ConfirmReloadWhenDirty())
            {
                return;
            }

            if (TrySwitchToImageEditorFromClipboard())
            {
                return;
            }

            if (!TryGetClipboardText(out var text))
            {
                MessageBox.Show(this, "Clipboard does not contain text to reload.", WindowTitleBase, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            LoadText(text);
        }

        private bool TrySwitchToImageEditorFromClipboard()
        {
            if (RequestImageEditor == null)
            {
                return false;
            }

            Image? clipboardImage = null;
            try
            {
                if (Clipboard.ContainsImage())
                {
                    clipboardImage = Clipboard.GetImage();
                }
            }
            catch (ExternalException ex)
            {
                MessageBox.Show(this, $"Failed to access the clipboard.\n{ex.Message}", WindowTitleBase, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return true;
            }

            if (clipboardImage == null)
            {
                return false;
            }

            var imageEditor = RequestImageEditor?.Invoke();
            if (imageEditor == null)
            {
                clipboardImage.Dispose();
                return false;
            }

            imageEditor.AdoptWindowGeometry(this);
            using (clipboardImage)
            {
                imageEditor.LoadImage(clipboardImage);
            }

            imageEditor.ShowAndFocus();

            ClearClipboardNotification(clearSnapshot: true);
            if (Visible)
            {
                Hide();
            }
            return true;
        }

        private bool ConfirmReloadWhenDirty()
        {
            if (!isDirty)
            {
                return true;
            }

            var result = MessageBox.Show(this, "Reloading from the clipboard will discard unsaved changes. Continue?", WindowTitleBase, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            return result == DialogResult.Yes;
        }

        private void ConfigureKeywords()
        {
            editor.SetKeywords(0, PrimaryKeywords);
            editor.SetKeywords(1, TypeKeywords);
            editor.SetKeywords(2, PreprocessorKeywords);
            editor.SetProperty("styling.within.preprocessor", "1");
        }

        private void InitializeThemeConfiguration()
        {
            themeFilePath = EnsureThemeFile();
            currentTheme = LoadThemeFromDisk(themeFilePath);
        }

        private string EnsureThemeFile()
        {
            var localFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Screenzap");
            Directory.CreateDirectory(localFolder);
            var destination = Path.Combine(localFolder, ThemeFileName);

            if (!File.Exists(destination))
            {
                var bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ThemeFileName);
                if (File.Exists(bundled))
                {
                    try
                    {
                        File.Copy(bundled, destination, overwrite: false);
                    }
                    catch (IOException)
                    {
                        // Another instance may have already created the file.
                    }
                }
                else
                {
                    WriteThemeFile(destination, TextEditorTheme.CreateDefault());
                }
            }

            return destination;
        }

        private void ConfigureThemeWatcher()
        {
            if (string.IsNullOrWhiteSpace(themeFilePath))
            {
                return;
            }

            DisposeThemeWatcher();

            var directory = Path.GetDirectoryName(themeFilePath);
            var fileName = Path.GetFileName(themeFilePath);
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
            {
                return;
            }

            themeWatcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime
            };

            themeWatcher.Changed += ThemeWatcherOnChanged;
            themeWatcher.Created += ThemeWatcherOnChanged;
            themeWatcher.Renamed += ThemeWatcherOnRenamed;
            themeWatcher.Deleted += ThemeWatcherOnDeleted;
            themeWatcher.EnableRaisingEvents = true;
        }

        private void ThemeWatcherOnChanged(object sender, FileSystemEventArgs e)
        {
            if (!IsThemeFileEvent(e.FullPath))
            {
                return;
            }

            ScheduleThemeReload();
        }

        private void ThemeWatcherOnRenamed(object sender, RenamedEventArgs e)
        {
            if (IsThemeFileEvent(e.FullPath))
            {
                ScheduleThemeReload();
                return;
            }

            if (IsThemeFileEvent(e.OldFullPath) && !string.IsNullOrWhiteSpace(themeFilePath))
            {
                WriteThemeFile(themeFilePath, TextEditorTheme.CreateDefault());
                ScheduleThemeReload();
            }
        }

        private void ThemeWatcherOnDeleted(object sender, FileSystemEventArgs e)
        {
            if (!IsThemeFileEvent(e.FullPath) || string.IsNullOrWhiteSpace(themeFilePath))
            {
                return;
            }

            WriteThemeFile(themeFilePath, TextEditorTheme.CreateDefault());
            ScheduleThemeReload();
        }

        private bool IsThemeFileEvent(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(themeFilePath))
            {
                return false;
            }

            return string.Equals(Path.GetFullPath(path), Path.GetFullPath(themeFilePath), StringComparison.OrdinalIgnoreCase);
        }

        private void ScheduleThemeReload()
        {
            if (string.IsNullOrWhiteSpace(themeFilePath))
            {
                return;
            }

            if (Interlocked.Exchange(ref themeReloadScheduled, 1) == 1)
            {
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(150).ConfigureAwait(false);
                    var updatedTheme = LoadThemeFromDisk(themeFilePath);
                    if (IsDisposed)
                    {
                        return;
                    }

                    BeginInvoke(new Action(() => ApplyTheme(updatedTheme)));
                }
                catch (ObjectDisposedException)
                {
                    // Form closed before invocation.
                }
                finally
                {
                    Interlocked.Exchange(ref themeReloadScheduled, 0);
                }
            });
        }

        private TextEditorTheme LoadThemeFromDisk(string path)
        {
            if (!File.Exists(path))
            {
                WriteThemeFile(path, TextEditorTheme.CreateDefault());
            }

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var theme = JsonSerializer.Deserialize<TextEditorTheme>(stream, themeSerializerOptions);
                    return theme ?? TextEditorTheme.CreateDefault();
                }
                catch (IOException)
                {
                    Thread.Sleep(50);
                }
                catch (UnauthorizedAccessException)
                {
                    break;
                }
                catch (JsonException)
                {
                    break;
                }
            }

            return TextEditorTheme.CreateDefault();
        }

        private void WriteThemeFile(string path, TextEditorTheme theme)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(theme, themeSerializerOptions);
            try
            {
                File.WriteAllText(path, json);
            }
            catch (IOException)
            {
                // Ignore temporary write failures.
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore permission issues; theme will fall back to defaults.
            }
        }

        private void ApplyTheme(TextEditorTheme? theme)
        {
            if (editor == null)
            {
                return;
            }

            currentTheme = theme ?? TextEditorTheme.CreateDefault();
            var defaults = TextEditorTheme.CreateDefault();

            var background = ParseColor(currentTheme.Background, Color.FromArgb(32, 32, 32));
            var foreground = ParseColor(currentTheme.Foreground, Color.Gainsboro);
            var caretLine = ParseColor(currentTheme.CaretLine, Color.FromArgb(0x2c, 0x2c, 0x2c));
            var caret = ParseColor(currentTheme.Caret, Color.Gainsboro);
            var selection = ParseColor(currentTheme.Selection, Color.FromArgb(0x26, 0x4f, 0x78));
            var lineNumberForeground = ParseColor(currentTheme.LineNumberForeground, Color.Gainsboro);
            var lineNumberBackground = ParseColor(currentTheme.LineNumberBackground, Color.FromArgb(45, 45, 48));
            var keyword = ParseColor(currentTheme.Keyword, Color.FromArgb(0x56, 0x9c, 0xd6));
            var type = ParseColor(currentTheme.Type, Color.FromArgb(0x4e, 0xc9, 0xb0));
            var str = ParseColor(currentTheme.String, Color.FromArgb(0xd6, 0x9d, 0x85));
            var comment = ParseColor(currentTheme.Comment, Color.FromArgb(0x6a, 0x99, 0x55));
            var number = ParseColor(currentTheme.Number, Color.FromArgb(0xb5, 0xce, 0xa8));
            var op = ParseColor(currentTheme.Operator, Color.FromArgb(0xd4, 0xd4, 0xd4));
            var preprocessor = ParseColor(currentTheme.Preprocessor, Color.FromArgb(0xc5, 0x86, 0xc0));
            var fontFamily = string.IsNullOrWhiteSpace(currentTheme.FontFamily)
                ? (defaults.FontFamily ?? "Consolas")
                : currentTheme.FontFamily!;
            var fontSize = currentTheme.FontSize <= 0 ? defaults.FontSize : currentTheme.FontSize;

            editor.StyleResetDefault();
            editor.Styles[StyleConstants.Default].Font = fontFamily;
            editor.Styles[StyleConstants.Default].Size = (int)Math.Round(fontSize);
            editor.Styles[StyleConstants.Default].BackColor = background;
            editor.Styles[StyleConstants.Default].ForeColor = foreground;
            editor.StyleClearAll();
            editor.CaretLineBackColor = caretLine;
            editor.CaretForeColor = caret;
            editor.SetSelectionBackColor(true, selection);
            editor.Margins[0].BackColor = lineNumberBackground;
            editor.Styles[StyleConstants.LineNumber].ForeColor = lineNumberForeground;
            editor.Styles[StyleConstants.LineNumber].BackColor = lineNumberBackground;

            ApplyCppStyleColors(keyword, type, str, comment, number, op, preprocessor, background);
        }

        private void ApplyCppStyleColors(Color keyword, Color type, Color str, Color comment, Color number, Color op, Color preprocessor, Color background)
        {
            void StyleRange(Color color, params int[] styles)
            {
                foreach (var style in styles)
                {
                    editor.Styles[style].ForeColor = color;
                }
            }

            StyleRange(comment,
                CppStyles.Comment,
                CppStyles.CommentLine,
                CppStyles.CommentDoc,
                CppStyles.CommentLineDoc,
                CppStyles.CommentDocKeyword,
                CppStyles.CommentDocKeywordError,
                CppStyles.PreprocessorComment,
                CppStyles.PreprocessorCommentDoc,
                CppStyles.TaskMarker);

            StyleRange(number, CppStyles.Number, CppStyles.UserLiteral);
            StyleRange(keyword, CppStyles.Word);
            StyleRange(type, CppStyles.Word2, CppStyles.GlobalClass);
            StyleRange(str,
                CppStyles.String,
                CppStyles.Character,
                CppStyles.StringEol,
                CppStyles.Verbatim,
                CppStyles.Regex,
                CppStyles.RawString,
                CppStyles.HashQuotedString);
            StyleRange(op, CppStyles.Operator);
            StyleRange(preprocessor, CppStyles.Preprocessor);

            foreach (var style in new[] { CppStyles.StringEol, CppStyles.RawString, CppStyles.HashQuotedString })
            {
                editor.Styles[style].BackColor = background;
            }
        }

        private static Color ParseColor(string? value, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            try
            {
                return ColorTranslator.FromHtml(value);
            }
            catch
            {
                return fallback;
            }
        }

        private void DisposeThemeWatcher()
        {
            if (themeWatcher == null)
            {
                return;
            }

            themeWatcher.EnableRaisingEvents = false;
            themeWatcher.Changed -= ThemeWatcherOnChanged;
            themeWatcher.Created -= ThemeWatcherOnChanged;
            themeWatcher.Renamed -= ThemeWatcherOnRenamed;
            themeWatcher.Deleted -= ThemeWatcherOnDeleted;
            themeWatcher.Dispose();
            themeWatcher = null;
        }

        private void Editor_TextChanged(object? sender, EventArgs e)
        {
            if (suppressTextChanged)
            {
                return;
            }

            isDirty = true;
            UpdateWindowTitle();
            UpdateCommandStates();
            UpdateStatusLabels();
        }

        private void Editor_UpdateUI(object? sender, UpdateUIEventArgs e)
        {
            if ((e.Change & UpdateChange.Selection) != 0 || (e.Change & UpdateChange.Content) != 0)
            {
                UpdateStatusLabels();
            }
        }

        private void UpdateStatusLabels()
        {
            if (editor == null)
            {
                return;
            }

            var caretPos = editor.CurrentPosition;
            var line = editor.LineFromPosition(caretPos) + 1;
            var column = caretPos - editor.Lines[line - 1].Position + 1;
            var selectionLength = Math.Abs(editor.SelectionEnd - editor.SelectionStart);
            caretStatusLabel.Text = $"Ln {line:N0}, Col {column:N0}";
            documentStatusLabel.Text = $"Len {editor.TextLength:N0}, Sel {selectionLength:N0}";
        }

        private void UpdateWindowTitle()
        {
            string title = WindowTitleBase;
            if (!string.IsNullOrWhiteSpace(currentSavePath))
            {
                title += $" - {Path.GetFileName(currentSavePath)}";
            }

            if (isDirty)
            {
                title += "*";
            }

            Text = title;
        }

        private void UpdateCommandStates()
        {
            bool hasContent = editor?.TextLength > 0;
            saveToolStripButton.Enabled = isDirty;
            saveAsToolStripButton.Enabled = hasContent;
            copyToolStripButton.Enabled = hasContent;
        }

        private void saveToolStripButton_Click(object? sender, EventArgs e)
        {
            SaveDocument();
        }

        private void saveAsToolStripButton_Click(object? sender, EventArgs e)
        {
            SaveDocument(promptForPath: true);
        }

        private void copyToolStripButton_Click(object? sender, EventArgs e)
        {
            CopyToClipboard();
        }

        private void reloadToolStripButton_Click(object? sender, EventArgs e)
        {
            ReloadFromClipboard();
        }

        private void findToolStripButton_Click(object? sender, EventArgs e)
        {
            ToggleSearchPanel(show: true, focusReplace: false);
        }

        private void TextEditor_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.F)
            {
                ToggleSearchPanel(show: true, focusReplace: false);
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.H)
            {
                ToggleSearchPanel(show: true, focusReplace: true);
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.F3)
            {
                ExecuteSearch(backwards: e.Shift);
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape && searchPanel.Visible)
            {
                ToggleSearchPanel(show: false);
                e.SuppressKeyPress = true;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.S))
            {
                SaveDocument();
                return true;
            }

            if (keyData == (Keys.Control | Keys.Shift | Keys.S))
            {
                SaveDocument(promptForPath: true);
                return true;
            }

            if (keyData == (Keys.Control | Keys.Shift | Keys.C))
            {
                CopyToClipboard();
                return true;
            }

            if (keyData == (Keys.Control | Keys.R))
            {
                ReloadFromClipboard();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void CopyToClipboard()
        {
            if (editor == null)
            {
                return;
            }

            var text = editor.Text ?? string.Empty;
            try
            {
                ignoreNextClipboardUpdate = true;
                Clipboard.SetText(text);
                ClipboardMetadata.LastTextCaptureTimestamp = DateTime.Now;
                pendingClipboardText = text;
                ClearClipboardNotification(clearSnapshot: false);
            }
            catch (ExternalException ex)
            {
                ignoreNextClipboardUpdate = false;
                MessageBox.Show(this, $"Failed to copy text to the clipboard.\n{ex.Message}", WindowTitleBase, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ToggleSearchPanel(bool show, bool focusReplace = false)
        {
            searchPanel.Visible = show;
            if (show)
            {
                searchMessageLabel.Text = string.Empty;
                searchMessageLabel.ForeColor = SystemColors.ControlText;
                var target = focusReplace ? replaceTextBox : findTextBox;
                target.Focus();
                target.SelectAll();
            }
            else
            {
                FocusEditor();
            }
        }

        private void closeSearchPanelButton_Click(object? sender, EventArgs e)
        {
            ToggleSearchPanel(show: false);
        }

        private void findTextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                ExecuteSearch(backwards: e.Shift);
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                ToggleSearchPanel(show: false);
                e.SuppressKeyPress = true;
            }
        }

        private void replaceTextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                ReplaceSelection();
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                ToggleSearchPanel(show: false);
                e.SuppressKeyPress = true;
            }
        }

        private void findNextButton_Click(object? sender, EventArgs e)
        {
            ExecuteSearch(backwards: false);
        }

        private void findPreviousButton_Click(object? sender, EventArgs e)
        {
            ExecuteSearch(backwards: true);
        }

        private void replaceButton_Click(object? sender, EventArgs e)
        {
            ReplaceSelection();
        }

        private void replaceAllButton_Click(object? sender, EventArgs e)
        {
            ReplaceAll();
        }

        private void ExecuteSearch(bool backwards)
        {
            if (!TryFind(backwards, out var start, out var length, reportErrors: true))
            {
                return;
            }

            editor.SetSelection(start + length, start);
            editor.ScrollCaret();
        }

        private bool TryFind(bool backwards, out int start, out int length, bool reportErrors)
        {
            start = -1;
            length = 0;

            var needle = findTextBox.Text;
            if (string.IsNullOrEmpty(needle))
            {
                if (reportErrors)
                {
                    searchMessageLabel.Text = "Enter text to search.";
                    searchMessageLabel.ForeColor = Color.Firebrick;
                }
                return false;
            }

            try
            {
                var flags = BuildSearchFlags();
                editor.SearchFlags = flags;

                if (!backwards)
                {
                    if (SearchForward(Math.Max(editor.SelectionStart, editor.SelectionEnd), editor.TextLength, needle, out start, out length))
                    {
                        return true;
                    }

                    return SearchForward(0, editor.TextLength, needle, out start, out length);
                }
                else
                {
                    if (SearchBackward(0, Math.Min(editor.SelectionStart, editor.SelectionEnd), needle, out start, out length))
                    {
                        return true;
                    }

                    return SearchBackward(0, editor.TextLength, needle, out start, out length);
                }
            }
            catch (ArgumentException ex)
            {
                if (reportErrors)
                {
                    searchMessageLabel.Text = ex.Message;
                    searchMessageLabel.ForeColor = Color.Firebrick;
                }
                return false;
            }
        }

        private SearchFlags BuildSearchFlags()
        {
            var flags = SearchFlags.None;
            if (matchCaseCheckBox.Checked)
            {
                flags |= SearchFlags.MatchCase;
            }

            if (wholeWordCheckBox.Checked)
            {
                flags |= SearchFlags.WholeWord;
            }

            if (regexCheckBox.Checked)
            {
                flags |= SearchFlags.Regex;
            }

            return flags;
        }

        private bool SearchForward(int rangeStart, int rangeEnd, string pattern, out int start, out int length)
        {
            editor.TargetStart = rangeStart;
            editor.TargetEnd = rangeEnd;
            var result = editor.SearchInTarget(pattern);
            if (result == -1)
            {
                start = -1;
                length = 0;
                return false;
            }

            start = editor.TargetStart;
            length = Math.Max(1, editor.TargetEnd - editor.TargetStart);
            return true;
        }

        private bool SearchBackward(int rangeStart, int rangeEnd, string pattern, out int start, out int length)
        {
            start = -1;
            length = 0;
            if (rangeEnd <= rangeStart)
            {
                return false;
            }
            int searchOffset = rangeStart;
            while (searchOffset < rangeEnd)
            {
                editor.TargetStart = searchOffset;
                editor.TargetEnd = rangeEnd;
                var result = editor.SearchInTarget(pattern);
                if (result == -1)
                {
                    break;
                }

                start = editor.TargetStart;
                length = Math.Max(1, editor.TargetEnd - editor.TargetStart);
                searchOffset = start + length;
            }

            return start >= 0;
        }

        private bool SelectionMatchesSearch(out int start, out int length)
        {
            start = editor.SelectionStart;
            length = editor.SelectionEnd - editor.SelectionStart;
            if (length <= 0)
            {
                return false;
            }

            editor.TargetStart = start;
            editor.TargetEnd = editor.SelectionEnd;
            var flags = BuildSearchFlags();
            editor.SearchFlags = flags;
            var needle = findTextBox.Text;
            if (string.IsNullOrEmpty(needle))
            {
                return false;
            }

            try
            {
                var result = editor.SearchInTarget(needle);
                if (result == -1)
                {
                    return false;
                }

                if (editor.TargetStart != start)
                {
                    return false;
                }

                length = Math.Max(1, editor.TargetEnd - editor.TargetStart);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private void ReplaceSelection()
        {
            var replacement = replaceTextBox.Text ?? string.Empty;
            if (!SelectionMatchesSearch(out var start, out var length))
            {
                if (TryFind(backwards: false, out start, out length, reportErrors: true))
                {
                    editor.SetSelection(start + length, start);
                }
                return;
            }

            editor.TargetStart = start;
            editor.TargetEnd = start + length;
            editor.ReplaceTarget(replacement);
            editor.SetSelection(editor.TargetEnd, editor.TargetEnd);
            isDirty = true;
            UpdateWindowTitle();
            UpdateCommandStates();
        }

        private void ReplaceAll()
        {
            var needle = findTextBox.Text;
            if (string.IsNullOrEmpty(needle))
            {
                searchMessageLabel.Text = "Enter text to replace.";
                searchMessageLabel.ForeColor = Color.Firebrick;
                return;
            }

            var replacement = replaceTextBox.Text ?? string.Empty;
            int replacedCount = 0;
            int searchOffset = 0;
            editor.BeginUndoAction();
            try
            {
                var flags = BuildSearchFlags();
                editor.SearchFlags = flags;
                while (searchOffset <= editor.TextLength)
                {
                    editor.TargetStart = searchOffset;
                    editor.TargetEnd = editor.TextLength;
                    var result = editor.SearchInTarget(needle);
                    if (result == -1)
                    {
                        break;
                    }

                    var length = Math.Max(1, editor.TargetEnd - editor.TargetStart);
                    editor.ReplaceTarget(replacement);
                    replacedCount++;
                    searchOffset = editor.TargetEnd;
                }
            }
            catch (ArgumentException ex)
            {
                searchMessageLabel.Text = ex.Message;
                searchMessageLabel.ForeColor = Color.Firebrick;
                return;
            }
            finally
            {
                editor.EndUndoAction();
            }

            if (replacedCount > 0)
            {
                isDirty = true;
                UpdateWindowTitle();
                UpdateCommandStates();
            }

            searchMessageLabel.ForeColor = Color.ForestGreen;
            searchMessageLabel.Text = replacedCount == 1 ? "Replaced 1 occurrence." : $"Replaced {replacedCount} occurrences.";
        }

        private void SaveDocument(bool promptForPath = false)
        {
            if (editor == null)
            {
                return;
            }

            if (promptForPath || string.IsNullOrWhiteSpace(currentSavePath))
            {
                using var dialog = new SaveFileDialog
                {
                    Filter = "Text files|*.txt;*.md;*.log;*.json;*.xml|All files|*.*",
                    DefaultExt = "txt",
                    AddExtension = true,
                    FileName = Path.GetFileName(FileUtils.BuildCapturePath(".txt")),
                    InitialDirectory = FileUtils.GetCaptureFolderPath()
                };

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                currentSavePath = dialog.FileName;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(currentSavePath!)!);
                File.WriteAllText(currentSavePath!, editor.Text, utf8NoBom);
                isDirty = false;
                editor.SetSavePoint();
                ClipboardMetadata.LastTextCaptureTimestamp = DateTime.Now;
                UpdateWindowTitle();
                UpdateCommandStates();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to save file.\n{ex.Message}", "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void TextEditor_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (!PromptForUnsavedChanges())
            {
                e.Cancel = true;
            }
        }

        private bool PromptForUnsavedChanges()
        {
            if (!isDirty)
            {
                return true;
            }

            var result = MessageBox.Show(this, "You have unsaved changes. Save before closing?", WindowTitleBase, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (result == DialogResult.Cancel)
            {
                return false;
            }

            if (result == DialogResult.Yes)
            {
                SaveDocument();
                return !isDirty;
            }

            return true;
        }

        private sealed class TextEditorTheme
        {
            public string? FontFamily { get; set; } = "Consolas";
            public double FontSize { get; set; } = 11;
            public string? Background { get; set; } = "#202020";
            public string? Foreground { get; set; } = "#dcdcdc";
            public string? CaretLine { get; set; } = "#2c2c2c";
            public string? Caret { get; set; } = "#f4f4f4";
            public string? Selection { get; set; } = "#264f78";
            public string? LineNumberForeground { get; set; } = "#858585";
            public string? LineNumberBackground { get; set; } = "#2d2d30";
            public string? Keyword { get; set; } = "#569cd6";
            public string? Type { get; set; } = "#4ec9b0";
            public string? String { get; set; } = "#d69d85";
            public string? Comment { get; set; } = "#6a9955";
            public string? Number { get; set; } = "#b5cea8";
            public string? Operator { get; set; } = "#d4d4d4";
            public string? Preprocessor { get; set; } = "#c586c0";

            public static TextEditorTheme CreateDefault() => new TextEditorTheme();
        }
    }
}
