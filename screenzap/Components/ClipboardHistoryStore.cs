using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;

namespace screenzap.Components
{
    /// <summary>
    /// In-memory list of recent clipboard items with Screenzap-maintained edit state.
    /// Capped at <see cref="MaxItems"/>. Emits events when the list changes or an item mutates.
    /// </summary>
    internal sealed class ClipboardHistoryStore
    {
        public const int MaxItems = 128;

        private readonly List<ClipboardHistoryItem> items = new();
        private readonly HashSet<string> suppressedSystemHistoryIds = new(StringComparer.Ordinal);
        private ClipboardHistoryItem? activeItem;

        public IReadOnlyList<ClipboardHistoryItem> Items => new ReadOnlyCollection<ClipboardHistoryItem>(items);
        public ClipboardHistoryItem? ActiveItem => activeItem;

        /// <summary>Fired whenever the ordered list of items changes (add/remove/activate/update).</summary>
        public event EventHandler? Changed;

        /// <summary>Fired when only the active item changed.</summary>
        public event EventHandler? ActiveItemChanged;

        /// <summary>Fired when a specific item's content/dirty flag changed and thumbnails should be refreshed.</summary>
        public event EventHandler<ClipboardHistoryItem>? ItemUpdated;

        /// <summary>
        /// Adds a new observed image item to the top of the list.
        /// Returns the new item. Auto-activates per the supplied rule.
        /// </summary>
        public ClipboardHistoryItem AddObservedImage(Bitmap source)
        {
            var item = ClipboardHistoryItem.FromImage(source);
            InsertAtTop(item);
            return item;
        }

        private void InsertAtTop(ClipboardHistoryItem item)
        {
            items.Insert(0, item);
            TrimToMax();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void TrimToMax()
        {
            while (items.Count > MaxItems)
            {
                var victim = items[items.Count - 1];
                items.RemoveAt(items.Count - 1);
                if (ReferenceEquals(activeItem, victim))
                {
                    activeItem = null;
                }
                victim.Dispose();
            }
        }

        public void Activate(ClipboardHistoryItem? item)
        {
            if (ReferenceEquals(activeItem, item))
            {
                return;
            }

            // The outgoing item is no longer on screen at full size, so free its decoded full bitmaps;
            // it keeps its compressed blobs + thumbnail and will re-decode lazily if reactivated. This
            // is what keeps resident full bitmaps bounded to ~the active item.
            activeItem?.ReleaseDecodedImages();

            activeItem = item;
            ActiveItemChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Notify the store that the active item's current image changed (editor edit).</summary>
        public void NotifyImageEdited(ClipboardHistoryItem item, Bitmap updated)
        {
            item.UpdateCurrentImage(updated);
            ItemUpdated?.Invoke(this, item);
        }

        /// <summary>Fire ItemUpdated for external changes that don't go through Notify*Edited.</summary>
        public void NotifyItemUpdated(ClipboardHistoryItem item)
        {
            ItemUpdated?.Invoke(this, item);
        }

        public void MarkClean(ClipboardHistoryItem item)
        {
            item.MarkClean();
            ItemUpdated?.Invoke(this, item);
        }

        public void Revert(ClipboardHistoryItem item)
        {
            item.RevertToOriginal();
            ItemUpdated?.Invoke(this, item);
        }

        public ClipboardHistoryItem Duplicate(ClipboardHistoryItem source)
        {
            var clone = source.CloneCurrentAsNew();
            InsertAtTop(clone);
            return clone;
        }

        /// <summary>Insert a clone of <paramref name="source"/> directly above it in the list.</summary>
        public ClipboardHistoryItem DuplicateAbove(ClipboardHistoryItem source)
        {
            var clone = source.CloneCurrentAsNew();
            int idx = items.IndexOf(source);
            items.Insert(idx >= 0 ? idx : 0, clone);
            TrimToMax();
            Changed?.Invoke(this, EventArgs.Empty);
            return clone;
        }

        public void Remove(ClipboardHistoryItem item)
        {
            if (!items.Remove(item)) return;
            if (ReferenceEquals(activeItem, item)) activeItem = null;
            item.Dispose();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public ClipboardHistoryItem? TopItem => items.FirstOrDefault();

        internal ClipboardHistoryItem? FindById(Guid id)
        {
            return items.FirstOrDefault(item => item.Id == id);
        }

        internal bool ContainsSuppressedSystemHistoryId(string? systemHistoryId)
        {
            if (string.IsNullOrWhiteSpace(systemHistoryId))
            {
                return false;
            }

            return suppressedSystemHistoryIds.Contains(systemHistoryId)
                || items.Any(item => item.ContainsSuppressedSystemHistoryId(systemHistoryId));
        }

        internal void SuppressSystemHistoryId(string? systemHistoryId)
        {
            if (!string.IsNullOrWhiteSpace(systemHistoryId))
            {
                suppressedSystemHistoryIds.Add(systemHistoryId);
            }
        }

        internal void LoadPersisted(IEnumerable<ClipboardHistoryItem> restoredItems, Guid? activeItemId)
        {
            var incoming = restoredItems?.ToList() ?? new List<ClipboardHistoryItem>();
            ReplaceAll(incoming);

            if (!activeItemId.HasValue)
            {
                return;
            }

            var restoredActive = items.FirstOrDefault(entry => entry.Id == activeItemId.Value);
            if (restoredActive != null)
            {
                activeItem = restoredActive;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Replace the entire ordered list of items (used by the system-history sync path). Disposes items that aren't in the new list.</summary>
        public void ReplaceAll(IEnumerable<ClipboardHistoryItem> newOrder)
        {
            var incoming = newOrder.ToList();

            // The WinRT history sync calls this on every Windows HistoryChanged event and usually
            // reuses the exact same item instances in the exact same order (nothing actually moved).
            // Short-circuit that no-op so it doesn't fan out into a full panel rebuild and a full
            // persistence re-save. Reference identity (not Id) is required: identical Ids with
            // different instances would mean we'd skip disposing the replaced ones.
            if (IsReferenceIdenticalOrder(incoming))
            {
                return;
            }

            var incomingIds = new HashSet<Guid>(incoming.Select(i => i.Id));

            // Dispose items that disappear (but weren't transferred to the new list).
            foreach (var existing in items)
            {
                if (!incomingIds.Contains(existing.Id))
                {
                    existing.Dispose();
                }
            }

            items.Clear();
            items.AddRange(incoming);
            while (items.Count > MaxItems)
            {
                var victim = items[items.Count - 1];
                items.RemoveAt(items.Count - 1);
                if (ReferenceEquals(activeItem, victim)) activeItem = null;
                victim.Dispose();
            }

            if (activeItem != null && !items.Contains(activeItem))
            {
                activeItem = null;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }

        private bool IsReferenceIdenticalOrder(List<ClipboardHistoryItem> incoming)
        {
            if (incoming.Count != items.Count)
            {
                return false;
            }

            for (int i = 0; i < incoming.Count; i++)
            {
                if (!ReferenceEquals(incoming[i], items[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
