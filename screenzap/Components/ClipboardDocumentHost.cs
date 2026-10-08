using screenzap.Components.Shared;
using screenzap.lib;
using System.Drawing;
using System.Windows.Forms;

namespace screenzap.Components;
internal sealed class ClipboardDocumentHost : IDisposable
{

    public bool IsDisposed { get; private set; }

    internal event Action? StateChanged;
    internal string? CurrentStatusText { get; private set; }

    public ClipboardDocumentHost(params IClipboardDocumentPresenter[] presenters) : this(presenters, null, true)
    {
    }

    internal ClipboardDocumentHost(bool disablePersistenceForDiagnostics, params IClipboardDocumentPresenter[] presenters) : this(presenters, null, !disablePersistenceForDiagnostics, !disablePersistenceForDiagnostics, !disablePersistenceForDiagnostics)
    {
    }

    internal ClipboardDocumentHost(IEnumerable<IClipboardDocumentPresenter>? documents, ClipboardHistoryPersistence? persistence, bool restorePersistedHistory, bool persistHistoryChanges = true, bool allowSystemClipboardWrites = true)
    {
        historyStore = new ClipboardHistoryStore();
        historyPersistence = persistence ?? new ClipboardHistoryPersistence();
        this.persistHistoryChanges = persistHistoryChanges;
        this.allowSystemClipboardWrites = allowSystemClipboardWrites;
        persistenceSaveTimer = new System.Windows.Forms.Timer
        {
            Interval = 350
        };
        persistenceSaveTimer.Tick += OnPersistenceSaveTimerTick;
        hostServices = new EditorHostServices
        {
            SetReloadIndicator = UpdateReloadIndicator,
            RequestClipboardReload = () => ExecuteCommand(EditorCommandId.Reload),
            UpdateStatusText = UpdateStatusText,
            FocusHost = ShowAndActivate,
            ActivatePresenter = ActivatePresenter,
            NotifyContentEdited = OnActivePresenterContentEdited
        };
        if (restorePersistedHistory)
            RestorePersistedHistory();
        historyStore.ItemUpdated += OnStoreItemUpdated;
        historyStore.Changed += OnStoreChanged;
        historyStore.ActiveItemChanged += OnActiveItemChanged;
        foreach (var document in documents ?? Array.Empty<IClipboardDocumentPresenter>())
            AddPresenter(document);
        if (!ActivatePreferredHistoryItem() && this.presenters.FirstOrDefault()is { } first)
            ActivatePresenter(first);
    }

    public void AddPresenter(IClipboardDocumentPresenter presenter)
    {
        if (!presenters.Contains(presenter))
            presenters.Add(presenter);
        presenter.AttachHostServices(hostServices);
    }

    private bool ActivatePresenter(IClipboardDocumentPresenter presenter)
    {
        AddPresenter(presenter);
        activePresenter = presenter;
        return true;
    }


    internal void ShowAndActivate() => ExternalActivateRequested?.Invoke();
    private void UpdateCommandStates() => StateChanged?.Invoke();
    private void UpdateReloadIndicator(bool pending)
    {
        hasPendingReloadIndicator = pending;
        StateChanged?.Invoke();
    }

    private void UpdateStatusText(string? text)
    {
        CurrentStatusText = text;
        StateChanged?.Invoke();
    }

    internal bool HandleShortcut(Keys keys) => keys switch
    {
        Keys.Control | Keys.PageUp => NavigateHistoryPrevious(),
        Keys.Control | Keys.PageDown => NavigateHistoryNext(),
        _ => false
    };
    public void Dispose()
    {
        if (IsDisposed)
            return;
        persistenceSaveTimer.Stop();
        SyncAndPersistHistory();
        persistenceSaveTimer.Dispose();
        foreach (var document in presenters)
            document.Dispose();
        foreach (var item in historyStore.Items)
            item.Dispose();
        IsDisposed = true;
    }

