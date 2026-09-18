using System;
using System.Drawing;
using System.Windows.Forms;
using screenzap.Components;

namespace screenzap.Components.Shared
{
    /// <summary>
    /// A presenter's natural size, with the part that can be scaled down to fit (an image's pixel
    /// dimensions) kept apart from the part that cannot (the rails and strips the presenter draws
    /// around it). The host needs them separately because it often cannot grant the full size: when
    /// it caps the window at a share of the screen, only <see cref="Content"/> gives, so sizing the
    /// other axis for the unscaled content would wrap the picture in a band of empty canvas.
    /// </summary>
    internal readonly struct PresenterContentSize
    {
        public PresenterContentSize(Size content, Size chrome)
        {
            Content = content;
            Chrome = chrome;
        }

        /// <summary>The scalable content, in its own pixels.</summary>
        public Size Content { get; }

        /// <summary>Fixed furniture the presenter puts around the content, in client pixels.</summary>
        public Size Chrome { get; }

        /// <summary>Client size that shows <see cref="Content"/> at 1:1.</summary>
        public Size Total => new Size(Content.Width + Chrome.Width, Content.Height + Chrome.Height);
    }

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

        /// <summary>
        /// What the presenter would like to be shown at, or null when it has no opinion (e.g. text
        /// content), in which case the host should leave its size alone.
        /// </summary>
        PresenterContentSize? GetNaturalContentSize();

        /// <summary>
        /// Scale the content down until all of it is visible in the view the presenter currently
        /// has. Only ever zooms out — content that already fits is left at 1:1 rather than being
        /// blown up to fill the view. The host calls this whenever the view's size or its content
        /// changes, because <see cref="GetNaturalContentSize"/> is a request the host is free to
        /// refuse: the window is capped at a share of the screen, and a window the user has sized
        /// themselves is not resized at all. Presenters with nothing to scale leave it a no-op.
        /// </summary>
        void FitContentToView()
        {
        }
    }
}

