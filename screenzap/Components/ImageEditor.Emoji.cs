using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FontAwesome.Sharp;

namespace screenzap;

public partial class ImageEditor
{
    private IconToolStripButton? emojiToolStripButton;
    private EmojiFlyout? emojiFlyout;
    private EmojiRecentStore emojiRecentStore = new();
    private TextBox? emojiPickerInput;
    private System.Windows.Forms.Timer? emojiPickerTimer;
    private bool emojiPickerPending;

    private void InitializeEmojiTool()
    {
        emojiToolStripButton = new IconToolStripButton
        {
            Name = "emojiToolStripButton", Text = "Emoji", ToolTipText = "Drag emoji onto the image",
            Enabled = false
        };
        ConfigureIconButton(emojiToolStripButton, IconChar.FaceSmile);
        emojiToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
        emojiToolStripButton.AutoSize = false;
        emojiToolStripButton.Size = new Size(32, 32);
        emojiToolStripButton.Margin = new Padding(2);
        emojiToolStripButton.Padding = Padding.Empty;
        toolsToolStrip!.Items.Insert(toolsToolStrip.Items.IndexOf(textToolStripButton) + 1, emojiToolStripButton);
        emojiToolStripButton.Click += (_, _) => ToggleEmojiFlyout();
        pictureBox1.DragEnter += EmojiDragEnter;
        pictureBox1.DragOver += EmojiDragEnter;
        pictureBox1.DragDrop += EmojiDragDrop;
        VisibleChanged += (_, _) => { if (!Visible) CloseEmojiUi(); };
    }

