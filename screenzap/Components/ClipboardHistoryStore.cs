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
        public const int MaxItems = 25;

        private readonly List<ClipboardHistoryItem> items = new();
        private ClipboardHistoryItem? activeItem;

        public IReadOnlyList<ClipboardHistoryItem> Items => new ReadOnlyCollection<ClipboardHistoryItem>(items);
        public ClipboardHistoryItem? ActiveItem => activeItem;

        /// <summary>Fired whenever the ordered list of items changes (add/remove/activate/update).</summary>
        public event EventHandler? Changed;

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

        public ClipboardHistoryItem AddObservedText(string source)
        {
            var item = ClipboardHistoryItem.FromText(source);
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
            activeItem = item;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Notify the store that the active item's current image changed (editor edit).</summary>
        public void NotifyImageEdited(ClipboardHistoryItem item, Bitmap updated)
        {
            item.UpdateCurrentImage(updated);
            ItemUpdated?.Invoke(this, item);
        }

        public void NotifyTextEdited(ClipboardHistoryItem item, string updated)
        {
            item.UpdateCurrentText(updated);
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

        public ClipboardHistoryItem? TopItem => items.FirstOrDefault();
    }
}
