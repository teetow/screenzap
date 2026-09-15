using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using FontAwesome.Sharp;

namespace screenzap
{
    /// <summary>
    /// The ephemeral layers panel. It exists only while something is floating: a paste is a
    /// transient state, not a document structure, so the panel appears with the first floating
    /// layer and is gone the moment the last one is glued down or deleted.
    ///
    /// Rows run top-most first, Photoshop order, and the base image sits at the bottom as
    /// "Background" — selecting it drops the layer selection so the tools address the canvas
    /// again, which is the whole reason to want a panel mid-paste.
    /// </summary>
    public partial class ImageEditor
    {
        private Panel? layersPanel;
        private Panel? layersPanelRowHost;

        // Control.Visible reports *effective* visibility — false whenever an ancestor is
        // hidden, which covers construction and the whole headless test suite. Panel logic
        // that needs "did I put it up?" has to ask this instead of asking the control.
        private bool layersPanelIsShown;

        private ImageLayer? draggingPanelLayer;
        private bool layerRowDragActive;
        private int layerRowDragStartScreenY;
        private Panel? layerRowDropIndicator;

        private const int LayerRowDragThreshold = 4;
        private readonly List<LayerPanelRow> layerPanelRows = new List<LayerPanelRow>();

        private const int LayersPanelWidth = 232;
        private const int LayersPanelRowHeight = 34;
        private const int LayersPanelHeaderHeight = 22;
        private const int LayerRowButtonSize = 26;
        private const int LayerRowThumbnailSize = 26;
        private const int LayersPanelMargin = 8;

        /// <summary>One row of the panel. A null <see cref="Layer"/> marks the Background row.</summary>
        private sealed class LayerPanelRow
        {
            public ImageLayer? Layer;
            public Panel Host = null!;
            public Label Caption = null!;
            public PictureBox Thumbnail = null!;
            public IconButton? Mute;
            public IconButton? Commit;
            public IconButton? Delete;

            // The button's Click handler and the test seams both run these, so a seam
            // exercises the row's real wiring instead of a parallel code path. Button
            // .PerformClick() cannot stand in: it is a no-op unless the control is selectable,
            // and nothing under a hidden form is.
            public Action? MuteAction;
            public Action? CommitAction;
            public Action? DeleteAction;
        }

        internal bool LayersPanelAvailableForTests => layersPanel != null;

        internal bool LayersPanelShownForTests => layersPanelIsShown;

        /// <summary>Row count including the Background row.</summary>
        internal int LayersPanelRowCountForTests => layerPanelRows.Count;

        internal IReadOnlyList<string> LayersPanelCaptionsForTests =>
            layerPanelRows.Select(row => row.Caption.Text).ToList();

        internal bool IsImageLayerVisibleForTests(int index) => imageLayers[index].IsVisible;

        internal void ClickLayerPanelMuteForTests(int layerIndex) =>
            FindPanelRow(layerIndex)?.MuteAction?.Invoke();

        internal void ClickLayerPanelCommitForTests(int layerIndex) =>
            FindPanelRow(layerIndex)?.CommitAction?.Invoke();

        internal void ClickLayerPanelDeleteForTests(int layerIndex) =>
            FindPanelRow(layerIndex)?.DeleteAction?.Invoke();

        internal bool LayerPanelCommitEnabledForTests(int layerIndex) =>
            FindPanelRow(layerIndex)?.Commit?.Enabled == true;

        internal bool LayerPanelMuteShowsMutedForTests(int layerIndex) =>
            FindPanelRow(layerIndex)?.Mute?.IconChar == IconChar.EyeSlash;

        internal void ClickLayerPanelRowForTests(int layerIndex) =>
            SelectLayerFromPanel(layerIndex < 0 ? null : imageLayers[layerIndex]);

        /// <summary>Drop the given layer's row into <paramref name="slot"/>, counting rows from the top.</summary>
        internal bool DragLayerRowToSlotForTests(int layerIndex, int slot) =>
            layerIndex >= 0
            && layerIndex < imageLayers.Count
            && MoveLayerToRowSlot(imageLayers[layerIndex], slot);

