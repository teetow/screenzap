# Outstanding TODOs

## Image editor enhancements
- [ ] Implement the `Ctrl+S` shortcut in `Components/ImageEditor.cs` (within `ImageEditor_KeyDown`). The handler block is empty, so saving the edited image or selection never occurs.

## Secondary hotkey configurability
- [ ] Add UI to expose and edit the instant-capture hotkey loaded from `Properties.Settings.Default.seqCaptureCombo` in `Screenzap.cs`. Users cannot change or discover this shortcut today, so the convenience feature is only half-delivered.