    private readonly List<IClipboardDocumentPresenter> presenters = new();
    private readonly EditorHostServices hostServices;
    private readonly ClipboardHistoryStore historyStore;
    private readonly ClipboardHistoryPersistence historyPersistence;
    private readonly bool persistHistoryChanges;
    private readonly bool allowSystemClipboardWrites;
    private readonly System.Windows.Forms.Timer persistenceSaveTimer;
    // Set when the editor reports an edit, cleared by a capture. Purely an optimisation:
    // saves are also scheduled by things that have nothing to do with the open editor (a
    // new clipboard item, say), and re-encoding the active image for those is wasted work.
    private bool activeItemHasUncapturedEdits;
    private IClipboardDocumentPresenter? activePresenter;
    private bool hasPendingReloadIndicator;
    private bool hasPendingPersistenceSave;
    private DateTime? suppressExternalClipboardUntilUtc;
    private Guid? pendingCommittedItemId;
    private DateTime? pendingCommittedItemUntilUtc;
    internal Func<string?, Task<bool>>? TryDeleteFromSystemHistoryAsync { get; set; }
    internal Func<Task>? RefreshSystemHistoryAsync { get; set; }
    internal Func<Image, bool>? ClipboardImageWriterForDiagnostics { get; set; }
    internal Action? ExternalActivateRequested { get; set; }
    internal ClipboardHistoryStore HistoryStore => historyStore;

    /// <summary>Run the debounced save now, as the timer would.</summary>
    internal void TriggerPersistedHistorySaveForTests() => FlushPersistedHistorySave();
    internal bool HasUncapturedEditsForTests => activeItemHasUncapturedEdits;
    // App-level actions the menu bar surfaces but the tray host (Screenzap) owns. Wired after
    // construction; menu handlers read them lazily so late wiring is fine. Null hooks disable
    // the corresponding menu items.
    internal Func<bool>? GetStartOnLogin { get; set; }
    internal Action<bool>? SetStartOnLogin { get; set; }
    internal Func<bool>? GetStartupNotificationEnabled { get; set; }
    internal Action<bool>? SetStartupNotificationEnabled { get; set; }
    internal Func<Keys>? GetCaptureShortcut { get; set; }
    internal Func<Keys, bool>? TrySetCaptureShortcut { get; set; }



    internal Action? SaveClipboardImageRequested { get; set; }

    private const int InternalClipboardWriteSuppressMs = 2000;
    private const int PendingCommittedItemMatchMs = 10000;
    internal void BeginInternalClipboardWrite()
    {
        suppressExternalClipboardUntilUtc = DateTime.UtcNow.AddMilliseconds(InternalClipboardWriteSuppressMs);
    }

    /// <summary>True when a recent internal clipboard write should suppress inbound history observation.</summary>
    internal bool IsInternalClipboardWriteWindow()
    {
        return suppressExternalClipboardUntilUtc.HasValue && DateTime.UtcNow < suppressExternalClipboardUntilUtc.Value;
    }

    public IClipboardDocumentPresenter? ActivePresenter => activePresenter;
    internal bool HasPendingReloadIndicator => hasPendingReloadIndicator;

    internal bool ExecuteHostCommand(EditorCommandId commandId)
    {
        var result = ExecuteCommand(commandId);
        UpdateCommandStates();
        return result;
    }

    internal bool CanExecuteHostCommand(EditorCommandId commandId)
    {
        return ComputeCommandEnabled(commandId, historyStore.ActiveItem);
    }

    private async Task RefreshHistoryFromSystemAsync()
    {
        if (RefreshSystemHistoryAsync == null)
        {
            UpdateStatusText("Windows clipboard history sync is unavailable.");
            return;
        }

        try
        {
            await RefreshSystemHistoryAsync();
            UpdateStatusText("Clipboard history refreshed.");
        }
        catch (Exception ex)
        {
            Logger.Log($"Manual system history refresh failed: {ex.Message}");
            UpdateStatusText("Failed to refresh Windows clipboard history.");
        }
    }

    private void ActivateNewestHistoryItem()
    {
        var newest = historyStore.TopItem;
        if (newest == null)
        {
            return;
        }

        ActivateHistoryItem(newest);
        UpdateStatusText("Activated newest history item.");
    }

