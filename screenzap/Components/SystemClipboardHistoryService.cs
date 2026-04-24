using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using screenzap.lib;
using WinRtClipboard = Windows.ApplicationModel.DataTransfer.Clipboard;
using WinRtClipboardHistoryItem = Windows.ApplicationModel.DataTransfer.ClipboardHistoryItem;
using WinRtClipboardHistoryItemsResult = Windows.ApplicationModel.DataTransfer.ClipboardHistoryItemsResult;

namespace screenzap.Components
{
    /// <summary>
    /// Bridges the Windows 11 system clipboard history (WinRT) into the Screenzap-owned
    /// <see cref="ClipboardHistoryStore"/>. Preserves Screenzap's dirty state / undo stacks across
    /// refreshes by keying items on <see cref="ClipboardHistoryItem.SystemHistoryId"/>.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows10.0.17763.0")]
    internal sealed class SystemClipboardHistoryService : IDisposable
    {
        private readonly ClipboardHistoryStore store;
        private readonly SynchronizationContextPoster poster;
        private readonly Action<ClipboardHistoryItem>? onItemObserved;
        private readonly Func<bool>? isInternalWriteWindow;
        private bool disposed;
        private bool subscribed;

        public SystemClipboardHistoryService(
            ClipboardHistoryStore store,
            Control uiDispatcher,
            Action<ClipboardHistoryItem>? onItemObserved,
            Func<bool>? isInternalWriteWindow)
        {
            this.store = store;
            this.poster = new SynchronizationContextPoster(uiDispatcher);
            this.onItemObserved = onItemObserved;
            this.isInternalWriteWindow = isInternalWriteWindow;
        }

        public bool IsAvailable
        {
            get
            {
                try { return WinRtClipboard.IsHistoryEnabled(); }
                catch (Exception ex)
                {
                    Logger.Log($"WinRT Clipboard.IsHistoryEnabled threw: {ex.Message}");
                    return false;
                }
            }
        }

        public void Start()
        {
            if (subscribed || disposed) return;
            try
            {
                WinRtClipboard.HistoryChanged += OnHistoryChanged;
                subscribed = true;
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to subscribe to WinRT Clipboard.HistoryChanged: {ex.Message}");
                return;
            }

            _ = RefreshAsync();
        }

        public async Task RefreshAsync()
        {
            if (disposed) return;
            WinRtClipboardHistoryItemsResult? result = null;
            try
            {
                result = await WinRtClipboard.GetHistoryItemsAsync();
            }
            catch (Exception ex)
            {
                Logger.Log($"WinRT GetHistoryItemsAsync failed: {ex.Message}");
                return;
            }

            if (result == null || result.Status.ToString() != "Success")
            {
                return;
            }

            // Translate WinRT items to Screenzap items sequentially so order is preserved.
            var translated = new List<(string id, ClipboardHistoryItem built)?>();
            foreach (var sys in result.Items)
            {
                var converted = await TryConvertAsync(sys);
                translated.Add(converted);
            }

            poster.Post(() => ApplySnapshot(translated));
        }

