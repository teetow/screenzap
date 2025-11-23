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
            { EditorCommandId.Undo, new EditorCommandDescriptor { Id = EditorCommandId.Undo, Label = "Undo", ToolTip = "Undo (Ctrl+Z)", Icon = IconChar.RotateLeft } },
            { EditorCommandId.Redo, new EditorCommandDescriptor { Id = EditorCommandId.Redo, Label = "Redo", ToolTip = "Redo (Ctrl+Y)", Icon = IconChar.RotateRight } },
            { EditorCommandId.Find, new EditorCommandDescriptor { Id = EditorCommandId.Find, Label = "Find", ToolTip = "Find (Ctrl+F)", Icon = IconChar.MagnifyingGlass } }
        };

        public static IReadOnlyDictionary<EditorCommandId, EditorCommandDescriptor> All => Commands;
    }
}