    internal bool ExecuteCommandForDiagnostics(EditorCommandId commandId) => ExecuteCommand(commandId);
    private bool ExecuteCommand(EditorCommandId commandId)
    {
        switch (commandId)
        {
            case EditorCommandId.CommitEdits:
                return CommitActiveItemEdits();
            case EditorCommandId.Duplicate:
                return DuplicateActiveItem();
            case EditorCommandId.Revert:
                return RevertActiveItem();
            case EditorCommandId.Delete:
                var active = historyStore.ActiveItem;
                if (active == null)
                    return false;
                _ = DeleteItemAsync(active);
                return true;
            default:
                return activePresenter?.TryExecute(commandId) == true;
        }
    }

    private bool ComputeCommandEnabled(EditorCommandId commandId, ClipboardHistoryItem? activeItem)
    {
        return commandId switch
        {
            EditorCommandId.CommitEdits => activeItem?.IsDirty == true,
            EditorCommandId.Revert => activeItem?.CanRevertToOriginal == true,
            EditorCommandId.Duplicate => activeItem != null,
            EditorCommandId.Delete => activeItem != null,
            _ => activePresenter?.CanExecute(commandId) == true,
        };
    }

    private bool CommitActiveItemEdits()
    {
        var item = historyStore.ActiveItem;
        if (item == null || !item.IsDirty)
            return false;
        using var composite = activePresenter?.GetCurrentContent() as Bitmap;
        if (composite == null)
            return false;
        BeginInternalClipboardWrite();
        try
        {
            if (ClipboardImageWriterForDiagnostics != null)
            {
                if (!ClipboardImageWriterForDiagnostics(composite))
                    throw new InvalidOperationException("Clipboard writer rejected the image.");
            }
            else if (allowSystemClipboardWrites)
            {
                screenzap.lib.ClipboardImageWriter.WriteImage(composite);
            }
        }
        catch (Exception ex)
        {
            screenzap.lib.Logger.Log($"CommitActiveItemEdits clipboard write failed: {ex.Message}");
            suppressExternalClipboardUntilUtc = null;
            UpdateStatusText("Could not write to the clipboard. Try Commit again.");
            return false;
        }

        // Only a successful export seals pending text/gestures and advances the checkpoint.
        // No document reload or synthetic undo step: existing objects and redo stay live.
        if (activePresenter is screenzap.ImageDocumentEditor imageEditor)
            imageEditor.RecordSuccessfulClipboardExport();
        activePresenter?.CaptureLiveStateInto(item);
        item.RecordClipboardExport(composite);
        activeItemHasUncapturedEdits = false;
        if (!string.IsNullOrEmpty(item.SystemHistoryId))
        {
            item.AddSuppressedSystemHistoryId(item.SystemHistoryId);
            item.SystemHistoryId = null;
        }

        TrackPendingCommittedItem(item.Id);
        historyStore.NotifyItemUpdated(item);
        UpdateCommandStates();
        UpdateStatusText("Edits committed to clipboard.");
        return true;
    }

    private bool DuplicateActiveItem()
    {
        var item = historyStore.ActiveItem;
        if (item == null)
            return false;
        // Capture latest presenter content into the source before cloning.
        activePresenter?.StashHistoryItemState(item);
        var clone = historyStore.Duplicate(item);
        ActivateHistoryItem(clone);
        UpdateStatusText("Duplicated to new history entry.");
        return true;
    }

    private bool RevertActiveItem()
    {
        var item = historyStore.ActiveItem;
        if (item == null)
            return false;
        return RevertItemCore(item);
    }

