using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
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
        private const int ThumbnailOuterPadding = 8;
        private readonly FlowLayoutPanel flow;
        private readonly ToolTip tooltip = new ToolTip();
        private readonly Dictionary<Guid, ThumbnailButton> buttons = new();
        private readonly ContextMenuStrip listMenu;
        private readonly ToolStripMenuItem refreshFromWindowsHistoryItem;
        private readonly ToolStripMenuItem activateNewestItem;
        private readonly ToolStripMenuItem scrollToActiveItem;
        private readonly ToolStripMenuItem showTextItemsItem;
        private ClipboardHistoryStore? store;
        private int currentThumbMaxWidth = 64;
        private int currentThumbMaxHeight = 64;
        private bool suppressShowTextItemsEvents;
        private Guid? lastActiveItemId;

        public event EventHandler<ClipboardHistoryItem>? ItemActivated;
        public event EventHandler<ClipboardHistoryItem>? ItemSetActive;
        public event EventHandler<ClipboardHistoryItem>? ItemDuplicate;
        public event EventHandler<ClipboardHistoryItem>? ItemRevert;
        public event EventHandler<ClipboardHistoryItem>? ItemDelete;
        public event EventHandler? RefreshRequested;
        public event EventHandler? ActivateNewestRequested;
        public event EventHandler<bool>? ShowTextItemsChanged;

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
            EnableDoubleBuffer(flow);
            Controls.Add(flow);

            refreshFromWindowsHistoryItem = new ToolStripMenuItem("Refresh from Windows History")
            {
                Image = MakeIcon(IconChar.Rotate, Color.FromArgb(180, 220, 255))
            };
            refreshFromWindowsHistoryItem.Click += (s, e) => RefreshRequested?.Invoke(this, EventArgs.Empty);

            activateNewestItem = new ToolStripMenuItem("Activate Newest")
            {
                Image = MakeIcon(IconChar.ClockRotateLeft, SystemColors.ControlText)
            };
            activateNewestItem.Click += (s, e) => ActivateNewestRequested?.Invoke(this, EventArgs.Empty);

            scrollToActiveItem = new ToolStripMenuItem("Scroll to Active")
            {
                Image = MakeIcon(IconChar.LocationArrow, SystemColors.ControlText)
            };
            scrollToActiveItem.Click += (s, e) => ScrollToActiveItem();

            showTextItemsItem = new ToolStripMenuItem("Show Text Items")
            {
                CheckOnClick = true,
                Image = MakeIcon(IconChar.Font, SystemColors.ControlText)
            };
            showTextItemsItem.CheckedChanged += (s, e) =>
            {
                if (suppressShowTextItemsEvents)
                {
                    return;
                }

                ShowTextItemsChanged?.Invoke(this, showTextItemsItem.Checked);
            };

            listMenu = new ContextMenuStrip();
            listMenu.Items.AddRange(new ToolStripItem[]
            {
                refreshFromWindowsHistoryItem,
                new ToolStripSeparator(),
                activateNewestItem,
                scrollToActiveItem,
                new ToolStripSeparator(),
                showTextItemsItem
            });
            listMenu.Opening += (s, e) =>
            {
                bool hasItems = store?.Items.Count > 0;
                activateNewestItem.Enabled = hasItems;
                scrollToActiveItem.Enabled = store?.ActiveItem != null;
            };

            flow.ContextMenuStrip = listMenu;
            ContextMenuStrip = listMenu;

            SizeChanged += (_, _) => UpdateThumbnailSizing();
            flow.SizeChanged += (_, _) => UpdateThumbnailSizing();
        }

        public void SetShowTextItems(bool enabled)
        {
            suppressShowTextItemsEvents = true;
            try
            {
                showTextItemsItem.Checked = enabled;
            }
            finally
            {
                suppressShowTextItemsEvents = false;
            }
        }

        public void AttachStore(ClipboardHistoryStore store)
        {
            if (this.store != null)
            {
                this.store.Changed -= OnStoreChanged;
                this.store.ActiveItemChanged -= OnActiveItemChanged;
                this.store.ItemUpdated -= OnItemUpdated;
            }

            this.store = store;
            store.Changed += OnStoreChanged;
            store.ActiveItemChanged += OnActiveItemChanged;
            store.ItemUpdated += OnItemUpdated;
            lastActiveItemId = store.ActiveItem?.Id;
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

        private void OnActiveItemChanged(object? sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(UpdateActiveSelection));
                return;
            }

            UpdateActiveSelection();
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
            item.RebuildThumbnail(currentThumbMaxWidth, currentThumbMaxHeight);
            if (buttons.TryGetValue(item.Id, out var btn))
            {
                btn.Rebind(item, ReferenceEquals(store?.ActiveItem, item), currentThumbMaxWidth, currentThumbMaxHeight);
            }
        }

        private void UpdateActiveSelection()
        {
            if (store == null)
            {
                return;
            }

            var newActiveId = store.ActiveItem?.Id;

            flow.SuspendLayout();
            try
            {
                if (lastActiveItemId.HasValue
                    && buttons.TryGetValue(lastActiveItemId.Value, out var oldActiveButton)
                    && oldActiveButton.Item != null)
                {
                    oldActiveButton.Rebind(oldActiveButton.Item, false, currentThumbMaxWidth, currentThumbMaxHeight);
                }

                if (newActiveId.HasValue
                    && buttons.TryGetValue(newActiveId.Value, out var newActiveButton)
                    && newActiveButton.Item != null)
                {
                    newActiveButton.Rebind(newActiveButton.Item, true, currentThumbMaxWidth, currentThumbMaxHeight);
                }
            }
            finally
            {
                flow.ResumeLayout(false);
            }

            lastActiveItemId = newActiveId;
        }

        private void RebuildAll()
        {
            if (store == null) return;

            UpdateThumbnailSizing(force: true);

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

                    item.RebuildThumbnail(currentThumbMaxWidth, currentThumbMaxHeight);
                    btn.Rebind(item, ReferenceEquals(store.ActiveItem, item), currentThumbMaxWidth, currentThumbMaxHeight);
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

            lastActiveItemId = store.ActiveItem?.Id;
        }

        private void UpdateThumbnailSizing(bool force = false)
        {
            int availableWidth = Math.Max(1, flow.ClientSize.Width - flow.Padding.Horizontal - ThumbnailOuterPadding);
            int availableHeight = availableWidth;

            if (!force && availableWidth == currentThumbMaxWidth && availableHeight == currentThumbMaxHeight)
            {
                return;
            }

            currentThumbMaxWidth = availableWidth;
            currentThumbMaxHeight = availableHeight;

            if (store == null)
            {
                return;
            }

            flow.SuspendLayout();
            try
            {
                foreach (var item in store.Items)
                {
                    item.RebuildThumbnail(currentThumbMaxWidth, currentThumbMaxHeight);
                    if (buttons.TryGetValue(item.Id, out var btn))
                    {
                        btn.Rebind(item, ReferenceEquals(store.ActiveItem, item), currentThumbMaxWidth, currentThumbMaxHeight);
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

        private void ScrollToActiveItem()
        {
            var active = store?.ActiveItem;
            if (active == null)
            {
                return;
            }

            if (buttons.TryGetValue(active.Id, out var btn))
            {
                flow.ScrollControlIntoView(btn);
            }
        }

        private static Bitmap MakeIcon(IconChar icon, Color color)
            => FormsIconHelper.ToBitmap(icon, color, 16, 0, FlipOrientation.Normal);

        private static void EnableDoubleBuffer(Control control)
        {
            var property = typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            property?.SetValue(control, true, null);
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
            private Bitmap? lastThumbnail;

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
                menu.Opening += (s, e) => { revertItem.Enabled = Item?.CanRevertToOriginal == true; };
                ContextMenuStrip = menu;
            }

            private static Bitmap MakeIcon(IconChar icon, Color color)
                => FormsIconHelper.ToBitmap(icon, color, 16, 0, FlipOrientation.Normal);

            public void Rebind(ClipboardHistoryItem item, bool active)
            {
                Rebind(item, active, 64, 64);
            }

            public void Rebind(ClipboardHistoryItem item, bool active, int maxThumbWidth, int maxThumbHeight)
            {
                bool changed = !ReferenceEquals(Item, item) || isActive != active;
                Item = item;
                isActive = active;

                var newSize = new Size(Math.Max(1, maxThumbWidth) + 8, Math.Max(1, maxThumbHeight) + 8);
                if (Size != newSize)
                {
                    Size = newSize;
                    changed = true;
                }

                var currentThumb = item.Thumbnail;
                if (!ReferenceEquals(lastThumbnail, currentThumb))
                {
                    lastThumbnail = currentThumb;
                    changed = true;
                }

                if (changed)
                {
                    Invalidate();
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                g.Clear(BackColor);

                if (Item?.Thumbnail is Bitmap thumb)
                {
                    int thumbX = 4 + Math.Max(0, (Width - 8 - thumb.Width) / 2);
                    int thumbY = 4 + Math.Max(0, (Height - 8 - thumb.Height) / 2);
                    g.DrawImage(thumb, new Rectangle(thumbX, thumbY, thumb.Width, thumb.Height));
                    using var borderPen = new Pen(isActive ? ActiveBorder : IdleBorder, isActive ? 2f : 1f);
                    g.DrawRectangle(borderPen, new Rectangle(thumbX - 1, thumbY - 1, thumb.Width + 1, thumb.Height + 1));
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
