# Editor command placement

The image editor has two horizontal bars and a vertical rail. Place commands by how the user interacts with them:

| Surface | Purpose | Commands |
| --- | --- | --- |
| File bar (first horizontal row) | Save, export, and recover the document | Save, Save As, Copy, Copy SVG, Reload, Undo, Redo; Find on text documents |
| Image operations (second horizontal row) | Run an operation or open its dialog | Geometry, Cleanup, Adjustments |
| Canvas tool rail | Choose what clicking and dragging on the image does | Move / Select; Annotate: Arrow, Rectangle, Highlighter, Text, Emoji; Protect: Censor; Transform: Free Rotate, Perspective |
| Contextual controls | Configure and finish the active tool or selected object | Font, colors, thickness, dimensions, angle, Apply / Cancel; Merge Layer for a selected image layer |
| History panel | Manage clipboard history entries | Commit, Duplicate, Revert, Delete; thumbnail context menus retain their entry actions |
| Bottom view bar | Change how the image is displayed | Zoom out / in, Fit, 100%, transparency checkerboard |
| Settings menu | Configure the application | Capture folder, capture shortcut, startup options, checkerboard colors |

## Image operation groups

- **Geometry:** Crop to Selection, Resize Image, Expand Canvas, Rotate 90° Right, Flip Horizontal, Flip Vertical.
- **Cleanup:** Replace Background (requires a pixel selection).
- **Adjustments:** Color Correction, De-JPEG, Optimize for Text.

Perspective is the four-corner correction tool previously called Straighten. Its shortcut remains Ctrl+L. It enters a canvas mode and belongs on the rail. Crop to Selection applies immediately and belongs under Geometry.

Keep rail labels visible and use distinct icons. Narrow or short windows must retain access through overflow. Menus should mirror the same categories; a shortcut is additional access, never the only discoverable entry point. Tool-specific actions stay in their contextual controls rather than the file bar.
