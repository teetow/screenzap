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
        Highlighter,
        Text,
        Censor,
        Straighten,
        FreeRotate
    }

    public partial class ImageEditor
    {
        private ActiveTool activeTool = ActiveTool.None;
        private bool insideSetActiveTool;

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

        private bool isFreeRotateToolActive
        {
            get => activeTool == ActiveTool.FreeRotate;
            set
            {
                if (value) SetActiveTool(ActiveTool.FreeRotate);
                else if (activeTool == ActiveTool.FreeRotate) SetActiveTool(ActiveTool.None);
            }
        }

        private DrawingTool activeDrawingTool
        {
            get => activeTool switch
            {
                ActiveTool.Arrow => DrawingTool.Arrow,
                ActiveTool.Rectangle => DrawingTool.Rectangle,
                ActiveTool.Highlighter => DrawingTool.Highlighter,
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
                    case DrawingTool.Highlighter:
                        SetActiveTool(ActiveTool.Highlighter);
                        break;
                    case DrawingTool.None:
                        // Old field semantics: assigning DrawingTool.None unconditionally
                        // cleared this slot. Preserve that, but don't touch unrelated tools.
                        if (activeTool is ActiveTool.Arrow or ActiveTool.Rectangle or ActiveTool.Highlighter)
                        {
                            SetActiveTool(ActiveTool.None);
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Rail button for ActiveTool.None. Switching to Move cancels whatever tool is
        /// engaged — SetActiveTool runs the previous tool's deactivator with apply=false,
        /// so this is the click equivalent of walking the Escape ladder to the bottom.
        /// </summary>
        private void moveToolStripButton_Click(object? sender, EventArgs e)
        {
            SetActiveTool(ActiveTool.None);
            pictureBox1?.Focus();
        }

        private void UpdateMoveToolButton()
        {
            if (moveToolStripButton != null)
            {
                moveToolStripButton.Checked = activeTool == ActiveTool.None;
            }
        }

        /// <summary>
        /// Central tool switcher. Tears down the previously active tool (finalizing
        /// in-flight edits, hiding overlay toolstrips) before flipping the flag, so
        /// activating tool B from tool A always leaves A's UI consistent — even when
        /// the new tool was engaged via an Activate*() that doesn't know about A.
        /// </summary>
        private void SetActiveTool(ActiveTool next)
        {
            if (insideSetActiveTool)
            {
                // Re-entrant call from a deactivator clearing its own flag — just
                // record the value and let the outer call finish the transition.
                activeTool = next;
                return;
            }

            if (activeTool == next)
            {
                return;
            }

            insideSetActiveTool = true;
            try
            {
                var previous = activeTool;

                // Run the previous tool's deactivator while its flag is still set
                // (deactivators may guard on it).
                switch (previous)
                {
                    case ActiveTool.Text:
                        FinalizeActiveTextAnnotation();
                        break;
                    case ActiveTool.Arrow:
                    case ActiveTool.Rectangle:
                    case ActiveTool.Highlighter:
                        CancelAnnotationPreview();
                        break;
                    case ActiveTool.Censor:
                        DeactivateCensorTool(false);
                        break;
                    case ActiveTool.Straighten:
                        DeactivateStraightenTool(false);
                        break;
                    case ActiveTool.FreeRotate:
                        DeactivateFreeRotateTool(false);
                        break;
                }

                activeTool = next;

                switch (previous)
                {
                    case ActiveTool.Text:
                        UpdateTextToolButtons();
                        UpdateTextToolbarVisibility();
                        break;
                    case ActiveTool.Arrow:
                    case ActiveTool.Rectangle:
                    case ActiveTool.Highlighter:
                        UpdateDrawingToolButtons();
                        break;
                }
            }
            finally
            {
                insideSetActiveTool = false;
                UpdateMoveToolButton();
            }
        }
    }
}
