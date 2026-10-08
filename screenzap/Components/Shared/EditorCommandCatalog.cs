using System.Collections.Generic;
using System.Windows.Forms;

namespace screenzap.Components.Shared
{
    internal static class EditorCommandCatalog
    {
        private static readonly Dictionary<EditorCommandId, EditorCommandDescriptor> Commands = new()
        {
            { EditorCommandId.Save, new EditorCommandDescriptor { Id = EditorCommandId.Save, Label = "Save", ToolTip = "Save", Shortcut = Keys.Control | Keys.S } },
            { EditorCommandId.SaveAs, new EditorCommandDescriptor { Id = EditorCommandId.SaveAs, Label = "Save As", ToolTip = "Save As", Shortcut = Keys.Control | Keys.Shift | Keys.S } },
            { EditorCommandId.Copy, new EditorCommandDescriptor { Id = EditorCommandId.Copy, Label = "Copy", ToolTip = "Copy to Clipboard", Shortcut = Keys.Control | Keys.C } },
            { EditorCommandId.Reload, new EditorCommandDescriptor { Id = EditorCommandId.Reload, Label = "Reload", ToolTip = "Reload from Clipboard", Shortcut = Keys.Control | Keys.R } },
            { EditorCommandId.ExpandCanvas, new EditorCommandDescriptor { Id = EditorCommandId.ExpandCanvas, Label = "Expand Canvas", ToolTip = "Expand canvas by 8px with edge padding", Shortcut = Keys.Control | Keys.Shift | Keys.E } },
            { EditorCommandId.Undo, new EditorCommandDescriptor { Id = EditorCommandId.Undo, Label = "Undo", ToolTip = "Undo", Shortcut = Keys.Control | Keys.Z } },
            { EditorCommandId.Redo, new EditorCommandDescriptor { Id = EditorCommandId.Redo, Label = "Redo", ToolTip = "Redo", Shortcut = Keys.Control | Keys.Shift | Keys.Z } },
            { EditorCommandId.Find, new EditorCommandDescriptor { Id = EditorCommandId.Find, Label = "Find", ToolTip = "Find", Shortcut = Keys.Control | Keys.F } },
            { EditorCommandId.Duplicate, new EditorCommandDescriptor { Id = EditorCommandId.Duplicate, Label = "Duplicate", ToolTip = "Duplicate this item as a new history entry" } },
            { EditorCommandId.Revert, new EditorCommandDescriptor { Id = EditorCommandId.Revert, Label = "Revert", ToolTip = "Revert to the original clipboard content (Ctrl+Z restores your edits)" } },
            { EditorCommandId.CommitEdits, new EditorCommandDescriptor { Id = EditorCommandId.CommitEdits, Label = "Commit", ToolTip = "Accept edits: push to clipboard and mark clean (undo stack preserved)", Shortcut = Keys.Control | Keys.Enter } },
            { EditorCommandId.Delete, new EditorCommandDescriptor { Id = EditorCommandId.Delete, Label = "Delete", ToolTip = "Remove this item from history" } },
            { EditorCommandId.ApplyFloatingPaste, new EditorCommandDescriptor { Id = EditorCommandId.ApplyFloatingPaste, Label = "Merge Layer", ToolTip = "Merge the selected layer into the image pixels", Shortcut = Keys.Enter } },
            { EditorCommandId.ToggleTransparencyGrid, new EditorCommandDescriptor { Id = EditorCommandId.ToggleTransparencyGrid, Label = "Transparency Grid", ToolTip = "Toggle the transparency checkerboard (show alpha vs. flatten opaque)", Shortcut = Keys.M } },

            { EditorCommandId.SelectMoveTool, new EditorCommandDescriptor { Id = EditorCommandId.SelectMoveTool, Label = "Move / Select", ToolTip = "Move / select tool" } },
            { EditorCommandId.ArrowTool, new EditorCommandDescriptor { Id = EditorCommandId.ArrowTool, Label = "Arrow", ToolTip = "Draw arrows" } },
            { EditorCommandId.RectangleTool, new EditorCommandDescriptor { Id = EditorCommandId.RectangleTool, Label = "Rectangle", ToolTip = "Draw rectangles" } },
            { EditorCommandId.HighlighterTool, new EditorCommandDescriptor { Id = EditorCommandId.HighlighterTool, Label = "Highlighter", ToolTip = "Freehand highlighter" } },
            { EditorCommandId.TextTool, new EditorCommandDescriptor { Id = EditorCommandId.TextTool, Label = "Text", ToolTip = "Add text" } },
            { EditorCommandId.CropTool, new EditorCommandDescriptor { Id = EditorCommandId.CropTool, Label = "Crop to Selection", ToolTip = "Crop to selection", Shortcut = Keys.Control | Keys.T } },
            { EditorCommandId.RotateRight, new EditorCommandDescriptor { Id = EditorCommandId.RotateRight, Label = "Rotate 90° Right", ToolTip = "Rotate 90° clockwise" } },
            { EditorCommandId.FlipHorizontal, new EditorCommandDescriptor { Id = EditorCommandId.FlipHorizontal, Label = "Flip Horizontal", ToolTip = "Flip horizontally" } },
            { EditorCommandId.FlipVertical, new EditorCommandDescriptor { Id = EditorCommandId.FlipVertical, Label = "Flip Vertical", ToolTip = "Flip vertically" } },
            { EditorCommandId.StraightenTool, new EditorCommandDescriptor { Id = EditorCommandId.StraightenTool, Label = "Perspective", ToolTip = "Adjust four corners to crop and correct perspective", Shortcut = Keys.Control | Keys.L } },
            { EditorCommandId.FreeRotateTool, new EditorCommandDescriptor { Id = EditorCommandId.FreeRotateTool, Label = "Free Rotate", ToolTip = "Rotate the image or selection by any angle" } },
            { EditorCommandId.DeJpeg, new EditorCommandDescriptor { Id = EditorCommandId.DeJpeg, Label = "De-JPEG", ToolTip = "Remove JPEG artifacts" } },
            { EditorCommandId.ResizeImage, new EditorCommandDescriptor { Id = EditorCommandId.ResizeImage, Label = "Resize Image...", ToolTip = "Resize the image" } },
            { EditorCommandId.CensorTool, new EditorCommandDescriptor { Id = EditorCommandId.CensorTool, Label = "Censor", ToolTip = "Detect text and censor selections", Shortcut = Keys.Control | Keys.E } },
            { EditorCommandId.ReplaceBackground, new EditorCommandDescriptor { Id = EditorCommandId.ReplaceBackground, Label = "Replace Background", ToolTip = "Replace the background", Shortcut = Keys.Control | Keys.B } },
            { EditorCommandId.ColorCorrect, new EditorCommandDescriptor { Id = EditorCommandId.ColorCorrect, Label = "Color Correct...", ToolTip = "Adjust color / levels" } },
            { EditorCommandId.EmojiTool, new EditorCommandDescriptor { Id = EditorCommandId.EmojiTool, Label = "Emoji", ToolTip = "Drag emoji onto the image" } },
            { EditorCommandId.FitImageToView, new EditorCommandDescriptor { Id = EditorCommandId.FitImageToView, Label = "Fit Image", ToolTip = "Fit the image in the viewport" } },
            { EditorCommandId.CopySvgPoster, new EditorCommandDescriptor { Id = EditorCommandId.CopySvgPoster, Label = "Poster (logos, icons)", ToolTip = "Copy the image as SVG: logos and flat graphics" } },
            { EditorCommandId.CopySvgPhoto, new EditorCommandDescriptor { Id = EditorCommandId.CopySvgPhoto, Label = "Photo (complex images)", ToolTip = "Copy the image as SVG: photos and complex images" } },
            { EditorCommandId.CopySvgBlackAndWhite, new EditorCommandDescriptor { Id = EditorCommandId.CopySvgBlackAndWhite, Label = "B&&W (line art, sketches)", ToolTip = "Copy the image as SVG: black and white line art" } },
            { EditorCommandId.OptimizeText, new EditorCommandDescriptor { Id = EditorCommandId.OptimizeText, Label = "Optimize for Text", ToolTip = "Sharpen and threshold for legible text" } }
        };

        public static IReadOnlyDictionary<EditorCommandId, EditorCommandDescriptor> All => Commands;

        public static string FormatTooltip(EditorCommandDescriptor descriptor)
        {
            if (descriptor.Shortcut is not Keys shortcut)
            {
                return descriptor.ToolTip;
            }
            return $"{descriptor.ToolTip} ({FormatShortcut(shortcut)})";
        }

        public static string FormatShortcut(Keys shortcut)
        {
            var parts = new List<string>();
            if ((shortcut & Keys.Control) == Keys.Control) parts.Add("Ctrl");
            if ((shortcut & Keys.Shift) == Keys.Shift) parts.Add("Shift");
            if ((shortcut & Keys.Alt) == Keys.Alt) parts.Add("Alt");
            var key = shortcut & Keys.KeyCode;
            parts.Add(key.ToString());
            return string.Join("+", parts);
        }
    }
}
