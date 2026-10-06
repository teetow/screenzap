using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FontAwesome.Sharp;

namespace screenzap
{
    public partial class ImageEditor
    {
        private ToolStrip? documentToolStrip;
        private ToolStripDropDownButton? geometryCommands;
        private ToolStripDropDownButton? cleanupCommands;
        private ToolStripDropDownButton? adjustmentCommands;
        private ToolStripButton? transparencyViewButton;
        private ToolStripButton? fitViewButton;
        private ToolStripButton? actualSizeViewButton;
        private ToolStripButton? zoomInViewButton;
        private ToolStripButton? zoomOutViewButton;

        private void InitializeEditorCommandLayout()
        {
            var strips = SuspendToolStripLayout();
            try
            {
                // The hosted editor uses the host's file bar. Keep an equivalent bar for
                // standalone surfaces, with export commands out of the image operations row.
                documentToolStrip = new ToolStrip
                {
                    Name = "documentToolStrip", AccessibleName = "File actions",
                    Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden,
                    ImageScalingSize = mainToolStrip.ImageScalingSize
                };
                foreach (var item in new ToolStripItem[]
                {
                    saveToolStripButton, saveAsToolStripButton, copyClipboardToolStripButton,
                    traceToolStripDropDown, reloadToolStripButton, reloadNotificationLabel
                })
                {
                    item.Owner?.Items.Remove(item);
                    documentToolStrip.Items.Add(item);
                }
                copyClipboardToolStripButton.Text = "Copy";
                traceToolStripDropDown.Text = "Copy SVG";
                Controls.Add(documentToolStrip);
                documentToolStrip.BringToFront();

                mainToolStrip.AccessibleName = "Image operations";
                mainToolStrip.CanOverflow = true;
                geometryCommands = CreateOperationGroup("Geometry", "Image dimensions and orientation",
                    cropToolStripButton, resizeImageToolStripButton!, expandCanvasToolStripButton,
                    rotateToolStripButton, flipHorizontalToolStripButton, flipVerticalToolStripButton);
                cleanupCommands = CreateOperationGroup("Cleanup", "Remove content from the selected region",
                    replaceToolStripButton);
                adjustmentCommands = CreateOperationGroup("Adjustments", "Image color and clarity",
                    colorCorrectToolStripButton!, deJpegButton!, optimizeTextToolStripButton);
                mainToolStrip.Items.AddRange(new ToolStripItem[]
                {
                    geometryCommands, new ToolStripSeparator(), cleanupCommands,
                    new ToolStripSeparator(), adjustmentCommands
                });

                cropToolStripButton.Text = "Crop to Selection";
                rotateToolStripButton.Text = "Rotate 90° Right";
                expandCanvasToolStripButton.Text = "Expand Canvas (8 px)";
                replaceToolStripButton.Text = "Replace Background";
                colorCorrectToolStripButton!.Text = "Color Correction…";
                deJpegButton!.Text = "De-JPEG…";
                straightenToolStripButton.Text = "Perspective";
                straightenToolStripButton.IconChar = IconChar.DrawPolygon;
                toolsToolStrip!.AccessibleName = "Canvas tools";
                toolsToolStrip.CanOverflow = true;

                // Retain the actual buttons and their handlers; only their presentation changes.
                var railButtons = new[]
                {
                    moveToolStripButton, arrowToolStripButton, rectangleToolStripButton,
                    highlighterToolStripButton, textToolStripButton, emojiToolStripButton,
                    censorToolStripButton, freeRotateToolStripButton, straightenToolStripButton
                };
                foreach (var button in railButtons) toolsToolStrip.Items.Remove(button!);
                toolsToolStrip.Items.Add(moveToolStripButton!);
                AddRailSection("Annotate", arrowToolStripButton, rectangleToolStripButton,
                    highlighterToolStripButton, textToolStripButton, emojiToolStripButton!);
                AddRailSection("Protect", censorToolStripButton);
                AddRailSection("Transform", freeRotateToolStripButton, straightenToolStripButton);
                ConfigureToolRailButtons();

                foreach (var (button, command) in new[]
                {
                    (cropToolStripButton, Components.Shared.EditorCommandId.CropTool),
                    (rotateToolStripButton, Components.Shared.EditorCommandId.RotateRight),
                    (straightenToolStripButton, Components.Shared.EditorCommandId.StraightenTool),
                    (freeRotateToolStripButton, Components.Shared.EditorCommandId.FreeRotateTool),
                    (censorToolStripButton, Components.Shared.EditorCommandId.CensorTool)
                }) SetCommandTooltip(button, command);

                InitializeViewControls();
            }
            finally
            {
                foreach (var strip in strips) strip.ResumeLayout(true);
            }
        }

