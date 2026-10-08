using System;
using System.Drawing;
using System.Windows.Forms;
using screenzap.Components;

namespace screenzap.Components.Shared
{
    internal interface IClipboardDocumentPresenter : IDisposable
    {
        string DisplayName { get; }
        void AttachHostServices(EditorHostServices services);
        bool CanHandleClipboard(IDataObject dataObject);
        void LoadFromClipboard(IDataObject dataObject);
        bool CanExecute(EditorCommandId commandId);
        bool TryExecute(EditorCommandId commandId);

        /// <summary>True if this presenter handles the given history item's content kind.</summary>
        bool CanPresent(ClipboardHistoryItem item);

        /// <summary>Load the given history item into the presenter and restore any stashed state.</summary>
        void LoadHistoryItem(ClipboardHistoryItem item);

        /// <summary>
        /// Copy the presenter's live document into the item. Idempotent and non-destructive: the
        /// presenter keeps everything it had, so this is safe to run repeatedly while the user
        /// is still working, and the host does exactly that before every save. Presenters with
        /// no live state of their own can leave it a no-op.
        /// </summary>
        void CaptureLiveStateInto(ClipboardHistoryItem item)
        {
        }

        /// <summary>
        /// Capture, then hand over anything the presenter can only give away once — the undo
        /// stack is a move rather than a copy — because the presenter is leaving this item.
        /// Only call this when switching away from the item; on any path where editing
        /// continues use <see cref="CaptureLiveStateInto"/>, which takes nothing with it.
        /// </summary>
        void StashHistoryItemState(ClipboardHistoryItem item);

        /// <summary>The current content rendered by the presenter, or null if nothing is loaded. Caller owns the returned bitmap (for images).</summary>
        object? GetCurrentContent();

        /// <summary>Fit the image to the current viewport without enlarging it beyond 1:1.</summary>
        void FitContentToView()
        {
        }
    }
}

