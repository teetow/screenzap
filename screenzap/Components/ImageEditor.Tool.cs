using System;

namespace screenzap
{
    /// <summary>
    /// The currently engaged editor tool. Exactly one tool is active at any time;
    /// activating a new tool deactivates the previous one through <see cref="ImageEditor.SetActiveTool"/>.
    /// </summary>
    internal enum ActiveTool
    {
        None,
        Arrow,
        Rectangle,
        Text,
        Censor,
        Straighten
    }

    public partial class ImageEditor
    {
        private ActiveTool activeTool = ActiveTool.None;

        internal ActiveTool CurrentTool => activeTool;

        // Computed accessors keep legacy field-style call sites working while routing
        // every state change through SetActiveTool. Setters preserve the prior behavior
        // where assigning `false` only clears state if THIS tool was the active one
        // (assigning `false` to an inactive tool was a no-op in the old code).
        private bool isTextToolActive
        {
            get => activeTool == ActiveTool.Text;
            set
            {
                if (value) SetActiveTool(ActiveTool.Text);
                else if (activeTool == ActiveTool.Text) SetActiveTool(ActiveTool.None);
            }
        }

        private bool isCensorToolActive
        {
            get => activeTool == ActiveTool.Censor;
            set
            {
                if (value) SetActiveTool(ActiveTool.Censor);
                else if (activeTool == ActiveTool.Censor) SetActiveTool(ActiveTool.None);
            }
        }

        private bool isStraightenToolActive
        {
            get => activeTool == ActiveTool.Straighten;
            set
            {
                if (value) SetActiveTool(ActiveTool.Straighten);
                else if (activeTool == ActiveTool.Straighten) SetActiveTool(ActiveTool.None);
            }
        }

        private DrawingTool activeDrawingTool
        {
            get => activeTool switch
            {
                ActiveTool.Arrow => DrawingTool.Arrow,
                ActiveTool.Rectangle => DrawingTool.Rectangle,
                _ => DrawingTool.None
            };
            set
            {
                switch (value)
                {
                    case DrawingTool.Arrow:
                        SetActiveTool(ActiveTool.Arrow);
                        break;
                    case DrawingTool.Rectangle:
                        SetActiveTool(ActiveTool.Rectangle);
                        break;
                    case DrawingTool.None:
                        // Old field semantics: assigning DrawingTool.None unconditionally
                        // cleared this slot. Preserve that, but don't touch unrelated tools.
                        if (activeTool is ActiveTool.Arrow or ActiveTool.Rectangle)
                        {
                            SetActiveTool(ActiveTool.None);
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Central tool switcher. Just flips the state; per-tool cleanup (finalizing
        /// in-flight edits, hiding overlay toolstrips, refreshing button checked-state)
        /// is the responsibility of the existing per-tool Toggle/Activate/Deactivate
        /// methods. This keeps the existing call-site contracts intact.
        /// </summary>
        private void SetActiveTool(ActiveTool next)
        {
            if (activeTool == next)
            {
                return;
            }

            activeTool = next;
        }
    }
}