        private ToolStripDropDownButton CreateOperationGroup(string label, string tooltip, params ToolStripItem[] commands)
        {
            var group = new ToolStripDropDownButton(label)
            {
                Name = label.ToLowerInvariant() + "Commands",
                ToolTipText = tooltip,
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                AutoToolTip = false
            };
            foreach (var command in commands)
            {
                command.Owner?.Items.Remove(command);
                command.AutoSize = true;
                command.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
                group.DropDownItems.Add(command);
            }
            return group;
        }

        private void AddRailSection(string label, params ToolStripItem[] buttons)
        {
            toolsToolStrip!.Items.Add(new ToolStripSeparator());
            toolsToolStrip.Items.Add(new ToolStripLabel(label)
            {
                ForeColor = SystemColors.GrayText,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false,
                Size = new Size(122, 20),
                Overflow = ToolStripItemOverflow.AsNeeded
            });
            toolsToolStrip.Items.AddRange(buttons);
        }

        private void InitializeViewControls()
        {
            statusStrip.AccessibleName = "Image view";
            statusStrip.Items.Add(new ToolStripSeparator());
            zoomOutViewButton = AddViewButton("−", "Zoom out", () => ZoomAtViewCenter(FindZoomOut(ZoomLevel)));
            zoomInViewButton = AddViewButton("+", "Zoom in", () => ZoomAtViewCenter(FindZoomIn(ZoomLevel)));
            fitViewButton = AddViewButton("Fit", "Fit the image in the viewport", FitImageToCanvas);
            actualSizeViewButton = AddViewButton("100%", "View at actual pixel size", () => ZoomAtViewCenter(1m));
            transparencyViewButton = AddViewButton("Transparency", "Show transparency checkerboard (M)", ToggleAlphaView);
        }

        private ToolStripButton AddViewButton(string text, string tooltip, Action action)
        {
            var button = new ToolStripButton(text)
            {
                ToolTipText = tooltip, DisplayStyle = ToolStripItemDisplayStyle.Text
            };
            button.Click += (_, _) =>
            {
                action();
                pictureBox1.Focus();
            };
            statusStrip.Items.Add(button);
            return button;
        }

        private void ZoomAtViewCenter(decimal zoom)
        {
            pictureBox1.ZoomAround(zoom, new Point(pictureBox1.ClientSize.Width / 2, pictureBox1.ClientSize.Height / 2));
            _zoomlevel = pictureBox1.ZoomLevel;
        }

        private void UpdateEditorCommandLayoutState()
        {
            if (colorCorrectToolStripButton != null) colorCorrectToolStripButton.Enabled = HasEditableImage;
            foreach (var group in new[] { geometryCommands, cleanupCommands, adjustmentCommands })
            {
                if (group != null) group.Enabled = group.DropDownItems.Cast<ToolStripItem>().Any(item => item.Enabled);
            }
            foreach (var button in new[] { zoomOutViewButton, zoomInViewButton, fitViewButton, actualSizeViewButton, transparencyViewButton })
            {
                if (button != null) button.Enabled = HasEditableImage;
            }
            if (transparencyViewButton != null) transparencyViewButton.Checked = pictureBox1.AlphaViewEnabled;
        }
    }
}
