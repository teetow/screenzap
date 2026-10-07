# Editor command placement

The image editor has two horizontal bars and a vertical rail. Place commands by how the user interacts with them:

| Surface | Purpose | Commands |
| --- | --- | --- |
| File bar (first horizontal row) | Save, export, and recover the document | Commit, Save, Save As, Copy, Copy SVG, Reload, Undo, Redo; Find on text documents |
| Image operations (second horizontal row) | Run an operation or open its dialog | Crop, Resize, Expand Canvas, Rotate 90°, Flip Horizontal / Vertical, Replace Background, Color Correction, De-JPEG, Optimize for Text |
| Canvas tool rail | Choose what clicking and dragging on the image does | Move / Select; Annotate: Arrow, Rectangle, Highlighter, Text, Emoji; Protect: Censor; Transform: Free Rotate, Perspective |
| Contextual controls | Configure and finish the active tool or selected object | Font, colors, thickness, dimensions, angle, Apply / Cancel; Merge Layer for a selected image layer |
| History panel | Manage clipboard history entries | Compact direct buttons for Duplicate, Revert, Delete; thumbnail context menus retain their entry actions |
| Bottom view bar | Change how the image is displayed | Zoom out / in, Fit, 100%, transparency checkerboard |
| Settings menu | Configure the application | Capture folder, capture shortcut, startup options, checkerboard colors |

## Image operation groups and access

- **Geometry:** Crop to Selection, Resize Image, Expand Canvas, Rotate 90° Right, Flip Horizontal, Flip Vertical.
- **Cleanup:** Replace Background (requires a pixel selection).
- **Adjustments:** Color Correction, De-JPEG, Optimize for Text.

Perspective is the four-corner correction tool previously called Straighten. Its shortcut remains Ctrl+L. It enters a canvas mode and belongs on the rail. Crop to Selection applies immediately and belongs with geometry operations. The operation bar exposes commands directly, separated into these groups; the Tools menu mirrors the groups for keyboard/menu access.

Keep the rail narrow with icon-only buttons, distinct recognizable icons, accessible names, and tooltips. Common operations and Commit must remain one click away. History buttons stay within the thumbnail pane and do not open a popup over the canvas. Committing preserves zoom, pan, and the transparency view, including after deferred layout work. Narrow or short windows must retain access through overflow. Menus should mirror the same categories; a shortcut is additional access, never the only discoverable entry point. Tool-specific actions stay in their contextual controls rather than the file bar.
