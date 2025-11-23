using System;

namespace screenzap.Components.Shared
{
    internal enum EditorCommandId
    {
        Save,
        SaveAs,
        Copy,
        Reload,
        Undo,
        Redo,
        Find
    }

    internal sealed class EditorCommandDescriptor
    {
        public EditorCommandId Id { get; init; }
        public string Label { get; init; } = string.Empty;
        public string ToolTip { get; init; } = string.Empty;
        public FontAwesome.Sharp.IconChar Icon { get; init; }
    }
}
