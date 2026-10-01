using System.Drawing;
using System.Windows.Forms;

namespace screenzap;

// A non-activating owned window keeps the canvas usable during repeated drags.
internal sealed class EmojiFlyout : Form
{
    internal const string DragFormat = "Screenzap.Emoji";
    private readonly EmojiTile[] tiles = new EmojiTile[8];
    internal event Action? MoreRequested;

    internal EmojiFlyout()
    {
        Name = "emojiFlyout";
        Text = "Emoji";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(288, 288);
        BackColor = Color.FromArgb(245, 245, 245);
        MaximizeBox = MinimizeBox = ControlBox = false;
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, Padding = new Padding(3) };
        for (int i = 0; i < 3; i++)
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 3));
        }
        for (int i = 0; i < 9; i++)
        {
            var tile = new EmojiTile { Dock = DockStyle.Fill, Margin = new Padding(2), IsMore = i == 8, TabStop = false };
            if (i < 8) tiles[i] = tile;
            else
            {
                tile.AccessibleName = "More emoji — Windows emoji picker";
                tile.Click += (_, _) => MoreRequested?.Invoke();
            }
            grid.Controls.Add(tile, i % 3, i / 3);
        }
        Controls.Add(grid);
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            return cp;
        }
    }

    internal void SetEmoji(string[] emoji)
    {
        for (int i = 0; i < tiles.Length; i++) tiles[i].Emoji = emoji[i];
    }

    private sealed class EmojiTile : Control
    {
        private string emoji = string.Empty;
        private Point? dragStart;
        private bool hovered;
        internal bool IsMore { get; init; }
        internal string Emoji
        {
            set { emoji = value; AccessibleName = value; Invalidate(); }
        }

        internal EmojiTile()
        {
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(hovered ? Color.FromArgb(225, 235, 250) : Color.White);
            string text = IsMore ? "…" : emoji;
            float size = (IsMore ? 48f : 64f) * DeviceDpi / 96f;
            var measured = EmojiTextRenderer.MeasureText(e.Graphics, text, "Segoe UI Emoji", size, FontStyle.Regular);
            EmojiTextRenderer.DrawText(e.Graphics, text,
                new PointF((Width - measured.Width) / 2, (Height - measured.Height) / 2),
                Color.Black, "Segoe UI Emoji", size, FontStyle.Regular);
            base.OnPaint(e);
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (!IsMore && e.Button == MouseButtons.Left) dragStart = e.Location;
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.Button != MouseButtons.Left || !dragStart.HasValue) return;
            var threshold = new Rectangle(dragStart.Value, Size.Empty);
            threshold.Inflate(SystemInformation.DragSize.Width / 2, SystemInformation.DragSize.Height / 2);
            if (threshold.Contains(e.Location)) return;
            dragStart = null;
            var data = new DataObject();
            data.SetData(DragFormat, emoji);
            DoDragDrop(data, DragDropEffects.Copy);
        }
        protected override void OnMouseUp(MouseEventArgs e) { dragStart = null; base.OnMouseUp(e); }
    }
}
