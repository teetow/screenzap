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
        private bool disposed;
        private bool subscribed;

        // Single-flight + debounce guard. A burst of HistoryChanged events collapses into at most
        // one in-flight refresh plus one queued rerun, instead of N overlapping full scans all
        // hammering the single-threaded clipboard service (cbdhsvc) concurrently.
        private readonly object refreshGate = new object();
        private bool refreshInProgress;
        private bool refreshRequested;
        private TaskCompletionSource<bool>? refreshDrain;

        // Snapshot of SystemHistoryIds currently held in the store, published from ApplySnapshot
        // (UI thread). DoRefreshOnceAsync reads it to skip re-decoding bitmaps we already have.
        private volatile HashSet<string> knownSystemHistoryIds = new(StringComparer.Ordinal);

        private const int RefreshDebounceMs = 120;
        private const int WinRtOperationTimeoutMs = 4000;

        public SystemClipboardHistoryService(
            ClipboardHistoryStore store,
            Control uiDispatcher,
            Action<ClipboardHistoryItem>? onItemObserved,
            Func<ClipboardHistoryItem, ClipboardHistoryItem?>? tryBindPendingCommittedItem,
            Func<bool>? isInternalWriteWindow)
        {
            this.store = store;
            this.poster = new SynchronizationContextPoster(uiDispatcher);
            this.onItemObserved = onItemObserved;
            this.tryBindPendingCommittedItem = tryBindPendingCommittedItem;
            this.isInternalWriteWindow = isInternalWriteWindow;

            // The store may already contain the persisted queue when this service is constructed.
            // Prime the snapshot now so the first WinRT refresh reuses those entries instead of
            // re-streaming and re-encoding every image once before ApplySnapshot can populate it.
            knownSystemHistoryIds = new HashSet<string>(
                store.Items
                    .Select(item => item.SystemHistoryId)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Select(id => id!),
                StringComparer.Ordinal);
        }

        internal IReadOnlyCollection<string> KnownSystemHistoryIdsForDiagnostics => knownSystemHistoryIds;

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

        /// <summary>
        /// Requests a refresh of the system clipboard history. Coalescing: if a refresh is already
        /// running, this only marks a rerun rather than starting an overlapping scan, so a burst of
        /// clipboard changes never piles concurrent full scans onto the single-threaded clipboard
        /// service. The returned task completes when the in-flight/just-started cycle drains.
        /// </summary>
        public Task RefreshAsync()
        {
            lock (refreshGate)
            {
                if (disposed) return Task.CompletedTask;

                refreshDrain ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var drain = refreshDrain.Task;

                if (refreshInProgress)
                {
                    refreshRequested = true;
                    return drain;
                }

                refreshInProgress = true;
                _ = Task.Run(RefreshLoopAsync);
                return drain;
            }
        }

        private async Task RefreshLoopAsync()
        {
            while (true)
            {
                try
                {
                    // Brief debounce so a rapid burst of HistoryChanged events settles into one scan.
                    await Task.Delay(RefreshDebounceMs).ConfigureAwait(false);
                    await DoRefreshOnceAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Clipboard history refresh failed: {ex.Message}");
                }

                TaskCompletionSource<bool>? toSignal;
                lock (refreshGate)
                {
                    if (refreshRequested && !disposed)
                    {
                        refreshRequested = false;
                        continue;
                    }

                    refreshInProgress = false;
                    toSignal = refreshDrain;
                    refreshDrain = null;
                }

                toSignal?.TrySetResult(true);
                return;
            }
        }

        private async Task DoRefreshOnceAsync()
        {
            if (disposed) return;

            var result = await AwaitWithTimeout(WinRtClipboard.GetHistoryItemsAsync(), "GetHistoryItemsAsync").ConfigureAwait(false);
            if (result == null)
            {
                Logger.Log("WinRT GetHistoryItemsAsync returned null or timed out.");
                return;
            }

            if (result.Status.ToString() != "Success")
            {
                Logger.Log($"WinRT GetHistoryItemsAsync returned non-success status: {result.Status}");
                return;
            }

            var known = knownSystemHistoryIds;

            // Translate WinRT items and then order by WinRT timestamp (newest first).
            var translated = new List<(string id, DateTimeOffset timestamp, ClipboardHistoryItem? built)?>();
            foreach (var sys in result.Items)
            {
                if (disposed) return;

                // Skip the expensive bitmap decode for items we already hold; ApplySnapshot reuses
                // the existing store item by SystemHistoryId. Avoids re-streaming every image out of
                // the single-threaded clipboard service on every change.
                if (!string.IsNullOrEmpty(sys.Id) && known.Contains(sys.Id))
                {
                    translated.Add((sys.Id, sys.Timestamp, (ClipboardHistoryItem?)null));
                    continue;
                }

                var converted = await TryConvertAsync(sys).ConfigureAwait(false);
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

        private static async Task<T?> AwaitWithTimeout<T>(Windows.Foundation.IAsyncOperation<T> operation, string label)
            where T : class
        {
            var task = operation.AsTask();
            var winner = await Task.WhenAny(task, Task.Delay(WinRtOperationTimeoutMs)).ConfigureAwait(false);
            if (winner != task)
            {
                Logger.Log($"WinRT operation timed out after {WinRtOperationTimeoutMs}ms: {label}");
                try { operation.Cancel(); } catch { /* best effort */ }
                return null;
            }

            try
            {
                return await task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Log($"WinRT operation failed ({label}): {ex.Message}");
                return null;
            }
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

                // Screenzap's history is image-only. Some providers do not reliably advertise bitmap
                // support, so probe likely image formats too; everything that isn't a decodable bitmap
                // (text, files, etc.) is ignored.
                if (declaresBitmap || imageLikeFormats)
                {
                    var imageItem = await TryBuildImageItemAsync(dp, sys.Id, logFailures: declaresBitmap);
                    if (imageItem != null)
                    {
                        return (sys.Id, imageItem);
                    }

                    Logger.Log($"Skipping image-like WinRT history item {sys.Id} because bitmap decode failed. Formats: {string.Join(", ", availableFormats.Take(6))}");
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
                var bmpRef = await AwaitWithTimeout(dataPackage.GetBitmapAsync(), "GetBitmapAsync").ConfigureAwait(false);
                if (bmpRef == null) return null;
                using var stream = await AwaitWithTimeout(bmpRef.OpenReadAsync(), "BitmapReference.OpenReadAsync").ConfigureAwait(false);
                if (stream == null) return null;
                using var ms = new MemoryStream();
                using (var dotNetStream = stream.AsStreamForRead())
                {
                    await dotNetStream.CopyToAsync(ms);
                }

                ms.Position = 0;
                try
                {
                    // Detach from ms: a stream-backed Bitmap breaks once ms is disposed (draw-copies
                    // happen to work, but Save() throws a generic GDI+ error on PNG re-encode).
                    using var streamBacked = new Bitmap(ms);
                    return new Bitmap(streamBacked);
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
                var storageItems = await AwaitWithTimeout(dataPackage.GetStorageItemsAsync(), "GetStorageItemsAsync").ConfigureAwait(false);
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

                    using var stream = await AwaitWithTimeout(file.OpenReadAsync(), "StorageFile.OpenReadAsync").ConfigureAwait(false);
                    if (stream == null) continue;
                    using var ms = new MemoryStream();
                    using (var dotNetStream = stream.AsStreamForRead())
                    {
                        await dotNetStream.CopyToAsync(ms);
                    }

                    ms.Position = 0;
                    try
                    {
                        // Detach from ms — see TryDecodeBitmapFromBitmapReferenceAsync.
                        using var streamBacked = new Bitmap(ms);
                        return new Bitmap(streamBacked);
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

        private void ApplySnapshot(List<(string id, DateTimeOffset timestamp, ClipboardHistoryItem? built)?> translated)
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
                    built?.Dispose();
                    continue;
                }

                if (built != null && tryBindPendingCommittedItem?.Invoke(built) is ClipboardHistoryItem rebound)
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
                    built?.Dispose();
                }
                else if (built != null)
                {
                    // A system entry can re-appear for content we already hold as a local-only item —
                    // e.g. the entry produced by a "set as active"/commit write whose in-memory rebind
                    // window was lost across a restart. Absorb it into that item (claim the new system
                    // id) instead of creating an identical twin. Seeded fallbacks are handled by the
                    // dedup pass below; user-created duplicates are exempt so intentional copies survive.
                    var absorbTarget = store.Items.FirstOrDefault(candidate =>
                        !handled.Contains(candidate.Id)
                        && string.IsNullOrEmpty(candidate.SystemHistoryId)
                        && !candidate.IsDirty
                        && !candidate.IsSeededFallback
                        && !candidate.IsUserDuplicate
                        && candidate.ContentMatches(built));

                    if (absorbTarget != null)
                    {
                        absorbTarget.AssignSystemHistoryId(built.SystemHistoryId);
                        finalOrder.Add(absorbTarget);
                        finalTimestamps[absorbTarget.Id] = timestamp;
                        handled.Add(absorbTarget.Id);
                        built.Dispose();
                    }
                    else
                    {
                        finalOrder.Add(built);
                        finalTimestamps[built.Id] = timestamp;
                    }
                }
                // else: decode was skipped for a known id, but the item is no longer in the store
                // (removed between snapshot publish and now). Drop silently; the next refresh sees
                // it as unknown and re-fetches it.
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
                    // System item no longer present in the Windows clipboard history. This happens on
                    // an OS reboot, a "Clear" in clipboard settings, or when Windows evicts old entries
                    // at its own size cap — none of which mean the user wanted it gone. We hold our own
                    // persisted copy, so demote it to a local-only entry and keep it rather than discard
                    // data. (Deletions initiated inside Screenzap go through the suppression path above,
                    // so this does not resurrect those.)
                    item.SystemHistoryId = null;
                    InsertByTimestamp(finalOrder, finalTimestamps, item, ToHistoryTimestamp(item));
                    handled.Add(item.Id);
                }
            }

            store.ReplaceAll(finalOrder);

            // Publish the set of system ids we now hold so the next refresh can skip re-decoding them.
            var updatedKnown = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in finalOrder)
            {
                if (!string.IsNullOrEmpty(entry.SystemHistoryId))
                {
                    updatedKnown.Add(entry.SystemHistoryId!);
                }
            }
            knownSystemHistoryIds = updatedKnown;

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

                // ContentMatches compares cached 16×16 signatures, so this scan never decodes the
                // full (compressed) images — important because it runs per item on every sync.
                if (item.ContentMatches(candidate))
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

                var result = await AwaitWithTimeout(WinRtClipboard.GetHistoryItemsAsync(), "GetHistoryItemsAsync (delete)").ConfigureAwait(false);
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