        internal int LayerRowDropSlotForTests(int clientY) => LayerRowDropSlotAtClientY(clientY);

        private LayerPanelRow? FindPanelRow(int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= imageLayers.Count)
            {
                return null;
            }

            var layer = imageLayers[layerIndex];
            return layerPanelRows.FirstOrDefault(row => ReferenceEquals(row.Layer, layer));
        }

        /// <summary>
        /// The next free "Paste N". Derived from the live stack rather than a running counter so
        /// it stays right after layers come back from history, an undo, or a delete — a counter
        /// would restart at 1 on restore and hand a restored "Paste 2" a twin.
        /// </summary>
        private int NextPasteLayerNumber()
        {
            int highest = 0;
            foreach (var layer in imageLayers)
            {
                var name = layer.Name;
                if (name != null
                    && name.StartsWith("Paste ", StringComparison.Ordinal)
                    && int.TryParse(name.AsSpan(6), out int number))
                {
                    highest = Math.Max(highest, number);
                }
            }

            return highest + 1;
        }

        private void InitializeLayersPanel()
        {
            if (layersPanel != null)
            {
                return;
            }

            layersPanel = new Panel
            {
                Name = "layersPanel",
                Width = LayersPanelWidth,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SystemColors.Control,
                Visible = false,
            };

            var header = new Label
            {
                Name = "layersPanelHeader",
                Text = "Layers",
                Dock = DockStyle.Top,
                Height = LayersPanelHeaderHeight,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 0, 0),
                BackColor = SystemColors.ControlDark,
                ForeColor = SystemColors.ControlLightLight,
            };

            layersPanelRowHost = new Panel
            {
                Name = "layersPanelRowHost",
                Dock = DockStyle.Fill,
                AutoScroll = true,
            };

