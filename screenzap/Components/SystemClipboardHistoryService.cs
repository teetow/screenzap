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
        private readonly Func<ClipboardHistoryItem, ClipboardHistoryItem?>? tryBindPendingCommittedItem;
        private readonly Func<bool>? isInternalWriteWindow;
        private readonly Func<bool>? includeNonBitmapItems;
        private bool disposed;
        private bool subscribed;

        public SystemClipboardHistoryService(
            ClipboardHistoryStore store,
            Control uiDispatcher,
            Action<ClipboardHistoryItem>? onItemObserved,
            Func<ClipboardHistoryItem, ClipboardHistoryItem?>? tryBindPendingCommittedItem,
            Func<bool>? isInternalWriteWindow,
            Func<bool>? includeNonBitmapItems)
        {
            this.store = store;
            this.poster = new SynchronizationContextPoster(uiDispatcher);
            this.onItemObserved = onItemObserved;
            this.tryBindPendingCommittedItem = tryBindPendingCommittedItem;
            this.isInternalWriteWindow = isInternalWriteWindow;
            this.includeNonBitmapItems = includeNonBitmapItems;
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
                var status = result == null ? "null" : result.Status.ToString();
                Logger.Log($"WinRT GetHistoryItemsAsync returned non-success status: {status}");
                return;
            }

            // Translate WinRT items and then order by WinRT timestamp (newest first).
            var translated = new List<(string id, DateTimeOffset timestamp, ClipboardHistoryItem built)?>();
            foreach (var sys in result.Items)
            {
                var converted = await TryConvertAsync(sys);
                if (converted.HasValue)
                {
                    translated.Add((converted.Value.id, sys.Timestamp, converted.Value.built));
                }
                else
                {
                    translated.Add(null);
                }
            }

            translated = translated
                .OrderByDescending(entry => entry?.timestamp ?? DateTimeOffset.MinValue)
                .ToList();

            poster.Post(() => ApplySnapshot(translated));
        }

        private async Task<(string id, ClipboardHistoryItem built)?> TryConvertAsync(WinRtClipboardHistoryItem sys)
        {
            try
            {
                var dp = sys.Content;
                if (dp == null) return null;

                bool declaresBitmap = dp.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Bitmap);
                var availableFormats = dp.AvailableFormats?.ToList() ?? new List<string>();
                bool imageLikeFormats = LooksImageLike(availableFormats);

                // Prefer image conversion. Some providers do not reliably advertise bitmap support,
                // so probe likely image formats too.
                if (declaresBitmap || imageLikeFormats)
                {
                    var imageItem = await TryBuildImageItemAsync(dp, sys.Id, logFailures: declaresBitmap);
                    if (imageItem != null)
                    {
                        return (sys.Id, imageItem);
                    }
                }

                // Optional filter: keep only bitmap-backed system entries.
                if (includeNonBitmapItems?.Invoke() != true)
                {
                    if (imageLikeFormats)
                    {
                        Logger.Log($"Skipping image-like WinRT history item {sys.Id} because bitmap decode failed. Formats: {string.Join(", ", availableFormats.Take(6))}");
                    }
                    return null;
                }

                if (dp.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
                {
                    var text = await dp.GetTextAsync();
                    var item = ClipboardHistoryItem.FromText(text ?? string.Empty);
                    item.AssignSystemHistoryId(sys.Id);
                    return (sys.Id, item);
                }

                // Preserve recency/order visibility even for clipboard entries we cannot edit directly.
                // We represent unsupported WinRT formats as lightweight text placeholders.
                if (availableFormats != null && availableFormats.Count > 0)
                {
                    var summary = string.Join(", ", availableFormats.Take(6));
                    if (availableFormats.Count > 6)
                    {
                        summary += ", ...";
                    }

                    var placeholder = ClipboardHistoryItem.FromText($"[Unsupported clipboard item: {summary}]");
                    placeholder.AssignSystemHistoryId(sys.Id);
                    return (sys.Id, placeholder);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to convert WinRT history item {sys?.Id}: {ex.Message}");
            }

            return null;
        }

        private static bool LooksImageLike(IEnumerable<string> formats)
        {
            foreach (var format in formats)
            {
                if (string.IsNullOrWhiteSpace(format))
                {
                    continue;
                }

                var lower = format.ToLowerInvariant();
                if (lower.Contains("bitmap") || lower.Contains("image") || lower.Contains("png") || lower.Contains("jpg") || lower.Contains("jpeg") || lower.Contains("gif") || lower.Contains("tiff") || lower.Contains("dib"))
                {
                    return true;
                }
            }

            return false;
        }

        private static async Task<ClipboardHistoryItem?> TryBuildImageItemAsync(Windows.ApplicationModel.DataTransfer.DataPackageView dataPackage, string systemId, bool logFailures)
        {
            try
            {
                Bitmap? bitmap = await TryDecodeBitmapFromBitmapReferenceAsync(dataPackage);
                bitmap ??= await TryDecodeBitmapFromStorageItemsAsync(dataPackage);

                if (bitmap == null)
                {
                    return null;
                }

                using (bitmap)
                {
                    var item = ClipboardHistoryItem.FromImage(bitmap);
                    item.AssignSystemHistoryId(systemId);
                    return item;
                }
            }
            catch (Exception ex)
            {
                if (logFailures)
                {
                    Logger.Log($"Failed to decode WinRT bitmap history item {systemId}: {ex.Message}");
                }

                return null;
            }
        }

        private static async Task<Bitmap?> TryDecodeBitmapFromBitmapReferenceAsync(Windows.ApplicationModel.DataTransfer.DataPackageView dataPackage)
        {
            try
            {
                var bmpRef = await dataPackage.GetBitmapAsync();
                using var stream = await bmpRef.OpenReadAsync();
                using var ms = new MemoryStream();
                using (var dotNetStream = stream.AsStreamForRead())
                {
                    await dotNetStream.CopyToAsync(ms);
                }

                ms.Position = 0;
                try
                {
                    return new Bitmap(ms);
                }
                catch
                {
                    ms.Position = 0;
                    using var decoded = Image.FromStream(ms, false, false);
                    return new Bitmap(decoded);
                }
            }
            catch
            {
                return null;
            }
        }

        private static async Task<Bitmap?> TryDecodeBitmapFromStorageItemsAsync(Windows.ApplicationModel.DataTransfer.DataPackageView dataPackage)
        {
            if (!dataPackage.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                return null;
            }

            try
            {
                var storageItems = await dataPackage.GetStorageItemsAsync();
                if (storageItems == null || storageItems.Count == 0)
                {
                    return null;
                }

                for (int index = 0; index < storageItems.Count; index++)
                {
                    if (storageItems[index] is not Windows.Storage.StorageFile file)
                    {
                        continue;
                    }

                    var contentType = (file.ContentType ?? string.Empty).ToLowerInvariant();
                    var extension = Path.GetExtension(file.Name ?? string.Empty).ToLowerInvariant();
                    bool imageLike = contentType.StartsWith("image/")
                        || extension == ".png"
                        || extension == ".jpg"
                        || extension == ".jpeg"
                        || extension == ".bmp"
                        || extension == ".gif"
                        || extension == ".tif"
                        || extension == ".tiff"
                        || extension == ".webp";

                    if (!imageLike)
                    {
                        continue;
                    }

                    using var stream = await file.OpenReadAsync();
                    using var ms = new MemoryStream();
                    using (var dotNetStream = stream.AsStreamForRead())
                    {
                        await dotNetStream.CopyToAsync(ms);
                    }

                    ms.Position = 0;
                    try
                    {
                        return new Bitmap(ms);
                    }
                    catch
                    {
                        ms.Position = 0;
                        using var decoded = Image.FromStream(ms, false, false);
                        return new Bitmap(decoded);
                    }
                }
            }
            catch
            {
                // Ignore storage-item decode failures and let caller continue with other paths.
            }

            return null;
        }

        private void ApplySnapshot(List<(string id, DateTimeOffset timestamp, ClipboardHistoryItem built)?> translated)
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
            var finalTimestamps = new Dictionary<Guid, DateTimeOffset>();
            var handled = new HashSet<Guid>();

            foreach (var maybe in translated)
            {
                if (!maybe.HasValue) continue;
                var (sysId, timestamp, built) = maybe.Value;

                if (store.ContainsSuppressedSystemHistoryId(sysId))
                {
                    built.Dispose();
                    continue;
                }

                if (tryBindPendingCommittedItem?.Invoke(built) is ClipboardHistoryItem rebound)
                {
                    if (!handled.Contains(rebound.Id))
                    {
                        finalOrder.Add(rebound);
                        finalTimestamps[rebound.Id] = timestamp;
                        handled.Add(rebound.Id);
                    }
                    built.Dispose();
                    continue;
                }

                if (existingById.TryGetValue(sysId, out var existing))
                {
                    finalOrder.Add(existing);
                    finalTimestamps[existing.Id] = timestamp;
                    handled.Add(existing.Id);
                    built.Dispose();
                }
                else
                {
                    finalOrder.Add(built);
                    finalTimestamps[built.Id] = timestamp;
                }
            }

            // Preserve local-only items (user-created duplicates, fallback seed, dirty edits) without
            // pinning stale local screenshots above the newest Windows clipboard history item.
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

                    InsertByTimestamp(finalOrder, finalTimestamps, item, ToHistoryTimestamp(item));
                    handled.Add(item.Id);
                }
                else if (item.IsDirty)
                {
                    // Keep dirty system-backed items, but do not force them to the top when
                    // their current system id is absent from the latest WinRT snapshot.
                    finalOrder.Add(item);
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

        private static void InsertByTimestamp(
            List<ClipboardHistoryItem> items,
            Dictionary<Guid, DateTimeOffset> timestamps,
            ClipboardHistoryItem item,
            DateTimeOffset timestamp)
        {
            int index = 0;
            while (index < items.Count)
            {
                var existingTimestamp = timestamps.TryGetValue(items[index].Id, out var value)
                    ? value
                    : ToHistoryTimestamp(items[index]);

                if (existingTimestamp < timestamp)
                {
                    break;
                }

                index++;
            }

            items.Insert(index, item);
            timestamps[item.Id] = timestamp;
        }

        private static DateTimeOffset ToHistoryTimestamp(ClipboardHistoryItem item)
        {
            var createdUtc = item.CreatedUtc.Kind == DateTimeKind.Utc
                ? item.CreatedUtc
                : DateTime.SpecifyKind(item.CreatedUtc, DateTimeKind.Utc);
            return new DateTimeOffset(createdUtc);
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