        private async Task<(string id, ClipboardHistoryItem built)?> TryConvertAsync(WinRtClipboardHistoryItem sys)
        {
            try
            {
                var dp = sys.Content;
                if (dp == null) return null;

                // Prefer bitmap; fall back to text.
                if (dp.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Bitmap))
                {
                    var bmpRef = await dp.GetBitmapAsync();
                    using var stream = await bmpRef.OpenReadAsync();
                    using var ms = new MemoryStream();
                    using (var dotNetStream = stream.AsStreamForRead())
                    {
                        await dotNetStream.CopyToAsync(ms);
                    }
                    ms.Position = 0;
                    using var bitmap = new Bitmap(ms);
                    var item = ClipboardHistoryItem.FromImage(bitmap);
                    item.SystemHistoryId = sys.Id;
                    return (sys.Id, item);
                }

                if (dp.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
                {
                    var text = await dp.GetTextAsync();
                    var item = ClipboardHistoryItem.FromText(text ?? string.Empty);
                    item.SystemHistoryId = sys.Id;
                    return (sys.Id, item);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to convert WinRT history item {sys?.Id}: {ex.Message}");
            }

            return null;
        }

        private void ApplySnapshot(List<(string id, ClipboardHistoryItem built)?> translated)
        {
            if (disposed) return;

            // Build a map of existing items keyed by SystemHistoryId so we can keep their dirty state.
            var existingById = new Dictionary<string, ClipboardHistoryItem>();
            foreach (var item in store.Items)
            {
                if (!string.IsNullOrEmpty(item.SystemHistoryId))
                {
                    existingById[item.SystemHistoryId!] = item;
                }
            }

            bool suppressActivation = isInternalWriteWindow?.Invoke() == true;

            var newTopId = translated.FirstOrDefault(t => t.HasValue)?.id;

            // Merge: if translated item exists in our store, keep the existing one; otherwise insert new.
            // We rebuild the full ordered list inside the store.
            var finalOrder = new List<ClipboardHistoryItem>();
            var handled = new HashSet<Guid>();

            foreach (var maybe in translated)
            {
                if (!maybe.HasValue) continue;
                var (sysId, built) = maybe.Value;

                if (existingById.TryGetValue(sysId, out var existing))
                {
                    finalOrder.Add(existing);
                    handled.Add(existing.Id);
                    built.Dispose();
                }
                else
                {
                    finalOrder.Add(built);
                }
            }

            // Preserve dirty items that have no system id (user-created duplicates etc.), appending at top priority.
            foreach (var item in store.Items)
            {
                if (handled.Contains(item.Id)) continue;
                if (string.IsNullOrEmpty(item.SystemHistoryId))
                {
                    // Startup fallback seed can race with WinRT sync; remove it once an equivalent
                    // system-backed entry is available to avoid duplicate rows for one clipboard item.
                    if (item.IsSeededFallback && !item.IsDirty && ContainsEquivalentSystemItem(item, finalOrder))
                    {
                        item.Dispose();
                        continue;
                    }

                    finalOrder.Insert(0, item);
                    handled.Add(item.Id);
                }
                else if (item.IsDirty)
                {
                    finalOrder.Insert(0, item);
                    handled.Add(item.Id);
                }
                else
                {
                    // System item no longer in history and not dirty — drop it.
                    item.Dispose();
                }
            }

            store.ReplaceAll(finalOrder);

            // Notify host of the newly observed top item so it can decide whether to auto-switch.
            if (!suppressActivation && newTopId != null)
            {
                var top = finalOrder.FirstOrDefault(i => i.SystemHistoryId == newTopId);
                if (top != null)
                {
                    onItemObserved?.Invoke(top);
                }
            }
        }

        private void OnHistoryChanged(object? sender, object e)
        {
            _ = RefreshAsync();
        }

        private static bool ContainsEquivalentSystemItem(ClipboardHistoryItem candidate, List<ClipboardHistoryItem> items)
        {
            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(item.SystemHistoryId)) continue;
                if (item.Kind != candidate.Kind) continue;

                if (candidate.Kind == ClipboardItemKind.Text)
                {
                    if (string.Equals(item.CurrentText ?? string.Empty, candidate.CurrentText ?? string.Empty, StringComparison.Ordinal))
                    {
                        return true;
                    }
                    continue;
                }

                if (AreImagesEquivalent(item.CurrentImage, candidate.CurrentImage))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AreImagesEquivalent(Bitmap? a, Bitmap? b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Size != b.Size) return false;
            return ImageSignature(a).SequenceEqual(ImageSignature(b));
        }

        private static byte[] ImageSignature(Bitmap bmp)
        {
            using var small = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(small))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(bmp, new Rectangle(0, 0, 16, 16));
            }

            var rect = new Rectangle(0, 0, 16, 16);
            var data = small.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var buffer = new byte[16 * 16 * 4];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
                return buffer;
            }
            finally
            {
                small.UnlockBits(data);
            }
        }

        /// <summary>
        /// Attempts to remove a system clipboard history item by WinRT id.
        /// Returns true when Windows reports successful deletion.
        /// </summary>
        public async Task<bool> TryDeleteSystemItemAsync(string? systemHistoryId)
        {
            if (string.IsNullOrWhiteSpace(systemHistoryId))
            {
                return false;
            }

            try
            {
                if (!IsAvailable)
                {
                    return false;
                }

                var result = await WinRtClipboard.GetHistoryItemsAsync();
                if (result == null || result.Status.ToString() != "Success")
                {
                    return false;
                }

                var sysItem = result.Items.FirstOrDefault(i => string.Equals(i.Id, systemHistoryId, StringComparison.Ordinal));
                if (sysItem == null)
                {
                    return false;
                }

                var removed = WinRtClipboard.DeleteItemFromHistory(sysItem);
                if (removed)
                {
                    _ = RefreshAsync();
                }
                return removed;
            }
            catch (Exception ex)
            {
                Logger.Log($"WinRT DeleteItemFromHistory failed for {systemHistoryId}: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (subscribed)
            {
                try { WinRtClipboard.HistoryChanged -= OnHistoryChanged; } catch { /* ignore */ }
                subscribed = false;
            }
        }

        /// <summary>Marshals actions onto the UI thread of a given Control.</summary>
        private sealed class SynchronizationContextPoster
        {
            private readonly Control target;
            public SynchronizationContextPoster(Control target) { this.target = target; }
            public void Post(Action action)
            {
                if (target.IsDisposed) return;
                if (target.InvokeRequired)
                {
                    try { target.BeginInvoke(action); } catch { /* form closing */ }
                }
                else
                {
                    action();
                }
            }
        }
    }
}
