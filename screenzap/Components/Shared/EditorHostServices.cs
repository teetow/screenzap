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
        /// <summary>Called by a presenter when the user has edited the loaded document (after hasUnsavedChanges flips to true).</summary>
        public Action? NotifyContentEdited { get; init; }
    }
}

