using System;
using System.Windows.Forms;

namespace screenzap.Components.Shared
{
    internal enum EditorCommandId
    {
        Save,
        SaveAs,
        Copy,
        Reload,
        ExpandCanvas,
        Undo,
        Redo,
        Find,
        Duplicate,
        Revert,
        CommitEdits,
        Delete,
        ApplyFloatingPaste,
        ToggleTransparencyGrid,

        // Tools (mirror the editor tool rail / transform toolbar in the Tools menu).
        SelectMoveTool,
        ArrowTool,
        RectangleTool,
        HighlighterTool,
        TextTool,
        CropTool,
        RotateRight,
        FlipHorizontal,
        FlipVertical,
        StraightenTool,
        FreeRotateTool,
        ResizeImage,
        CensorTool,
        ReplaceBackground,
        ColorCorrect,
        OptimizeText
    }

    internal sealed class EditorCommandDescriptor
    {
        public EditorCommandId Id { get; init; }
        public string Label { get; init; } = string.Empty;
        public string ToolTip { get; init; } = string.Empty;
        public FontAwesome.Sharp.IconChar Icon { get; init; }
        public Keys? Shortcut { get; init; }
    }
}
