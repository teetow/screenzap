using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

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

        public ClipboardHistoryPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(24, 24, 28);
            Width = 56;
            Padding = new Padding(6, 6, 6, 6);

            flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            Controls.Add(flow);

            var header = new Label
            {
                Text = "History",
                Dock = DockStyle.Top,
                ForeColor = Color.Gainsboro,
                BackColor = Color.Transparent,
                Height = 18,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold)
            };
            Controls.Add(header);
            Controls.SetChildIndex(header, 0);
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
                            {
                                ItemActivated?.Invoke(this, btn.Item);
                            }
                        };
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

            public ClipboardHistoryItem? Item { get; private set; }

            public ThumbnailButton()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
                Size = new Size(40, 40);
                Margin = new Padding(0, 0, 0, 4);
                Cursor = Cursors.Hand;
                BackColor = Color.FromArgb(24, 24, 28);
            }

            public void Rebind(ClipboardHistoryItem item, bool active)
            {
                Item = item;
                isActive = active;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                g.Clear(BackColor);

                if (Item?.Thumbnail is Bitmap thumb)
                {
                    g.DrawImage(thumb, new Rectangle(4, 4, 32, 32));
                }

                using var borderPen = new Pen(isActive ? ActiveBorder : IdleBorder, isActive ? 2f : 1f);
                g.DrawRectangle(borderPen, new Rectangle(3, 3, 33, 33));

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
