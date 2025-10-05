# Manual Test Checklist

## Undo / Redo

1. Launch Screenzap and capture or paste an image so the editor opens with real content.
2. Drag to create a selection, then press `C` (without modifiers) to apply the censor effect.
3. Press `Ctrl+Z` to undo the change and verify that the selection area returns to its pre-censored state.
4. Press `Ctrl+Shift+Z` to redo and confirm the censored pixels are restored.
5. Repeat steps 2–4 several times to ensure multiple undo/redo steps are recorded correctly and that the selection rectangle is preserved across operations.

## Save / Save As (regression spot check)

1. After editing, press `Ctrl+S` to trigger an automatic timestamped save and confirm the file appears in the configured capture folder.
2. Press `Ctrl+Shift+S`, choose a different destination, and confirm the saved file reflects the current editor state.
