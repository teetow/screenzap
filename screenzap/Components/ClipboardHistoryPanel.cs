using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FontAwesome.Sharp;

namespace screenzap.Components
{
    /// <summary>
    /// Right-side panel that displays clipboard history thumbnails as a vertical list.
    /// Raises <see cref="ItemActivated"/> when the user clicks a thumbnail.
    /// </summary>
    internal sealed class ClipboardHistoryPanel : Panel
    {
        private readonly FlowLayoutPanel flow;
        private readonly ToolTip tooltip = new ToolTip();
        private readonly Dictionary<Guid, ThumbnailButton> buttons = new();
        private ClipboardHistoryStore? store;

        public event EventHandler<ClipboardHistoryItem>? ItemActivated;
        public event EventHandler<ClipboardHistoryItem>? ItemSetActive;
        public event EventHandler<ClipboardHistoryItem>? ItemDuplicate;
        public event EventHandler<ClipboardHistoryItem>? ItemRevert;
        public event EventHandler<ClipboardHistoryItem>? ItemDelete;

        public ClipboardHistoryPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(24, 24, 28);
            Width = 93;
            Padding = Padding.Empty;

            flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = new Padding(4, 4, 17, 4)
            };
            Controls.Add(flow);
        }

        public void AttachStore(ClipboardHistoryStore store)
        {
            if (this.store != null)
            {
                this.store.Changed -= OnStoreChanged;
                this.store.ItemUpdated -= OnItemUpdated;
            }

            this.store = store;
            store.Changed += OnStoreChanged;
            store.ItemUpdated += OnItemUpdated;
            RebuildAll();
        }

        private void OnStoreChanged(object? sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(RebuildAll));
                return;
            }
            RebuildAll();
        }

        private void OnItemUpdated(object? sender, ClipboardHistoryItem item)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<ClipboardHistoryItem>(RefreshItem), item);
                return;
            }
            RefreshItem(item);
        }

        private void RefreshItem(ClipboardHistoryItem item)
        {
            if (buttons.TryGetValue(item.Id, out var btn))
            {
                btn.Rebind(item, ReferenceEquals(store?.ActiveItem, item));
                btn.Invalidate();
            }
        }

        private void RebuildAll()
        {
            if (store == null) return;

            flow.SuspendLayout();
            try
            {
                var existingIds = buttons.Keys.ToHashSet();
                var liveIds = new HashSet<Guid>();

                flow.Controls.Clear();

                foreach (var item in store.Items)
                {
                    liveIds.Add(item.Id);
                    if (!buttons.TryGetValue(item.Id, out var btn))
                    {
                        btn = new ThumbnailButton();
                        btn.Click += (s, e) =>
                        {
                            if (btn.Item != null)
                                ItemActivated?.Invoke(this, btn.Item);
                        };
                        btn.OnSetActive  = i => ItemSetActive?.Invoke(this, i);
                        btn.OnDuplicate  = i => ItemDuplicate?.Invoke(this, i);
                        btn.OnRevert     = i => ItemRevert?.Invoke(this, i);
                        btn.OnDelete     = i => ItemDelete?.Invoke(this, i);
                        buttons[item.Id] = btn;
                    }

                    btn.Rebind(item, ReferenceEquals(store.ActiveItem, item));
                    tooltip.SetToolTip(btn, BuildToolTip(item));
                    flow.Controls.Add(btn);
                }

                // Dispose buttons for removed items.
                foreach (var goneId in existingIds.Except(liveIds).ToList())
                {
                    if (buttons.TryGetValue(goneId, out var orphan))
                    {
                        buttons.Remove(goneId);
                        orphan.Dispose();
                    }
                }
            }
            finally
            {
                flow.ResumeLayout();
            }
        }

        private static string BuildToolTip(ClipboardHistoryItem item)
        {
            var kindLabel = item.Kind == ClipboardItemKind.Image ? "Image" : "Text";
            var dirty = item.IsDirty ? " (edited)" : string.Empty;
            return $"{kindLabel} - {item.CreatedUtc.ToLocalTime():HH:mm:ss}{dirty}";
        }

        private sealed class ThumbnailButton : Control
        {
            private static readonly Color ActiveBorder = Color.DeepSkyBlue;
            private static readonly Color IdleBorder = Color.FromArgb(70, 70, 75);
            private bool isActive;
            private readonly ContextMenuStrip menu;
            private readonly ToolStripMenuItem setActiveItem;
            private readonly ToolStripMenuItem duplicateItem;
            private readonly ToolStripMenuItem revertItem;
            private readonly ToolStripMenuItem deleteItem;

            public Action<ClipboardHistoryItem>? OnSetActive;
            public Action<ClipboardHistoryItem>? OnDuplicate;
            public Action<ClipboardHistoryItem>? OnRevert;
            public Action<ClipboardHistoryItem>? OnDelete;

            public ClipboardHistoryItem? Item { get; private set; }

            public ThumbnailButton()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
                Size = new Size(72, 72);
                Margin = new Padding(0, 0, 0, 4);
                Cursor = Cursors.Hand;
                BackColor = Color.FromArgb(24, 24, 28);

                setActiveItem = new ToolStripMenuItem("Set as Active") { Image = MakeIcon(IconChar.Clipboard, Color.FromArgb(180, 220, 255)) };
                setActiveItem.Click += (s, e) => { if (Item != null) OnSetActive?.Invoke(Item); };

                duplicateItem = new ToolStripMenuItem("Duplicate") { Image = MakeIcon(IconChar.Clone, SystemColors.ControlText) };
                duplicateItem.Click += (s, e) => { if (Item != null) OnDuplicate?.Invoke(Item); };

                revertItem = new ToolStripMenuItem("Revert") { Image = MakeIcon(IconChar.ArrowRotateLeft, SystemColors.ControlText) };
                revertItem.Click += (s, e) => { if (Item != null) OnRevert?.Invoke(Item); };

                deleteItem = new ToolStripMenuItem("Delete") { Image = MakeIcon(IconChar.Trash, Color.FromArgb(220, 80, 80)) };
                deleteItem.Click += (s, e) => { if (Item != null) OnDelete?.Invoke(Item); };

                menu = new ContextMenuStrip();
                menu.Items.AddRange(new ToolStripItem[] { setActiveItem, duplicateItem, revertItem, new ToolStripSeparator(), deleteItem });
                menu.Opening += (s, e) => { revertItem.Enabled = Item?.IsDirty == true; };
                ContextMenuStrip = menu;
            }

            private static Bitmap MakeIcon(IconChar icon, Color color)
                => FormsIconHelper.ToBitmap(icon, color, 16, 0, FlipOrientation.Normal);

            public void Rebind(ClipboardHistoryItem item, bool active)
            {
                Item = item;
                isActive = active;
                if (item.Thumbnail is Bitmap thumb)
                {
                    Size = new Size(thumb.Width + 8, thumb.Height + 8);
                }
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                g.Clear(BackColor);

                if (Item?.Thumbnail is Bitmap thumb)
                {
                    g.DrawImage(thumb, new Rectangle(4, 4, thumb.Width, thumb.Height));
                    using var borderPen = new Pen(isActive ? ActiveBorder : IdleBorder, isActive ? 2f : 1f);
                    g.DrawRectangle(borderPen, new Rectangle(3, 3, thumb.Width + 1, thumb.Height + 1));
                }

                if (Item?.IsDirty == true)
                {
                    using var dirtyBrush = new SolidBrush(Color.OrangeRed);
                    g.FillEllipse(dirtyBrush, new Rectangle(Width - 11, 2, 8, 8));
                    using var border = new Pen(Color.Black, 1f);
                    g.DrawEllipse(border, new Rectangle(Width - 11, 2, 8, 8));
                }
            }
        }
    }
}
