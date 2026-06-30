using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        private const int FallbackScrollbarWidth = 17;
        private readonly FlowLayoutPanel flow;
        private readonly ToolTip tooltip = new ToolTip();
        private readonly Dictionary<Guid, ThumbnailButton> buttons = new();
        private readonly Dictionary<Guid, string> tooltipTextById = new();
        private readonly ContextMenuStrip listMenu;
        private readonly ToolStripMenuItem refreshFromWindowsHistoryItem;
        private readonly ToolStripMenuItem activateNewestItem;
        private readonly ToolStripMenuItem scrollToActiveItem;
        private readonly ContextMenuStrip thumbnailMenu;
        private readonly ToolStripMenuItem setActiveThumbnailItem;
        private readonly ToolStripMenuItem duplicateThumbnailItem;
        private readonly ToolStripMenuItem revertThumbnailItem;
        private readonly ToolStripMenuItem deleteThumbnailItem;
        private ThumbnailButton? contextThumbnailButton;
        private ClipboardHistoryStore? store;
        private int currentThumbMaxWidth = 64;
        private int currentThumbMaxHeight = 64;
        private Guid? lastActiveItemId;

        public event EventHandler<ClipboardHistoryItem>? ItemActivated;
        public event EventHandler<ClipboardHistoryItem>? ItemSetActive;
        public event EventHandler<ClipboardHistoryItem>? ItemDuplicate;
        public event EventHandler<ClipboardHistoryItem>? ItemRevert;
        public event EventHandler<ClipboardHistoryItem>? ItemDelete;
        public event EventHandler? RefreshRequested;
        public event EventHandler? ActivateNewestRequested;

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

            listMenu = new ContextMenuStrip();
            listMenu.Items.AddRange(new ToolStripItem[]
            {
                refreshFromWindowsHistoryItem,
                new ToolStripSeparator(),
                activateNewestItem,
                scrollToActiveItem
            });
            listMenu.Opening += (s, e) =>
            {
                bool hasItems = store?.Items.Count > 0;
                activateNewestItem.Enabled = hasItems;
                scrollToActiveItem.Enabled = store?.ActiveItem != null;
            };

            flow.ContextMenuStrip = listMenu;
            ContextMenuStrip = listMenu;

            // All rows share one context menu. A full history can contain 128 controls; creating
            // four menu items and rendering four FontAwesome bitmaps for every row dominated panel
            // construction even though only one menu can ever be open.
            setActiveThumbnailItem = new ToolStripMenuItem("Set as Active")
            {
                Image = MakeIcon(IconChar.Clipboard, Color.FromArgb(180, 220, 255))
            };
            duplicateThumbnailItem = new ToolStripMenuItem("Duplicate")
            {
                Image = MakeIcon(IconChar.Clone, SystemColors.ControlText)
            };
            revertThumbnailItem = new ToolStripMenuItem("Revert")
            {
                Image = MakeIcon(IconChar.ArrowRotateLeft, SystemColors.ControlText)
            };
            deleteThumbnailItem = new ToolStripMenuItem("Delete")
            {
                Image = MakeIcon(IconChar.Trash, Color.FromArgb(220, 80, 80))
            };
            thumbnailMenu = new ContextMenuStrip();
            thumbnailMenu.Items.AddRange(new ToolStripItem[]
            {
                setActiveThumbnailItem,
                duplicateThumbnailItem,
                revertThumbnailItem,
                new ToolStripSeparator(),
                deleteThumbnailItem
            });
            thumbnailMenu.Opening += (_, e) =>
            {
                contextThumbnailButton = thumbnailMenu.SourceControl as ThumbnailButton;
                if (contextThumbnailButton?.Item == null)
                {
                    e.Cancel = true;
                    return;
                }

                revertThumbnailItem.Enabled = contextThumbnailButton.Item.CanRevertToOriginal;
            };
            setActiveThumbnailItem.Click += (_, _) =>
            {
                if (contextThumbnailButton?.Item is { } item)
                    ItemSetActive?.Invoke(this, item);
            };
            duplicateThumbnailItem.Click += (_, _) =>
            {
                if (contextThumbnailButton?.Item is { } item)
                    ItemDuplicate?.Invoke(this, item);
            };
            revertThumbnailItem.Click += (_, _) =>
            {
                if (contextThumbnailButton?.Item is { } item)
                    ItemRevert?.Invoke(this, item);
            };
            deleteThumbnailItem.Click += (_, _) =>
            {
                if (contextThumbnailButton?.Item is { } item)
                    DeleteAndReclaimFocus(contextThumbnailButton, item);
            };

            SizeChanged += (_, _) => UpdateThumbnailSizing();
            flow.SizeChanged += (_, _) => UpdateThumbnailSizing();
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
            // Metadata-only updates (for example binding a Windows history id) must not pull an
            // off-screen persisted thumbnail into memory. Edited/new items already own a thumbnail.
            if (item.Thumbnail != null)
            {
                item.RebuildThumbnail(currentThumbMaxWidth, currentThumbMaxHeight);
            }

            if (buttons.TryGetValue(item.Id, out var btn))
            {
                btn.Rebind(item, ReferenceEquals(store?.ActiveItem, item), currentThumbMaxWidth, currentThumbMaxHeight);
                UpdateToolTip(btn, item);
            }
        }

        private void UpdateToolTip(ThumbnailButton btn, ClipboardHistoryItem item)
        {
            // SetToolTip re-registers the control with the win32 tooltip and is comparatively slow,
            // so only call it when the text actually changed (dirty flag / timestamp).
            var text = BuildToolTip(item);
            if (tooltipTextById.TryGetValue(item.Id, out var existing) && existing == text)
            {
                return;
            }

            tooltipTextById[item.Id] = text;
            tooltip.SetToolTip(btn, text);
        }

        private void DeleteAndReclaimFocus(ThumbnailButton source, ClipboardHistoryItem item)
        {
            // Capture focus/position before the delete disposes the button.
            bool reclaim = HasLogicalFocus(source);
            int index = flow.Controls.GetChildIndex(source);

            ItemDelete?.Invoke(this, item);

            // Deleting the active item activates its successor, which focuses the editor canvas.
            // Pull focus onto the button now occupying the deleted slot so serial Deletes work.
            if (!reclaim || flow.Controls.Count == 0) return;
            var successor = flow.Controls[Math.Min(index, flow.Controls.Count - 1)];
            KeepFocusOn(successor);
        }

        // Up/Down (Left/Right as aliases — the list is a single column) move the SELECTION:
        // the destination item is activated, so the blue border, the editor content and the
        // keyboard all travel together. Home/End jump to the ends.
        private void NavigateFocusFrom(ThumbnailButton source, Keys key)
        {
            int count = flow.Controls.Count;
            if (count == 0) return;

            int index = flow.Controls.GetChildIndex(source);
            int target = key switch
            {
                Keys.Up or Keys.Left => Math.Max(0, index - 1),
                Keys.Down or Keys.Right => Math.Min(count - 1, index + 1),
                Keys.Home => 0,
                Keys.End => count - 1,
                _ => index
            };

            if (target == index) return;
            if (flow.Controls[target] is not ThumbnailButton destination) return;

            if (destination.Item != null)
                ItemActivated?.Invoke(this, destination.Item);
            if (!destination.IsDisposed)
                KeepFocusOn(destination);
        }

        // Focus the thumbnail for list-local keyboard commands. Keep this synchronous: a delayed
        // reclaim can override a later, intentional click into the editor and misroute shortcuts.
        // Loading presenter content must preserve focus rather than starting a focus race.
        private void KeepFocusOn(Control target)
        {
            target.Select();
            flow.ScrollControlIntoView(target);
        }

        // ContainsFocus is OS-focus based and stays false on forms that have never been shown
        // (tests, pre-Show init), so logical focus also consults the ActiveControl chain.
        private static bool HasLogicalFocus(Control control)
        {
            if (control.ContainsFocus) return true;
            return control.FindForm() is Form form && GetLeafActiveControl(form) == control;
        }

        private static Control? GetLeafActiveControl(ContainerControl container)
        {
            Control? active = container.ActiveControl;
            while (active is ContainerControl nested && nested.ActiveControl != null)
            {
                active = nested.ActiveControl;
            }
            return active;
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

            UpdateThumbnailSizing();

            flow.SuspendLayout();
            try
            {
                var existingIds = buttons.Keys.ToHashSet();
                var items = store.Items;
                var liveIds = new HashSet<Guid>(items.Count);

                // Incremental update: reuse existing buttons, only create/remove what changed and
                // fix the child order in place. Avoids tearing down and re-adding every control (and
                // re-registering every tooltip) on each change — costly when the history is full.
                foreach (var item in items)
                {
                    liveIds.Add(item.Id);
                    bool isNewButton = !buttons.TryGetValue(item.Id, out var btn);
                    if (isNewButton)
                    {
                        btn = new ThumbnailButton();
                        btn.Click += (s, e) =>
                        {
                            if (btn.Item != null)
                                ItemActivated?.Invoke(this, btn.Item);
                            // Activation may select the presenter synchronously; the user's thumbnail
                            // click is the final focus decision for this input event.
                            if (!btn.IsDisposed)
                                KeepFocusOn(btn);
                        };
                        btn.OnDelete     = i => DeleteAndReclaimFocus(btn, i);
                        btn.OnNavigate   = NavigateFocusFrom;
                        btn.OnBeginDrag  = BeginThumbnailDrag;
                        btn.ContextMenuStrip = thumbnailMenu;
                        buttons[item.Id] = btn;
                        flow.Controls.Add(btn);
                    }

                    btn!.Rebind(item, ReferenceEquals(store.ActiveItem, item), currentThumbMaxWidth, currentThumbMaxHeight);
                    UpdateToolTip(btn, item);
                }

                // Dispose buttons for removed items.
                foreach (var goneId in existingIds.Except(liveIds).ToList())
                {
                    if (buttons.TryGetValue(goneId, out var orphan))
                    {
                        buttons.Remove(goneId);
                        tooltipTextById.Remove(goneId);
                        flow.Controls.Remove(orphan);
                        orphan.Dispose();
                    }
                }

                // Align the flow's child order with the store order (cheap when already correct).
                for (int i = 0; i < items.Count; i++)
                {
                    if (buttons.TryGetValue(items[i].Id, out var ordered))
                    {
                        flow.Controls.SetChildIndex(ordered, i);
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
            int availableWidth = CalculateThumbnailMaxWidth();
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
                    // Keep file-backed, off-screen thumbnails lazy across panel resizes.
                    if (item.Thumbnail != null)
                    {
                        item.RebuildThumbnail(currentThumbMaxWidth, currentThumbMaxHeight);
                    }

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

        private int CalculateThumbnailMaxWidth()
        {
            int scrollbarReserve = SystemInformation.VerticalScrollBarWidth;
            if (scrollbarReserve <= 0)
            {
                scrollbarReserve = FallbackScrollbarWidth;
            }

            int reservedPadding = flow.Padding.Left + scrollbarReserve + ThumbnailOuterPadding;
            return Math.Max(1, flow.Width - reservedPadding);
        }

        internal bool ClickItemForDiagnostics(Guid itemId)
        {
            if (!buttons.TryGetValue(itemId, out var btn))
            {
                return false;
            }

            btn.ClickForDiagnostics();
            return true;
        }

        internal bool SendKeyToItemForDiagnostics(Guid itemId, Keys keyData)
        {
            if (!buttons.TryGetValue(itemId, out var btn))
            {
                return false;
            }

            btn.SendKeyForDiagnostics(keyData);
            return true;
        }

        /// <summary>The history item whose thumbnail holds (logical) keyboard focus, if any.</summary>
        internal ClipboardHistoryItem? FocusedItemForDiagnostics =>
            FindForm() is Form form && GetLeafActiveControl(form) is ThumbnailButton { IsDisposed: false } btn
                ? btn.Item
                : null;

        /// <summary>
        /// Deliver a key the way the OS would: to whatever holds focus. Returns false (and delivers
        /// nothing) when no thumbnail is focused — mirroring that Delete can't reach the list then.
        /// </summary>
        internal bool SendKeyThroughFocusForDiagnostics(Keys keyData)
        {
            if (FindForm() is not Form form
                || GetLeafActiveControl(form) is not ThumbnailButton { IsDisposed: false } btn)
            {
                return false;
            }

            btn.SendKeyForDiagnostics(keyData);
            return true;
        }

        internal Size GetItemButtonSizeForDiagnostics(Guid itemId)
        {
            return buttons.TryGetValue(itemId, out var btn)
                ? btn.Size
                : Size.Empty;
        }

        internal Bitmap? CloneItemDragImageForDiagnostics(Guid itemId)
        {
            if (!buttons.TryGetValue(itemId, out var btn))
            {
                return null;
            }

            using var payload = ClipboardHistoryImageDragPayload.Create(btn.Item);
            return payload == null ? null : new Bitmap(payload.Image);
        }

        private static void BeginThumbnailDrag(ThumbnailButton source, ClipboardHistoryItem item)
        {
            using var payload = ClipboardHistoryImageDragPayload.Create(item);
            if (payload == null)
            {
                return;
            }

            source.DoDragDrop(payload.CreateDataObject(), DragDropEffects.Copy);
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
            private const int ChromePadding = 8;
            private const int MinButtonHeight = 24;
            private static readonly Color ActiveBorder = Color.DeepSkyBlue;
            private static readonly Color IdleBorder = Color.FromArgb(70, 70, 75);
            private bool isActive;
            private Bitmap? lastThumbnail;
            private int maxThumbWidth = 64;
            private int maxThumbHeight = 64;
            private Point? dragStart;
            private bool suppressClickAfterDrag;

            public Action<ClipboardHistoryItem>? OnDelete;
            public Action<ThumbnailButton, Keys>? OnNavigate;
            public Action<ThumbnailButton, ClipboardHistoryItem>? OnBeginDrag;

            public ClipboardHistoryItem? Item { get; private set; }

            public ThumbnailButton()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
                Size = new Size(72, 72);
                Margin = new Padding(0, 0, 0, 4);
                Cursor = Cursors.Hand;
                BackColor = Color.FromArgb(24, 24, 28);
                TabStop = true; // reachable via Tab so the keyboard Delete shortcut has somewhere to land
            }

            // Raw Control doesn't take keyboard focus on click (that's ButtonBase behavior), so take
            // it explicitly — the click → Delete flow needs the key to land on this button.
            protected override void OnMouseDown(MouseEventArgs e)
            {
                Select();
                suppressClickAfterDrag = false;
                dragStart = e.Button == MouseButtons.Left && Item?.Kind == ClipboardItemKind.Image
                    ? e.Location
                    : null;
                base.OnMouseDown(e);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (e.Button != MouseButtons.Left
                    || dragStart is not Point origin
                    || Item == null
                    || OnBeginDrag is not { } beginDrag)
                {
                    return;
                }

                var dragSize = SystemInformation.DragSize;
                var threshold = new Rectangle(
                    origin.X - dragSize.Width / 2,
                    origin.Y - dragSize.Height / 2,
                    dragSize.Width,
                    dragSize.Height);
                if (threshold.Contains(e.Location))
                {
                    return;
                }

                dragStart = null;
                suppressClickAfterDrag = true;
                beginDrag(this, Item);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                dragStart = null;
                base.OnMouseUp(e);
            }

            protected override void OnClick(EventArgs e)
            {
                if (suppressClickAfterDrag)
                {
                    suppressClickAfterDrag = false;
                    return;
                }

                base.OnClick(e);
            }

            // Make sure Delete and the arrow keys reach OnKeyDown instead of being treated as
            // dialog-navigation keys (which would move focus to unrelated controls form-wide).
            protected override bool IsInputKey(Keys keyData)
                => (keyData & Keys.KeyCode) is Keys.Delete or Keys.Up or Keys.Down or Keys.Left or Keys.Right
                    || base.IsInputKey(keyData);

            protected override void OnKeyDown(KeyEventArgs e)
            {
                // Delete the focused history item. This handler lives on the thumbnail button, so it
                // can ONLY run while this button holds keyboard focus — focus is singular, so it can
                // never fire while the image editor is focused for editing. That scoping (not a flag)
                // is the safety guarantee: there is no path where an in-editor Delete reaches here.
                if (e.KeyCode == Keys.Delete && Item != null && OnDelete is { } handler)
                {
                    // Capture the item — the button may be reused/disposed by the time we delete.
                    var target = Item;
                    if (IsHandleCreated)
                    {
                        // Invoking now would run the store mutation that disposes THIS button (via the
                        // panel rebuild) in the middle of its own key event. Post it so the key event
                        // unwinds first.
                        BeginInvoke((Action)(() => handler(target)));
                    }
                    else
                    {
                        // No window handle means this isn't a live focused control (only reachable
                        // from diagnostics), so nothing is unwinding our WndProc — a direct call is safe.
                        handler(target);
                    }
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                }

                if (e.KeyCode is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Home or Keys.End
                    && OnNavigate is { } navigate)
                {
                    navigate(this, e.KeyCode);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                }

                base.OnKeyDown(e);
            }

            protected override void OnGotFocus(EventArgs e)
            {
                base.OnGotFocus(e);
                Invalidate();
            }

            protected override void OnLostFocus(EventArgs e)
            {
                base.OnLostFocus(e);
                Invalidate();
            }

            public void Rebind(ClipboardHistoryItem item, bool active)
            {
                Rebind(item, active, 64, 64);
            }

            public void Rebind(ClipboardHistoryItem item, bool active, int maxThumbWidth, int maxThumbHeight)
            {
                bool changed = !ReferenceEquals(Item, item) || isActive != active;
                Item = item;
                isActive = active;
                this.maxThumbWidth = Math.Max(1, maxThumbWidth);
                this.maxThumbHeight = Math.Max(1, maxThumbHeight);

                var thumb = item.Thumbnail;
                int buttonWidth = this.maxThumbWidth + ChromePadding;
                int contentHeight = thumb?.Height ?? item.GetThumbnailDisplaySize(this.maxThumbWidth, this.maxThumbHeight).Height;
                int buttonHeight = Math.Max(MinButtonHeight, contentHeight + ChromePadding);
                var newSize = new Size(buttonWidth, buttonHeight);
                if (Size != newSize)
                {
                    Size = newSize;
                    changed = true;
                }

                var currentThumb = thumb;
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

            internal void ClickForDiagnostics()
            {
                // Mirror a real click's event order: mouse-down (which takes focus) precedes the click.
                OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 1, 1, 0));
                OnClick(EventArgs.Empty);
                OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, 1, 1, 0));
            }

            internal void SendKeyForDiagnostics(Keys keyData)
            {
                OnKeyDown(new KeyEventArgs(keyData));
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                g.Clear(BackColor);

                // WinForms only paints child controls intersecting the visible viewport. This is
                // therefore the virtualization boundary: persisted thumbnail PNGs are decoded as
                // they scroll into view instead of all 128 being decoded while the form opens.
                if (Item != null && Item.Thumbnail == null)
                {
                    Item.RebuildThumbnail(maxThumbWidth, maxThumbHeight);
                    lastThumbnail = Item.Thumbnail;
                }

                if (Item?.Thumbnail is Bitmap thumb)
                {
                    int thumbX = (Width - thumb.Width) / 2;
                    int thumbY = (Height - thumb.Height) / 2;
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

                // Keyboard-focus cue: a dotted ring around the whole button, distinct from the solid
                // blue "active" border, so the user can see which item the Delete key will remove.
                if (Focused)
                {
                    using var focusPen = new Pen(Color.White, 1f) { DashStyle = DashStyle.Dot };
                    g.DrawRectangle(focusPen, new Rectangle(1, 1, Width - 3, Height - 3));
                }
            }
        }
    }
}