    private void ToggleEmojiFlyout()
    {
        if (!HasEditableImage) return;
        if (emojiFlyout?.Visible == true) { CloseEmojiUi(); return; }
        emojiFlyout ??= CreateEmojiFlyout();
        emojiFlyout.SetEmoji(emojiRecentStore.Tiles);
        var anchor = toolsToolStrip!.PointToScreen(new Point(toolsToolStrip.Width, emojiToolStripButton!.Bounds.Top));
        var workingArea = Screen.FromPoint(anchor).WorkingArea;
        emojiFlyout.Location = new Point(
            Math.Clamp(anchor.X, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - emojiFlyout.Width)),
            Math.Clamp(anchor.Y, workingArea.Top, Math.Max(workingArea.Top, workingArea.Bottom - emojiFlyout.Height)));
        var owner = TopLevelControl as Form;
        if (owner != null) emojiFlyout.Show(owner);
        else emojiFlyout.Show();
        emojiToolStripButton.Checked = true;
    }

    private EmojiFlyout CreateEmojiFlyout()
    {
        var flyout = new EmojiFlyout();
        flyout.MoreRequested += OpenWindowsEmojiPicker;
        return flyout;
    }

    private void CloseEmojiUi()
    {
        EndEmojiPicker();
        emojiFlyout?.Hide();
        if (emojiToolStripButton != null) emojiToolStripButton.Checked = false;
    }

    private void DisposeEmojiUi()
    {
        emojiPickerPending = false;
        emojiPickerTimer?.Dispose();
        emojiPickerInput?.Dispose();
        emojiFlyout?.Dispose();
    }

    private void EmojiDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(EmojiFlyout.DragFormat) == true)
            e.Effect = HasEditableImage && e.Data.GetData(EmojiFlyout.DragFormat) is string emoji
                && EmojiRecentStore.IsEmoji(emoji) && (e.AllowedEffect & DragDropEffects.Copy) != 0
                    ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void EmojiDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(EmojiFlyout.DragFormat) is not string emoji) return;
        e.Effect = AddEmojiAtClientPoint(emoji, pictureBox1.PointToClient(new Point(e.X, e.Y)))
            ? DragDropEffects.Copy : DragDropEffects.None;
        RequestCanvasFocus();
    }

    internal bool AddEmojiAtClientPoint(string emoji, Point clientPoint)
    {
        if (!HasEditableImage || !EmojiRecentStore.IsEmoji(emoji)) return false;
        SetActiveTool(ActiveTool.None);
        FinalizeActiveTextAnnotation();
        var before = CloneTextAnnotations();
        var annotation = new TextAnnotation
        {
            Text = emoji, FontFamily = "Segoe UI Emoji", FontSize = 72f,
            FontStyle = FontStyle.Regular, TextColor = Color.Black, OutlineThickness = 0f,
            CaretPosition = emoji.Length
        };
        using (var graphics = pictureBox1.CreateGraphics())
        {
            var bounds = annotation.GetBounds(graphics);
            var center = FormCoordToPixel(clientPoint);
            annotation.Position = new Point(center.X - bounds.Width / 2, center.Y - bounds.Height / 2);
        }
        SelectAnnotation(null);
        textAnnotations.Add(annotation);
        SelectTextAnnotation(annotation);
        PushTextUndoStep(before, CloneTextAnnotations());
        emojiRecentStore.Record(emoji);
        emojiFlyout?.SetEmoji(emojiRecentStore.Tiles);
        pictureBox1.Invalidate();
        return true;
    }

    private void OpenWindowsEmojiPicker()
    {
        if (!BeginEmojiPickerCapture()) return;
        if (!emojiPickerInput!.Focus() || !EmojiPickerNative.Open()) EndEmojiPicker();
    }

    private bool BeginEmojiPickerCapture()
    {
        if (!HasEditableImage) return false;
        EndEmojiPicker();
        // The native picker inserts Unicode into the focused edit control. No document
        // object is created until actual emoji arrive; Escape/blur with empty input is inert.
        emojiPickerInput ??= CreateEmojiPickerInput();
        emojiPickerPending = true;
        emojiPickerInput.Location = new Point(pictureBox1.ClientSize.Width / 2, pictureBox1.ClientSize.Height / 2);
        emojiPickerInput.Clear();
        emojiPickerInput.Show();
        return true;
    }

    private TextBox CreateEmojiPickerInput()
    {
        var input = new TextBox
        {
            Name = "emojiPickerInput", BorderStyle = BorderStyle.None, AutoSize = false,
            Size = new Size(1, 1), TabStop = false, Visible = false, ShortcutsEnabled = false
        };
        pictureBox1.Controls.Add(input);
        emojiPickerTimer = new System.Windows.Forms.Timer { Interval = 150 };
        emojiPickerTimer.Tick += (_, _) => CommitEmojiPickerInput();
        input.TextChanged += (_, _) =>
        {
            if (!emojiPickerPending) return;
            // Batch WM_CHAR messages so surrogate pairs, modifiers and ZWJ sequences
            // are complete before making a text object.
            emojiPickerTimer.Stop();
            emojiPickerTimer.Start();
        };
        input.LostFocus += (_, _) => { CommitEmojiPickerInput(); EndEmojiPicker(); };
        input.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape) return;
            EndEmojiPicker();
            RequestCanvasFocus();
            e.SuppressKeyPress = true;
        };
        return input;
    }

    internal void CommitEmojiPickerInput()
    {
        emojiPickerTimer?.Stop();
        if (!emojiPickerPending || emojiPickerInput == null) return;
        var elements = StringInfo.GetTextElementEnumerator(emojiPickerInput.Text);
        while (elements.MoveNext())
        {
            var emoji = elements.GetTextElement();
            if (EmojiRecentStore.IsEmoji(emoji))
                AddEmojiAtClientPoint(emoji, new Point(pictureBox1.ClientSize.Width / 2, pictureBox1.ClientSize.Height / 2));
        }
        // Keep the input target alive while Windows allows further emoji selections.
        emojiPickerInput.Clear();
        emojiPickerTimer?.Stop();
    }

    private void EndEmojiPicker()
    {
        emojiPickerPending = false;
        emojiPickerTimer?.Stop();
        emojiPickerInput?.Clear();
        emojiPickerInput?.Hide();
    }

    private static class EmojiPickerNative
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput
        {
            public ushort Key, Scan;
            public uint Flags, Time;
            public UIntPtr ExtraInfo;
        }
        // INPUT's union includes MOUSEINPUT (32 bytes on x64).
        [StructLayout(LayoutKind.Explicit, Size = 40)]
        private struct Input
        {
            [FieldOffset(0)] public uint Type;
            [FieldOffset(8)] public KeyboardInput Keyboard;
        }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, Input[] inputs, int size);
        private static Input Key(ushort key, bool up = false) => new()
        {
            Type = 1, Keyboard = new KeyboardInput { Key = key, Flags = up ? 2u : 0u }
        };
        internal static bool Open()
        {
            var inputs = new[] { Key(0x5B), Key(0xBE), Key(0xBE, true), Key(0x5B, true) };
            return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length;
        }
    }
}