    /// <summary>
    /// Called by <see cref = "EditorHostServices.NotifyContentEdited"/> when the active presenter dirties its content.
    /// We update the item's preview composite (for thumbnails) and flag it dirty without flattening the base image.
    /// </summary>
    private void OnActivePresenterContentEdited()
    {
        var item = historyStore.ActiveItem;
        if (item == null || activePresenter == null)
            return;
        // Flag and notify only. The item's content is copied out of the presenter by the
        // capture on the debounced save — building a full-size composite here meant two
        // whole-image allocations on every single edit, more often than the save that
        // consumed them.
        activeItemHasUncapturedEdits = true;
        item.SetDirtyFlagForRestore(activePresenter is screenzap.ImageDocumentEditor imageEditor ? imageEditor.DocumentIsDirty : true);
        historyStore.NotifyItemUpdated(item);
        UpdateCommandStates();
    }

    private void OnStoreItemUpdated(object? sender, ClipboardHistoryItem e)
    {
        UpdateCommandStates();
        SchedulePersistedHistorySave();
    }

    private void OnStoreChanged(object? sender, EventArgs e)
    {
        SchedulePersistedHistorySave();
    }

    private void OnActiveItemChanged(object? sender, EventArgs e)
    {
        // Switching items stashes the one being left, so whatever was outstanding has
        // already landed — and the incoming item has not been edited yet.
        activeItemHasUncapturedEdits = false;
        SchedulePersistedHistorySave();
    }

    private void SetItemAsClipboard(ClipboardHistoryItem item)
    {
        // Activate in the editor first, then write to the system clipboard.
        ActivateHistoryItem(item);
        // The pending write will spawn a new system-history entry. Track this item
        // so the incoming snapshot re-binds to it instead of inserting a duplicate
        // row, and suppress the old SystemHistoryId so it doesn't linger as a
        // separate entry beneath the new top.
        if (!string.IsNullOrEmpty(item.SystemHistoryId))
        {
            item.AddSuppressedSystemHistoryId(item.SystemHistoryId);
            item.SystemHistoryId = null;
        }

        TrackPendingCommittedItem(item.Id);
        BeginInternalClipboardWrite();
        try
        {
            // Prefer composited preview so annotation/text-overlay edits are preserved in Set as Active.
            var imageToWrite = item.PreviewComposite ?? item.CurrentImage;
            if (imageToWrite != null)
            {
                if (ClipboardImageWriterForDiagnostics != null)
                {
                    using var copy = new Bitmap(imageToWrite);
                    ClipboardImageWriterForDiagnostics(copy);
                }
                else if (allowSystemClipboardWrites)
                {
                    screenzap.lib.ClipboardImageWriter.WriteImage(imageToWrite);
                }
            }
        }
        catch (Exception ex)
        {
            screenzap.lib.Logger.Log($"SetItemAsClipboard failed: {ex.Message}");
        }

        UpdateStatusText("Set as active clipboard content.");
    }

    private void DuplicateItem(ClipboardHistoryItem item)
    {
        if (ReferenceEquals(historyStore.ActiveItem, item))
        {
            activePresenter?.StashHistoryItemState(item);
        }

        var clone = historyStore.DuplicateAbove(item);
        ActivateHistoryItem(clone);
        UpdateStatusText("Duplicated.");
    }

    private void RevertItem(ClipboardHistoryItem item)
    {
        RevertItemCore(item);
    }

    private bool RevertItemCore(ClipboardHistoryItem item)
    {
        if (!item.CanRevertToOriginal)
        {
            return false;
        }

        bool isActive = ReferenceEquals(historyStore.ActiveItem, item);
        // Stash live editor state first so the revert undo step captures the latest edits
        // (a non-active item was already stashed when it was deactivated).
        if (isActive)
        {
            activePresenter?.StashHistoryItemState(item);
        }

        historyStore.Revert(item);
        if (isActive)
        {
            activePresenter?.LoadHistoryItem(item);
        }

        UpdateCommandStates();
        UpdateStatusText("Reverted to original — Ctrl+Z to undo.");
        return true;
    }

