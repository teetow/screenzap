using System;

namespace screenzap.Components.Shared
{
    internal sealed class EditorHostServices
    {
        public Action<bool>? SetReloadIndicator { get; init; }
        public Func<bool>? RequestClipboardReload { get; init; }
        public Action<string?>? UpdateStatusText { get; init; }
        public Action? FocusHost { get; init; }
        public Func<IClipboardDocumentPresenter, bool>? ActivatePresenter { get; init; }
    }
}
