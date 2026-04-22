using System;
using System.Drawing;
using System.Windows.Forms;
using screenzap.Components;

namespace screenzap.Components.Shared
{
    internal interface IClipboardDocumentPresenter : IDisposable
    {
        Control View { get; }
        string DisplayName { get; }
        void AttachHostServices(EditorHostServices services);
        bool CanHandleClipboard(IDataObject dataObject);
        void LoadFromClipboard(IDataObject dataObject);
        bool CanExecute(EditorCommandId commandId);
        bool TryExecute(EditorCommandId commandId);
        void OnActivated();
        void OnDeactivated();

        /// <summary>True if this presenter handles the given history item's content kind.</summary>
        bool CanPresent(ClipboardHistoryItem item);

        /// <summary>Load the given history item into the presenter and restore any stashed state.</summary>
        void LoadHistoryItem(ClipboardHistoryItem item);

        /// <summary>Snapshot transient editor state (e.g. undo stack, current image) into the item before switching away.</summary>
        void StashHistoryItemState(ClipboardHistoryItem item);

        /// <summary>The current content rendered by the presenter, or null if nothing is loaded. Caller owns the returned bitmap (for images).</summary>
        object? GetCurrentContent();
    }
}