    private Task DeleteItemAsync(ClipboardHistoryItem item)
    {
        var systemHistoryId = item.SystemHistoryId;
        historyStore.SuppressSystemHistoryId(systemHistoryId);
        bool wasActive = ReferenceEquals(historyStore.ActiveItem, item);
        var items = historyStore.Items;
        int idx = -1;
        for (int i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], item))
            {
                idx = i;
                break;
            }
        }

        historyStore.Remove(item);
        if (wasActive)
        {
            var remaining = historyStore.Items;
            var next = remaining.Count > 0 ? remaining[Math.Min(idx, remaining.Count - 1)] : null;
            if (next != null)
                ActivateHistoryItem(next);
            else
                UpdateCommandStates();
        }

        UpdateStatusText("Deleted from history.");
        if (!string.IsNullOrEmpty(systemHistoryId))
        {
            _ = DeleteSystemHistoryItemInBackgroundAsync(systemHistoryId);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Activates the given history item: stashes outgoing state, loads content into the matching presenter.
    /// </summary>
    internal bool ActivateHistoryItem(ClipboardHistoryItem item)
    {
        if (item == null)
            return false;
        // Stash current active item state first.
        var outgoing = historyStore.ActiveItem;
        if (outgoing != null && outgoing.Id != item.Id)
        {
            activePresenter?.StashHistoryItemState(outgoing);
        }

        // Find a presenter that can handle this kind.
        var presenter = presenters.FirstOrDefault(p => p.CanPresent(item));
        if (presenter == null)
            return false;
        if (ReferenceEquals(outgoing, item) && activePresenter == presenter)
        {
            ActivatePresenter(presenter);
            UpdateCommandStates();
            return true;
        }

        historyStore.Activate(item);
        ActivatePresenter(presenter);
        presenter.LoadHistoryItem(item);
        // Loading resets the zoom, so fit the new content to the window we already have.
        // Opening the editor re-runs this after resizing; switching items in an open window
        // is the case that would otherwise drop a 4K capture in at 1:1.
        presenter.FitContentToView();
        UpdateCommandStates();
        return true;
    }

    internal bool ActivatePreferredHistoryItem()
    {
        var preferred = GetPreferredHistoryItem();
        return preferred != null && ActivateHistoryItem(preferred);
    }

    private ClipboardHistoryItem? GetPreferredHistoryItem()
    {
        var activeItem = historyStore.ActiveItem;
        return activeItem?.IsDirty == true ? activeItem : historyStore.TopItem;
    }

    /// <summary>
    /// Applies the auto-switch rule for a newly-observed clipboard item.
    /// Rule: if the current active item is dirty, keep editing it and leave the new item in history.
    /// Otherwise, activate the new item.
    /// </summary>
    internal void OnObservedClipboardItem(ClipboardHistoryItem newItem)
    {
        if (newItem == null)
            return;
        var activeItem = historyStore.ActiveItem;
        bool shouldPreserve = activeItem?.IsDirty == true;
        if (shouldPreserve)
        {
            // Keep the dirty item active; the new one is still in the list.
            UpdateCommandStates();
            return;
        }

        ActivateHistoryItem(newItem);
    }

    internal ClipboardHistoryItem? TryBindPendingCommittedSystemItem(ClipboardHistoryItem incomingSystemItem)
    {
        if (pendingCommittedItemId == null || pendingCommittedItemUntilUtc == null)
        {
            return null;
        }

        if (DateTime.UtcNow > pendingCommittedItemUntilUtc.Value)
        {
            pendingCommittedItemId = null;
            pendingCommittedItemUntilUtc = null;
            return null;
        }

        var localItem = historyStore.FindById(pendingCommittedItemId.Value);
        if (localItem == null || !localItem.ContentMatches(incomingSystemItem))
        {
            return null;
        }

        localItem.AssignSystemHistoryId(incomingSystemItem.SystemHistoryId);
        historyStore.NotifyItemUpdated(localItem);
        pendingCommittedItemId = null;
        pendingCommittedItemUntilUtc = null;
        return localItem;
    }

    private void TrackPendingCommittedItem(Guid itemId)
    {
        pendingCommittedItemId = itemId;
        pendingCommittedItemUntilUtc = DateTime.UtcNow.AddMilliseconds(PendingCommittedItemMatchMs);
    }

    private void RestorePersistedHistory()
    {
        var restored = historyPersistence.Load();
        if (restored.Items.Count == 0)
        {
            return;
        }

        historyStore.LoadPersisted(restored.Items, restored.ActiveItemId);
    }

    private void SyncAndPersistHistory()
    {
        // Always: the item is the editor's counterpart and has to keep up with it whether
        // or not anything is written to disk.
        CaptureActiveItemLiveState();
        if (!persistHistoryChanges)
        {
            return;
        }

        historyPersistence.Save(historyStore.Items, historyStore.ActiveItem);
    }

    /// <summary>
    /// Copy the open editor's document into its item, immediately before it is written out.
    /// The item is otherwise only updated when something remembers to stash it — on
    /// deactivate, commit, duplicate or revert — which left whatever was on screen out of
    /// every save that happened in between, and out of the last one entirely if the app was
    /// closed mid-edit. Capturing here means the item is never more than one debounce behind
    /// the editor and nobody has to remember anything.
    /// </summary>
    private void CaptureActiveItemLiveState()
    {
        if (!activeItemHasUncapturedEdits)
        {
            return;
        }

        var activeItem = historyStore.ActiveItem;
        if (activeItem == null || activePresenter == null)
        {
            activeItemHasUncapturedEdits = false;
            return;
        }

        activePresenter.CaptureLiveStateInto(activeItem);
        activeItemHasUncapturedEdits = false;
        // The capture rebuilt the thumbnail source. This signal repaints it without
        // scheduling the save we are already inside.
        historyStore.NotifyItemPreviewRefreshed(activeItem);
    }

    /// <summary>
    /// Debounce a sync of the open editor into its item, plus a save if persistence is on.
    /// Deliberately not gated on persistence: the capture half has to happen either way.
    /// </summary>
    private void SchedulePersistedHistorySave()
    {
        hasPendingPersistenceSave = true;
        persistenceSaveTimer.Stop();
        persistenceSaveTimer.Start();
    }

    private void OnPersistenceSaveTimerTick(object? sender, EventArgs e)
    {
        persistenceSaveTimer.Stop();
        FlushPersistedHistorySave();
    }

    private void FlushPersistedHistorySave()
    {
        if (!hasPendingPersistenceSave)
        {
            return;
        }

        hasPendingPersistenceSave = false;
        SyncAndPersistHistory();
    }

    private async Task DeleteSystemHistoryItemInBackgroundAsync(string systemHistoryId)
    {
        if (TryDeleteFromSystemHistoryAsync == null)
        {
            return;
        }

        bool removedFromSystem = false;
        try
        {
            removedFromSystem = await TryDeleteFromSystemHistoryAsync(systemHistoryId);
        }
        catch (Exception ex)
        {
            Logger.Log($"Delete system history item failed: {ex.Message}");
        }

        if (!removedFromSystem && !IsDisposed)
        {
            UpdateStatusText("Could not delete this Windows clipboard history item.");
        }
    }

    private bool NavigateHistoryPrevious()
    {
        var activeItem = historyStore.ActiveItem;
        var items = historyStore.Items;
        if (items.Count == 0)
            return false;
        // Find the current index
        int currentIndex = -1;
        for (int i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], activeItem))
            {
                currentIndex = i;
                break;
            }
        }

        // If no active item or at the beginning, go to the last item
        int nextIndex = currentIndex <= 0 ? items.Count - 1 : currentIndex - 1;
        return ActivateHistoryItem(items[nextIndex]);
    }

    private bool NavigateHistoryNext()
    {
        var activeItem = historyStore.ActiveItem;
        var items = historyStore.Items;
        if (items.Count == 0)
            return false;
        // Find the current index
        int currentIndex = -1;
        for (int i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], activeItem))
            {
                currentIndex = i;
                break;
            }
        }

        // If no active item or at the end, go to the first item
        int nextIndex = currentIndex < 0 || currentIndex >= items.Count - 1 ? 0 : currentIndex + 1;
        return ActivateHistoryItem(items[nextIndex]);
    }
}