            layersPanel.Controls.Add(layersPanelRowHost);
            layersPanel.Controls.Add(header);
            Controls.Add(layersPanel);
        }

        /// <summary>
        /// Rebuild every row. Structural only — called when layers are added, removed, glued
        /// down, muted or restored by undo, never from a drag, where re-laying out a stack of
        /// controls over the canvas would strobe the viewport.
        /// </summary>
        private void RebuildLayersPanel()
        {
            if (layersPanel == null || layersPanelRowHost == null)
            {
                return;
            }

            bool show = imageLayers.Count > 0;
            bool wasShown = layersPanelIsShown;

            DisposeLayerPanelRows();

            if (!show)
            {
                layersPanelIsShown = false;
                layersPanel.Visible = false;
                return;
            }

            layersPanelRowHost.SuspendLayout();
            try
            {
                int y = 0;

                // Top-most layer first, so the panel reads the way the canvas stacks.
                for (int i = imageLayers.Count - 1; i >= 0; i--)
                {
                    var row = CreateLayerPanelRow(imageLayers[i], y);
                    layerPanelRows.Add(row);
                    layersPanelRowHost.Controls.Add(row.Host);
                    y += LayersPanelRowHeight;
                }

                var background = CreateLayerPanelRow(null, y);
                layerPanelRows.Add(background);
                layersPanelRowHost.Controls.Add(background.Host);
                y += LayersPanelRowHeight;

                // Slack over the exact row total: on a dead-on fit the AutoScroll host still
                // decides it needs a scrollbar, and then genuinely does.
                layersPanel.Height = Math.Min(
                    LayersPanelHeaderHeight + y + 6,
                    Math.Max(LayersPanelHeaderHeight + LayersPanelRowHeight * 2, AvailableLayersPanelHeight()));
            }
            finally
            {
                layersPanelRowHost.ResumeLayout(performLayout: true);
            }

            layersPanelIsShown = true;
            layersPanel.Visible = true;
            UpdateLayersPanelSelection();

            if (!wasShown)
            {
                PositionOverlayToolStrips();
            }
            else
            {
                PositionLayersPanel();
            }
        }

        private int AvailableLayersPanelHeight()
        {
            int canvasHeight = canvasPanel?.Height ?? ClientSize.Height;
            return Math.Max(LayersPanelHeaderHeight + LayersPanelRowHeight, canvasHeight - LayersPanelMargin * 2);
        }

        /// <summary>
        /// Row width, leaving room for the scrollbar the host puts up once the stack is deep.
        /// Anything parented to the row host has to respect it: a child wider than the host
        /// trips a horizontal scrollbar, which costs vertical room, which trips the vertical
        /// one, which clips the bottom row.
        /// </summary>
        private static int LayerRowWidth =>
            LayersPanelWidth - SystemInformation.VerticalScrollBarWidth - 4;

        private LayerPanelRow CreateLayerPanelRow(ImageLayer? layer, int top)
        {
            bool isBackground = layer == null;
            int rowWidth = LayerRowWidth;

            var row = new LayerPanelRow
            {
                Layer = layer,
                Host = new Panel
                {
                    Name = isBackground ? "layerRowBackground" : "layerRow",
                    Location = new Point(0, top),
                    Size = new Size(rowWidth, LayersPanelRowHeight),
                    BackColor = SystemColors.Control,
                },
            };

            int buttonTop = (LayersPanelRowHeight - LayerRowButtonSize) / 2;
            int x = 2;

            if (!isBackground)
            {
                row.MuteAction = () => ToggleLayerMuteFromPanel(layer!);
                row.CommitAction = () => ApplyLayerFromPanel(layer!);
                row.DeleteAction = () => DeleteLayerFromPanel(layer!);

                row.Mute = CreateLayerRowButton(
                    "layerRowMute",
                    layer!.IsVisible ? IconChar.Eye : IconChar.EyeSlash,
                    "Mute this layer (muted layers are left out of the canvas, saves and copies)",
                    new Point(x, buttonTop),
                    row.MuteAction);
                row.Host.Controls.Add(row.Mute);
            }

            x += LayerRowButtonSize + 4;

            row.Thumbnail = new PictureBox
            {
                Name = "layerRowThumbnail",
                Location = new Point(x, (LayersPanelRowHeight - LayerRowThumbnailSize) / 2),
                Size = new Size(LayerRowThumbnailSize, LayerRowThumbnailSize),
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.CenterImage,
                // The row owns this bitmap: pointing a PictureBox at the layer's own Source
                // would leave a live GDI handle behind the moment the layer is disposed.
                Image = CreateLayerThumbnail(layer),
            };
            row.Host.Controls.Add(row.Thumbnail);

            x += LayerRowThumbnailSize + 6;

            int captionRight = rowWidth - (isBackground ? 4 : (LayerRowButtonSize * 2 + 8));
            row.Caption = new Label
            {
                Name = "layerRowCaption",
                Text = isBackground ? "Background" : layer!.Name,
                Location = new Point(x, 0),
                Size = new Size(Math.Max(20, captionRight - x), LayersPanelRowHeight),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
            };
            row.Host.Controls.Add(row.Caption);

            if (!isBackground)
            {
                row.Commit = CreateLayerRowButton(
                    "layerRowCommit",
                    IconChar.Check,
                    "Glue this layer down into the canvas",
                    new Point(rowWidth - LayerRowButtonSize * 2 - 6, buttonTop),
                    row.CommitAction!);
                row.Host.Controls.Add(row.Commit);

                row.Delete = CreateLayerRowButton(
                    "layerRowDelete",
                    IconChar.Xmark,
                    "Discard this layer",
                    new Point(rowWidth - LayerRowButtonSize - 3, buttonTop),
                    row.DeleteAction!);
                row.Host.Controls.Add(row.Delete);
            }

            // Clicking anywhere that is not a button selects the row. The buttons are separate
            // controls and keep their own mouse events, so neither selection nor the drag below
            // fires from a click on the eye, check or cross.
            void SelectThisRow(object? sender, EventArgs e) => SelectLayerFromPanel(layer);
            row.Host.Click += SelectThisRow;
            row.Caption.Click += SelectThisRow;
            row.Thumbnail.Click += SelectThisRow;

            if (!isBackground)
            {
                foreach (var surface in new Control[] { row.Host, row.Caption, row.Thumbnail })
                {
                    surface.MouseDown += (sender, e) => BeginLayerRowDrag(sender, e, layer!);
                    surface.MouseMove += UpdateLayerRowDrag;
                    surface.MouseUp += EndLayerRowDrag;
                }
            }

            return row;
        }

        private IconButton CreateLayerRowButton(
            string name,
            IconChar icon,
            string toolTip,
            Point location,
            Action onClick)
        {
            var button = new IconButton
            {
                Name = name,
                IconChar = icon,
                IconColor = SystemColors.ControlText,
                IconFont = IconFont.Auto,
                IconSize = 14,
                Location = location,
                Size = new Size(LayerRowButtonSize, LayerRowButtonSize),
                FlatStyle = FlatStyle.Flat,
                // Keyboard focus belongs to the canvas: a focused button would swallow Enter,
                // which is the shortcut for gluing the selected layer down.
                TabStop = false,
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += (_, _) =>
            {
                onClick();
                pictureBox1?.Focus();
            };

            layersPanelToolTip.SetToolTip(button, toolTip);
            return button;
        }

        private readonly ToolTip layersPanelToolTip = new ToolTip();

        /// <summary>
        /// A row-owned thumbnail. Null <paramref name="layer"/> renders the base image for the
        /// Background row.
        /// </summary>
        private Bitmap CreateLayerThumbnail(ImageLayer? layer)
        {
            int size = LayerRowThumbnailSize - 2;
            var thumbnail = new Bitmap(size, size);
            using var graphics = Graphics.FromImage(thumbnail);
            graphics.Clear(SystemColors.ControlDark);

            Image? source = layer?.Source ?? pictureBox1?.Image;
            if (source == null || source.Width <= 0 || source.Height <= 0)
            {
                return thumbnail;
            }

            float scale = Math.Min((float)size / source.Width, (float)size / source.Height);
            float width = Math.Max(1f, source.Width * scale);
            float height = Math.Max(1f, source.Height * scale);

            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(
                source,
                new RectangleF((size - width) / 2f, (size - height) / 2f, width, height));
            return thumbnail;
        }

        private void UpdateLayersPanelSelection()
        {
            foreach (var row in layerPanelRows)
            {
                bool selected = row.Layer == null
                    ? !HasSelectedLayer
                    : HasSelectedLayer && ReferenceEquals(imageLayers[selectedLayerIndex], row.Layer);

                row.Host.BackColor = selected ? SystemColors.Highlight : SystemColors.Control;
                row.Caption.ForeColor = selected ? SystemColors.HighlightText : SystemColors.ControlText;

                if (row.Layer != null)
                {
                    row.Mute!.IconChar = row.Layer.IsVisible ? IconChar.Eye : IconChar.EyeSlash;
                    row.Mute.IconColor = selected ? SystemColors.HighlightText : SystemColors.ControlText;
                    // Gluing down a muted layer would put pixels you cannot see into the canvas.
                    row.Commit!.Enabled = row.Layer.IsVisible;
                    row.Commit.IconColor = row.Layer.IsVisible
                        ? (selected ? SystemColors.HighlightText : SystemColors.ControlText)
                        : SystemColors.GrayText;
                    row.Delete!.IconColor = selected ? SystemColors.HighlightText : SystemColors.ControlText;
                    row.Caption.Font = row.Layer.IsVisible
                        ? row.Host.Font
                        : new Font(row.Host.Font, FontStyle.Italic);
                }
            }
        }

        private void PositionLayersPanel()
        {
            if (layersPanel == null || !layersPanelIsShown)
            {
                return;
            }

            var canvasBounds = canvasPanel?.Bounds ?? ClientRectangle;
            layersPanel.Height = Math.Min(layersPanel.Height, AvailableLayersPanelHeight());
            layersPanel.Location = new Point(
                Math.Max(canvasBounds.Left, canvasBounds.Right - LayersPanelWidth - LayersPanelMargin),
                canvasBounds.Top + LayersPanelMargin);
            layersPanel.BringToFront();
        }

        private void SelectLayerFromPanel(ImageLayer? layer)
        {
            // The Background row means "stop addressing the paste" — the tools go back to the
            // canvas, which is what you want when a paste is floating over something you still
            // need to fix.
            SelectImageLayer(layer == null ? -1 : imageLayers.IndexOf(layer));
            pictureBox1?.Focus();
        }

        private void ToggleLayerMuteFromPanel(ImageLayer layer)
        {
            int index = imageLayers.IndexOf(layer);
            if (index < 0)
            {
                return;
            }

            var layersBefore = CloneLayers();
            layer.IsVisible = !layer.IsVisible;

            // Muting rides the same layer-only undo step as a drag or a resize, so Ctrl+Z walks
            // back through visibility changes like any other layer edit.
            PushLayerOnlyUndoStep(layersBefore);
            UpdateLayersPanelSelection();
            UpdateCommandUI();
            pictureBox1?.Invalidate();
        }

        private void ApplyLayerFromPanel(ImageLayer layer)
        {
            int index = imageLayers.IndexOf(layer);
            if (index >= 0)
            {
                ApplyFloatingPaste(index);
            }
        }

        private void DeleteLayerFromPanel(ImageLayer layer)
        {
            int index = imageLayers.IndexOf(layer);
            if (index >= 0)
            {
                TryDeleteLayerAt(index);
            }
        }

        private void BeginLayerRowDrag(object? sender, MouseEventArgs e, ImageLayer layer)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            draggingPanelLayer = layer;
            layerRowDragActive = false;
            layerRowDragStartScreenY = Control.MousePosition.Y;

            // Capture keeps the gesture addressed to the row it started on even once the
            // pointer is over a different row — or off the panel entirely, where the drop
            // would otherwise never arrive and leave the drag stuck open.
            if (sender is Control surface)
            {
                surface.Capture = true;
            }
        }

        private void UpdateLayerRowDrag(object? sender, MouseEventArgs e)
        {
            if (draggingPanelLayer == null || e.Button != MouseButtons.Left)
            {
                return;
            }

            if (!layerRowDragActive)
            {
                if (Math.Abs(Control.MousePosition.Y - layerRowDragStartScreenY) < LayerRowDragThreshold)
                {
                    return;
                }

                layerRowDragActive = true;
            }

            ShowLayerRowDropIndicator(LayerRowDropSlotAtScreen(Control.MousePosition));
        }

        private void EndLayerRowDrag(object? sender, MouseEventArgs e)
        {
            if (draggingPanelLayer == null)
            {
                return;
            }

            var dragged = draggingPanelLayer;
            bool wasDrag = layerRowDragActive;

            draggingPanelLayer = null;
            layerRowDragActive = false;
            if (sender is Control surface)
            {
                surface.Capture = false;
            }

            HideLayerRowDropIndicator();

            if (wasDrag)
            {
                MoveLayerToRowSlot(dragged, LayerRowDropSlotAtScreen(Control.MousePosition));
            }
        }

        private int LayerRowDropSlotAtScreen(Point screenPoint) =>
            layersPanelRowHost == null
                ? 0
                : LayerRowDropSlotAtClientY(layersPanelRowHost.PointToClient(screenPoint).Y);

        /// <summary>
        /// Which gap the pointer is nearest, counting from the top: 0 is above the top row and
        /// <see cref="imageLayers"/>.Count is the gap just above Background, which is the floor —
        /// Background is the canvas and nothing floats under it.
        /// </summary>
        private int LayerRowDropSlotAtClientY(int clientY)
        {
            int scrollOffset = layersPanelRowHost?.AutoScrollPosition.Y ?? 0;
            int logicalY = clientY - scrollOffset;
            int slot = (int)Math.Round(logicalY / (double)LayersPanelRowHeight);
            return Math.Clamp(slot, 0, imageLayers.Count);
        }

        /// <summary>
        /// Restack <paramref name="layer"/> into <paramref name="slot"/>. Rows read top-most
        /// first and <see cref="imageLayers"/> is stored bottom-first, so the move happens in
        /// visual order and is written back reversed.
        /// </summary>
        private bool MoveLayerToRowSlot(ImageLayer layer, int slot)
        {
            int count = imageLayers.Count;
            int layerIndex = imageLayers.IndexOf(layer);
            if (layerIndex < 0)
            {
                return false;
            }

            int from = count - 1 - layerIndex;
            int to = Math.Clamp(slot, 0, count);

            // Dropping below where the row currently sits loses a slot to its own removal.
            if (to > from)
            {
                to--;
            }

            if (to == from)
            {
                return false;
            }

            var layersBefore = CloneLayers();

            var visual = new List<ImageLayer>(count);
            for (int i = count - 1; i >= 0; i--)
            {
                visual.Add(imageLayers[i]);
            }

            visual.RemoveAt(from);
            visual.Insert(to, layer);

            imageLayers.Clear();
            for (int i = visual.Count - 1; i >= 0; i--)
            {
                imageLayers.Add(visual[i]);
            }

            selectedLayerIndex = imageLayers.IndexOf(layer);

            PushLayerOnlyUndoStep(layersBefore);
            RelayoutLayerPanelRows();
            UpdateLayerToolbarState();
            UpdateLayersPanelSelection();
            UpdateCommandUI();
            pictureBox1?.Invalidate();
            return true;
        }

        /// <summary>
        /// Re-seat the existing rows in the new order. A reorder changes no row's content, so
        /// this moves them rather than rebuilding — which also avoids disposing the very
        /// control whose mouse-up is still on the stack.
        /// </summary>
        private void RelayoutLayerPanelRows()
        {
            if (layersPanelRowHost == null)
            {
                return;
            }

            var ordered = new List<LayerPanelRow>(layerPanelRows.Count);
            for (int i = imageLayers.Count - 1; i >= 0; i--)
            {
                var row = layerPanelRows.FirstOrDefault(r => ReferenceEquals(r.Layer, imageLayers[i]));
                if (row != null)
                {
                    ordered.Add(row);
                }
            }

            var background = layerPanelRows.FirstOrDefault(r => r.Layer == null);
            if (background != null)
            {
                ordered.Add(background);
            }

            if (ordered.Count != layerPanelRows.Count)
            {
                // Rows and layers disagree — the set changed under us, so start over.
                RebuildLayersPanel();
                return;
            }

            layerPanelRows.Clear();
            layerPanelRows.AddRange(ordered);

            int scrollOffset = layersPanelRowHost.AutoScrollPosition.Y;
            layersPanelRowHost.SuspendLayout();
            try
            {
                for (int i = 0; i < layerPanelRows.Count; i++)
                {
                    layerPanelRows[i].Host.Location =
                        new Point(0, i * LayersPanelRowHeight + scrollOffset);
                }
            }
            finally
            {
                layersPanelRowHost.ResumeLayout(performLayout: true);
            }
        }

        private void ShowLayerRowDropIndicator(int slot)
        {
            if (layersPanelRowHost == null)
            {
                return;
            }

            if (layerRowDropIndicator == null)
            {
                layerRowDropIndicator = new Panel
                {
                    Name = "layerRowDropIndicator",
                    Height = 3,
                    Width = LayerRowWidth,
                    BackColor = SystemColors.Highlight,
                };
                layersPanelRowHost.Controls.Add(layerRowDropIndicator);
            }

            layerRowDropIndicator.Location = new Point(
                0,
                slot * LayersPanelRowHeight + layersPanelRowHost.AutoScrollPosition.Y - 1);
            layerRowDropIndicator.Visible = true;
            layerRowDropIndicator.BringToFront();
        }

        private void HideLayerRowDropIndicator()
        {
            if (layerRowDropIndicator != null)
            {
                layerRowDropIndicator.Visible = false;
            }
        }

        private void DisposeLayerPanelRows()
        {
            if (layersPanelRowHost == null)
            {
                return;
            }

            // The ToolTip keeps an entry per control it was given; the rows are rebuilt on
            // every structural change, so without this the table grows for the life of the
            // editor and holds disposed controls.
            layersPanelToolTip.RemoveAll();

            foreach (var row in layerPanelRows)
            {
                layersPanelRowHost.Controls.Remove(row.Host);
                row.Thumbnail.Image?.Dispose();
                row.Host.Dispose();
            }

            layerPanelRows.Clear();
        }

        /// <summary>Tear down what the panel owns outside the control tree.</summary>
        private void DisposeLayersPanel()
        {
            DisposeLayerPanelRows();
            layerRowDropIndicator?.Dispose();
            layersPanelToolTip.Dispose();
        }
    }
}
