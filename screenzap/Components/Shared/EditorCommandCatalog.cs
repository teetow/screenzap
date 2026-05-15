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
            { EditorCommandId.Revert, new EditorCommandDescriptor { Id = EditorCommandId.Revert, Label = "Revert", ToolTip = "Revert to the original clipboard content", Icon = IconChar.ArrowRotateLeft } },
            { EditorCommandId.CommitEdits, new EditorCommandDescriptor { Id = EditorCommandId.CommitEdits, Label = "Commit", ToolTip = "Accept edits: push to clipboard and mark clean (undo stack preserved)", Icon = IconChar.Check } },
            { EditorCommandId.Delete, new EditorCommandDescriptor { Id = EditorCommandId.Delete, Label = "Delete", ToolTip = "Remove this item from history", Icon = IconChar.Trash } },
            { EditorCommandId.ApplyFloatingPaste, new EditorCommandDescriptor { Id = EditorCommandId.ApplyFloatingPaste, Label = "Apply", ToolTip = "Apply floating paste: burn layer(s) into the pixel buffer", Icon = IconChar.Stamp, Shortcut = Keys.Enter } }
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
