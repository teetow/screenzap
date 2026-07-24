using System.Collections.Generic;
using System.Windows.Forms;
using FontAwesome.Sharp;

namespace screenzap.Components.Shared
{
    internal static class EditorCommandCatalog
    {
        private static readonly Dictionary<EditorCommandId, EditorCommandDescriptor> Commands = new()
        {
            { EditorCommandId.Save, new EditorCommandDescriptor { Id = EditorCommandId.Save, Label = "Save", ToolTip = "Save", Icon = IconChar.FloppyDisk, Shortcut = Keys.Control | Keys.S } },
            { EditorCommandId.SaveAs, new EditorCommandDescriptor { Id = EditorCommandId.SaveAs, Label = "Save As", ToolTip = "Save As", Icon = IconChar.FilePen, Shortcut = Keys.Control | Keys.Shift | Keys.S } },
            { EditorCommandId.Copy, new EditorCommandDescriptor { Id = EditorCommandId.Copy, Label = "Copy", ToolTip = "Copy to Clipboard", Icon = IconChar.Clipboard, Shortcut = Keys.Control | Keys.C } },
            { EditorCommandId.Reload, new EditorCommandDescriptor { Id = EditorCommandId.Reload, Label = "Reload", ToolTip = "Reload from Clipboard", Icon = IconChar.Rotate, Shortcut = Keys.Control | Keys.R } },
            { EditorCommandId.ExpandCanvas, new EditorCommandDescriptor { Id = EditorCommandId.ExpandCanvas, Label = "Expand Canvas", ToolTip = "Expand canvas by 8px with edge padding", Icon = IconChar.Expand, Shortcut = Keys.Control | Keys.Shift | Keys.E } },
            { EditorCommandId.Undo, new EditorCommandDescriptor { Id = EditorCommandId.Undo, Label = "Undo", ToolTip = "Undo", Icon = IconChar.RotateLeft, Shortcut = Keys.Control | Keys.Z } },
            { EditorCommandId.Redo, new EditorCommandDescriptor { Id = EditorCommandId.Redo, Label = "Redo", ToolTip = "Redo", Icon = IconChar.RotateRight, Shortcut = Keys.Control | Keys.Shift | Keys.Z } },
            { EditorCommandId.Find, new EditorCommandDescriptor { Id = EditorCommandId.Find, Label = "Find", ToolTip = "Find", Icon = IconChar.MagnifyingGlass, Shortcut = Keys.Control | Keys.F } },
            { EditorCommandId.Duplicate, new EditorCommandDescriptor { Id = EditorCommandId.Duplicate, Label = "Duplicate", ToolTip = "Duplicate this item as a new history entry", Icon = IconChar.Clone } },
            { EditorCommandId.Revert, new EditorCommandDescriptor { Id = EditorCommandId.Revert, Label = "Revert", ToolTip = "Revert to the original clipboard content (Ctrl+Z restores your edits)", Icon = IconChar.ArrowRotateLeft } },
            { EditorCommandId.CommitEdits, new EditorCommandDescriptor { Id = EditorCommandId.CommitEdits, Label = "Commit", ToolTip = "Accept edits: push to clipboard and mark clean (undo stack preserved)", Icon = IconChar.Check } },
            { EditorCommandId.Delete, new EditorCommandDescriptor { Id = EditorCommandId.Delete, Label = "Delete", ToolTip = "Remove this item from history", Icon = IconChar.Trash } },
            { EditorCommandId.ApplyFloatingPaste, new EditorCommandDescriptor { Id = EditorCommandId.ApplyFloatingPaste, Label = "Apply", ToolTip = "Apply floating paste: burn layer(s) into the pixel buffer", Icon = IconChar.Stamp, Shortcut = Keys.Enter } },
            { EditorCommandId.ToggleTransparencyGrid, new EditorCommandDescriptor { Id = EditorCommandId.ToggleTransparencyGrid, Label = "Transparency Grid", ToolTip = "Toggle the transparency checkerboard (show alpha vs. flatten opaque)", Icon = IconChar.ChessBoard, Shortcut = Keys.M } },

            { EditorCommandId.SelectMoveTool, new EditorCommandDescriptor { Id = EditorCommandId.SelectMoveTool, Label = "Move / Select", ToolTip = "Move / select tool", Icon = IconChar.ArrowPointer } },
            { EditorCommandId.ArrowTool, new EditorCommandDescriptor { Id = EditorCommandId.ArrowTool, Label = "Arrow", ToolTip = "Draw arrows", Icon = IconChar.ArrowRightLong } },
            { EditorCommandId.RectangleTool, new EditorCommandDescriptor { Id = EditorCommandId.RectangleTool, Label = "Rectangle", ToolTip = "Draw rectangles", Icon = IconChar.VectorSquare } },
            { EditorCommandId.HighlighterTool, new EditorCommandDescriptor { Id = EditorCommandId.HighlighterTool, Label = "Highlighter", ToolTip = "Freehand highlighter", Icon = IconChar.Highlighter } },
            { EditorCommandId.TextTool, new EditorCommandDescriptor { Id = EditorCommandId.TextTool, Label = "Text", ToolTip = "Add text", Icon = IconChar.Font } },
            { EditorCommandId.CropTool, new EditorCommandDescriptor { Id = EditorCommandId.CropTool, Label = "Crop", ToolTip = "Crop to selection", Icon = IconChar.CropSimple, Shortcut = Keys.Control | Keys.T } },
            { EditorCommandId.RotateRight, new EditorCommandDescriptor { Id = EditorCommandId.RotateRight, Label = "Rotate 90° Right", ToolTip = "Rotate 90° clockwise", Icon = IconChar.ArrowRotateRight } },
            { EditorCommandId.FlipHorizontal, new EditorCommandDescriptor { Id = EditorCommandId.FlipHorizontal, Label = "Flip Horizontal", ToolTip = "Flip horizontally", Icon = IconChar.LeftRight } },
            { EditorCommandId.FlipVertical, new EditorCommandDescriptor { Id = EditorCommandId.FlipVertical, Label = "Flip Vertical", ToolTip = "Flip vertically", Icon = IconChar.UpDown } },
            { EditorCommandId.StraightenTool, new EditorCommandDescriptor { Id = EditorCommandId.StraightenTool, Label = "Straighten", ToolTip = "Auto-detect and correct rotation/perspective", Icon = IconChar.Rotate, Shortcut = Keys.Control | Keys.L } },
            { EditorCommandId.FreeRotateTool, new EditorCommandDescriptor { Id = EditorCommandId.FreeRotateTool, Label = "Free Rotate", ToolTip = "Rotate the image or selection by any angle", Icon = IconChar.ArrowsSpin } },
            { EditorCommandId.ResizeImage, new EditorCommandDescriptor { Id = EditorCommandId.ResizeImage, Label = "Resize Image...", ToolTip = "Resize the image", Icon = IconChar.Expand } },
            { EditorCommandId.CensorTool, new EditorCommandDescriptor { Id = EditorCommandId.CensorTool, Label = "Censor", ToolTip = "Detect text and censor selections", Icon = IconChar.UserSecret, Shortcut = Keys.Control | Keys.E } },
            { EditorCommandId.ReplaceBackground, new EditorCommandDescriptor { Id = EditorCommandId.ReplaceBackground, Label = "Replace Background", ToolTip = "Replace the background", Icon = IconChar.Eraser, Shortcut = Keys.Control | Keys.B } },
            { EditorCommandId.ColorCorrect, new EditorCommandDescriptor { Id = EditorCommandId.ColorCorrect, Label = "Color Correct...", ToolTip = "Adjust color / levels", Icon = IconChar.Palette } },
            { EditorCommandId.OptimizeText, new EditorCommandDescriptor { Id = EditorCommandId.OptimizeText, Label = "Optimize for Text", ToolTip = "Sharpen and threshold for legible text", Icon = IconChar.Magic } }
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
