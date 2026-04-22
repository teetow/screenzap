using System.Collections.Generic;
using FontAwesome.Sharp;

namespace screenzap.Components.Shared
{
    internal static class EditorCommandCatalog
    {
        private static readonly Dictionary<EditorCommandId, EditorCommandDescriptor> Commands = new()
        {
            { EditorCommandId.Save, new EditorCommandDescriptor { Id = EditorCommandId.Save, Label = "Save", ToolTip = "Save (Ctrl+S)", Icon = IconChar.FloppyDisk } },
            { EditorCommandId.SaveAs, new EditorCommandDescriptor { Id = EditorCommandId.SaveAs, Label = "Save As", ToolTip = "Save As (Ctrl+Shift+S)", Icon = IconChar.FilePen } },
            { EditorCommandId.Copy, new EditorCommandDescriptor { Id = EditorCommandId.Copy, Label = "Copy", ToolTip = "Copy to Clipboard", Icon = IconChar.Clipboard } },
            { EditorCommandId.Reload, new EditorCommandDescriptor { Id = EditorCommandId.Reload, Label = "Reload", ToolTip = "Reload from Clipboard (Ctrl+R)", Icon = IconChar.Rotate } },
            { EditorCommandId.ExpandCanvas, new EditorCommandDescriptor { Id = EditorCommandId.ExpandCanvas, Label = "Expand Canvas", ToolTip = "Expand canvas by 8px with edge padding", Icon = IconChar.Expand } },
            { EditorCommandId.Undo, new EditorCommandDescriptor { Id = EditorCommandId.Undo, Label = "Undo", ToolTip = "Undo (Ctrl+Z)", Icon = IconChar.RotateLeft } },
            { EditorCommandId.Redo, new EditorCommandDescriptor { Id = EditorCommandId.Redo, Label = "Redo", ToolTip = "Redo (Ctrl+Y)", Icon = IconChar.RotateRight } },
            { EditorCommandId.Find, new EditorCommandDescriptor { Id = EditorCommandId.Find, Label = "Find", ToolTip = "Find (Ctrl+F)", Icon = IconChar.MagnifyingGlass } },
            { EditorCommandId.Duplicate, new EditorCommandDescriptor { Id = EditorCommandId.Duplicate, Label = "Duplicate", ToolTip = "Duplicate this item as a new history entry", Icon = IconChar.Clone } },
            { EditorCommandId.Revert, new EditorCommandDescriptor { Id = EditorCommandId.Revert, Label = "Revert", ToolTip = "Revert to the original clipboard content", Icon = IconChar.ArrowRotateLeft } },
            { EditorCommandId.CommitEdits, new EditorCommandDescriptor { Id = EditorCommandId.CommitEdits, Label = "Commit", ToolTip = "Accept edits: push to clipboard and mark clean (undo stack preserved)", Icon = IconChar.Check } },
            { EditorCommandId.Delete, new EditorCommandDescriptor { Id = EditorCommandId.Delete, Label = "Delete", ToolTip = "Remove this item from history", Icon = IconChar.Trash } }
        };

        public static IReadOnlyDictionary<EditorCommandId, EditorCommandDescriptor> All => Commands;
    }
}
