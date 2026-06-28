using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace screenzap.Components
{
    internal enum ClipboardItemKind
    {
        Image,
        // Retained only so persistence can recognize and skip legacy text entries written by older
        // builds. Screenzap no longer creates text items — the clipboard history is image-only.
        Text
    }

    /// <summary>
    /// Pixel size + 16×16 ARGB signature of a stored image blob. Persisted alongside the PNG so
    /// restore can rebuild a <see cref="ClipboardHistoryItem"/> without decoding full images.
    /// </summary>
    internal readonly struct RoleImageMeta
    {
        internal const int SignatureByteLength = 16 * 16 * 4;

        public RoleImageMeta(Size pixelSize, byte[] signature)
        {
            PixelSize = pixelSize;
            Signature = signature;
        }

        public Size PixelSize { get; }
        public byte[] Signature { get; }
    }

    /// <summary>
    /// Immutable PNG content that can stay file-backed until its bytes are genuinely needed.
    /// Persisted history contains hundreds of PNGs; retaining their paths avoids reading the whole
    /// queue into managed memory while the editor is opening.
    /// </summary>
    internal sealed class PngContent
    {
        private readonly object loadGate = new();
        private byte[]? bytes;

        private PngContent(byte[]? bytes, string? backingFilePath)
        {
            this.bytes = bytes;
            BackingFilePath = backingFilePath;
        }

        internal string? BackingFilePath { get; }

        internal bool HasLoadedBytes
        {
            get
            {
                lock (loadGate)
                {
                    return bytes != null;
                }
            }
        }

        internal static PngContent FromBytes(byte[] bytes)
            => new PngContent(bytes ?? throw new ArgumentNullException(nameof(bytes)), null);

        internal static PngContent FromFile(string path)
            => new PngContent(null, Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path))));

        internal byte[] GetBytes()
        {
            lock (loadGate)
            {
                if (bytes == null)
                {
                    if (BackingFilePath == null)
                    {
                        throw new InvalidOperationException("PNG content has neither bytes nor a backing file.");
                    }

                    bytes = File.ReadAllBytes(BackingFilePath);
                }

                return bytes;
            }
        }

        internal Stream OpenRead()
        {
            lock (loadGate)
            {
                if (bytes != null)
                {
                    return new MemoryStream(bytes, writable: false);
                }
            }

            if (BackingFilePath == null)
            {
                throw new InvalidOperationException("PNG content has neither bytes nor a backing file.");
            }

            return new FileStream(BackingFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
    }

    /// <summary>
    /// Represents a single entry in Screenzap's clipboard history list.
    /// Tracks original + current content, dirty state and stashed undo stack so
    /// users can revisit a thumbnail and pick up where they left off.
    ///
    /// Memory model: an item's three image roles (original / committed / current) are immutable
    /// PNG content (<see cref="StoredImage"/>), not live <see cref="Bitmap"/>s. New/edited content
    /// is held compressed; restored content remains file-backed. Full bitmaps are decoded on demand
    /// and released when the item is no longer active, while thumbnails load only as they become
    /// visible. This keeps a 128-image queue from consuming several GB or blocking startup on I/O.
    /// </summary>
    internal sealed class ClipboardHistoryItem : IDisposable
    {
        private const int DefaultThumbnailMaxWidth = 64;
        private const int DefaultThumbnailMaxHeight = 64;

        // Cap for the cached thumbnail source. The panel renders thumbnails at roughly the panel
        // width (~64–85px even at high DPI); keeping a modest source lets RebuildThumbnail rescale
        // on panel resize without decoding the full image, while costing only ~256KB per item.
        private const int ThumbnailSourceMaxEdge = 256;

        // Last dimensions used when building the thumbnail, so that no-arg RebuildThumbnail()
        // (called from SetPreviewComposite during item stash) uses the panel's current size
        // rather than the hardcoded 64×64 default—avoiding shrunk thumbnails at non-100% DPI.
        private int lastThumbMaxWidth = DefaultThumbnailMaxWidth;
        private int lastThumbMaxHeight = DefaultThumbnailMaxHeight;
        private readonly HashSet<string> suppressedSystemHistoryIds = new(StringComparer.Ordinal);

        // Compressed role blobs. Identical roles point at the same instance (copy-on-write).
        private StoredImage? original;
        private StoredImage? committed;
        private StoredImage? current;

        // Lazily-decoded full bitmaps, keyed by the blob they came from so copy-on-write roles share
        // one decode. Populated by the image getters and released via ReleaseDecodedImages() when the
        // item is no longer active, bounding resident full bitmaps to roughly the active item.
        private readonly Dictionary<StoredImage, Bitmap> decodeCache = new(ReferenceEqualityComparer.Instance);

        // Modest-resolution source the panel-sized thumbnail is rescaled from. Built once whenever
        // content changes (the caller already holds the full bitmap then), so panel resizes never
        // decode the full image.
        private Bitmap? thumbnailSource;

        // Lazily-encoded PNG of thumbnailSource for persistence. Restored entries keep this
        // file-backed, so thumbnails below the visible viewport don't even get read at startup.
        private PngContent? thumbnailSourcePngContent;

        private ClipboardHistoryItem(ClipboardItemKind kind, Guid? id = null, DateTime? createdUtc = null)
        {
            Id = id ?? Guid.NewGuid();
            Kind = kind;
            CreatedUtc = createdUtc ?? DateTime.UtcNow;
        }

        public Guid Id { get; }
        public ClipboardItemKind Kind { get; }
        public DateTime CreatedUtc { get; }

        /// <summary>Windows clipboard history item Id (from WinRT), if this entry originated there.</summary>
        public string? SystemHistoryId { get; set; }
        /// <summary>
        /// True when inserted via first-open fallback seeding from current clipboard rather than WinRT history.
        /// Used to reconcile startup races when WinRT history refresh arrives moments later.
        /// </summary>
        internal bool IsSeededFallback { get; set; }

        /// <summary>
        /// True when this item was created by an explicit user "duplicate" action. Such items are
        /// intentional copies, so the system-history sync must not collapse them into an equivalent
        /// re-imported clipboard entry. Persisted so the distinction survives restarts.
        /// </summary>
        internal bool IsUserDuplicate { get; set; }

        // Image getters decode the corresponding blob on demand. The returned bitmap is owned by the
        // item (cached, released on deactivate / dispose); callers must not dispose it.
        public Bitmap? OriginalImage => GetDecoded(original);
        public Bitmap? CommittedImage => GetDecoded(committed);
        public Bitmap? CurrentImage => GetDecoded(current);

        // Immutable compressed content for persistence. Restored roles expose their backing paths,
        // allowing persistence to retain unchanged files without first loading them.
        internal PngContent? OriginalPngContent => original?.Content;
        internal PngContent? CommittedPngContent => committed?.Content;
        internal PngContent? CurrentPngContent => current?.Content;

        // Size + signature per role, persisted so restore can skip the full-image decode that
        // computing them from the PNG would require.
        internal RoleImageMeta? OriginalImageMeta => ToMeta(original);
        internal RoleImageMeta? CommittedImageMeta => ToMeta(committed);
        internal RoleImageMeta? CurrentImageMeta => ToMeta(current);

        private static RoleImageMeta? ToMeta(StoredImage? stored)
            => stored == null ? (RoleImageMeta?)null : new RoleImageMeta(stored.PixelSize, stored.SignatureBytes);

        internal PngContent? ThumbnailSourcePngContent
        {
            get
            {
                if (thumbnailSourcePngContent == null && thumbnailSource != null)
                {
                    using var ms = new MemoryStream();
                    thumbnailSource.Save(ms, ImageFormat.Png);
                    thumbnailSourcePngContent = PngContent.FromBytes(ms.ToArray());
                }

                return thumbnailSourcePngContent;
            }
        }

        public bool IsDirty { get; private set; }
        public IReadOnlyCollection<string> SuppressedSystemHistoryIds => suppressedSystemHistoryIds;
        public bool CanRevertToOriginal => IsDirty || !StoredImage.ContentEquals(original, committed);

        /// <summary>32x32 thumbnail. Owned by the item; regenerated on content change.</summary>
        public Bitmap? Thumbnail { get; private set; }

        /// <summary>Stashed undo state so re-activating this item restores its history.</summary>
        internal UndoRedo.Snapshot? UndoSnapshot { get; set; }

        internal bool TestHasUndoSnapshot() => UndoSnapshot != null && UndoSnapshot.Steps.Count > 0;

        internal string TestDescribeUndoSnapshot() =>
            UndoSnapshot == null ? "null" : $"steps={UndoSnapshot.Steps.Count} index={UndoSnapshot.Index}";

        /// <summary>Stashed annotation state (arrow/rectangle shapes) for image items. Null if never touched.</summary>
        internal List<AnnotationShape>? Annotations { get; set; }

        /// <summary>Stashed text annotations for image items. Null if never touched.</summary>
        internal List<TextAnnotation>? TextAnnotations { get; set; }

        private List<ImageLayer>? imageLayersBackingField;

        /// <summary>
        /// Stashed image layers (smart objects) for image items. Null if never touched.
        /// The setter disposes the previously-held list so callers can simply assign a freshly-cloned list.
        /// </summary>
        internal List<ImageLayer>? ImageLayers
        {
            get => imageLayersBackingField;
            set
            {
                if (!ReferenceEquals(imageLayersBackingField, value))
                {
                    DisposeImageLayerList(imageLayersBackingField);
                }
                imageLayersBackingField = value;
            }
        }

        private static void DisposeImageLayerList(List<ImageLayer>? layers)
        {
            if (layers == null) return;
            foreach (var layer in layers)
            {
                layer.Dispose();
            }
        }

        /// <summary>Optional composited preview (base image + annotations) used only for thumbnail rendering. Does not replace CurrentImage.</summary>
        internal Bitmap? PreviewComposite { get; private set; }

        /// <summary>Update the preview composite (takes ownership of a clone). Used when annotations change but CurrentImage stays the base.</summary>
        internal void SetPreviewComposite(Bitmap? composite)
        {
            PreviewComposite?.Dispose();
            PreviewComposite = composite == null ? null : new Bitmap(composite);
            RefreshThumbnailSource();
        }

        public static ClipboardHistoryItem FromImage(Bitmap source)
        {
            var item = new ClipboardHistoryItem(ClipboardItemKind.Image);
            var stored = StoredImage.FromBitmap(source);
            item.original = stored;
            item.committed = stored;
            item.current = stored;
            item.SetThumbnailSourceFrom(source);
            return item;
        }

        internal static ClipboardHistoryItem FromPersistedPng(
            Guid id,
            DateTime createdUtc,
            byte[] original,
            byte[] committed,
            byte[] current,
            RoleImageMeta? originalMeta = null,
            RoleImageMeta? committedMeta = null,
            RoleImageMeta? currentMeta = null,
            byte[]? thumbnailSourcePng = null)
        {
            var item = new ClipboardHistoryItem(ClipboardItemKind.Image, id, createdUtc);

            // Clean items persist three byte-identical files (the roles shared one blob in memory),
            // so dedup by raw byte-equality before building — the common case builds once, not thrice.
            var originalStored = CreateStored(original, originalMeta);
            var committedStored = BytesEqual(original, committed) ? originalStored : CreateStored(committed, committedMeta);
            StoredImage currentStored;
            if (BytesEqual(committed, current))
            {
                currentStored = committedStored;
            }
            else if (BytesEqual(original, current))
            {
                currentStored = originalStored;
            }
            else
            {
                currentStored = CreateStored(current, currentMeta);
            }

            item.original = originalStored;
            item.committed = committedStored;
            item.current = currentStored;
            item.IsDirty = !StoredImage.ContentEquals(committedStored, currentStored);

            // Keep the thumbnail compressed until its control is actually painted.
            if (thumbnailSourcePng != null && thumbnailSourcePng.Length > 0)
            {
                item.thumbnailSourcePngContent = PngContent.FromBytes(thumbnailSourcePng);
            }

            return item;
        }

        internal static ClipboardHistoryItem FromPersistedFiles(
            Guid id,
            DateTime createdUtc,
            string originalPath,
            string committedPath,
            string currentPath,
            RoleImageMeta originalMeta,
            RoleImageMeta committedMeta,
            RoleImageMeta currentMeta,
            string? thumbnailSourcePath)
        {
            var item = new ClipboardHistoryItem(ClipboardItemKind.Image, id, createdUtc)
            {
                original = StoredImage.FromPngFile(originalPath, originalMeta.PixelSize, originalMeta.Signature),
                committed = StoredImage.FromPngFile(committedPath, committedMeta.PixelSize, committedMeta.Signature),
                current = StoredImage.FromPngFile(currentPath, currentMeta.PixelSize, currentMeta.Signature)
            };

            item.IsDirty = !StoredImage.ContentEquals(item.committed, item.current);
            if (!string.IsNullOrWhiteSpace(thumbnailSourcePath))
            {
                item.thumbnailSourcePngContent = PngContent.FromFile(thumbnailSourcePath);
            }

            return item;
        }

        // With persisted metadata the blob needs no decode at all; without it (older manifest)
        // fall back to decoding the PNG to derive size + signature.
        private static StoredImage CreateStored(byte[] png, RoleImageMeta? meta)
            => meta.HasValue
                ? StoredImage.FromPngTrusted(png, meta.Value.PixelSize, meta.Value.Signature)
                : StoredImage.FromPng(png);

        private bool TryRestoreThumbnailSource()
        {
            if (thumbnailSourcePngContent == null)
            {
                return false;
            }

            try
            {
                using var stream = thumbnailSourcePngContent.OpenRead();
                using var decoded = new Bitmap(stream);
                thumbnailSource?.Dispose();
                // Detach from the backing stream so the bitmap owns its pixels.
                thumbnailSource = new Bitmap(decoded);
                return true;
            }
            catch
            {
                thumbnailSourcePngContent = null;
                return false;
            }
        }

        private static bool BytesEqual(byte[] a, byte[] b) => ReferenceEquals(a, b) || a.AsSpan().SequenceEqual(b);

        public void UpdateCurrentImage(Bitmap updated)
        {
            if (Kind != ClipboardItemKind.Image) return;
            current = StoredImage.FromBitmap(updated);
            PruneDecodeCache();
            IsDirty = !StoredImage.ContentEquals(committed, current);
            SetThumbnailSourceFrom(PreviewComposite ?? updated);
        }

        /// <summary>Update only the base image without recomputing dirty state (caller manages dirty).</summary>
        internal void UpdateCurrentImageWithoutDirty(Bitmap updated)
        {
            if (Kind != ClipboardItemKind.Image) return;
            current = StoredImage.FromBitmap(updated);
            PruneDecodeCache();
        }

        /// <summary>Mark the item as dirty from an external signal (e.g. annotation-only edit).</summary>
        internal void MarkDirtyExternally()
        {
            IsDirty = true;
        }

        public void MarkClean()
        {
            // Treat current state as the new committed baseline, but keep OriginalImage immutable.
            // Sharing the immutable blob means no extra copy.
            if (current != null)
            {
                committed = current;
            }

            // UndoSnapshot is intentionally preserved so undo/revert remain available after commit.
            // Live annotations and layers are cleared because they're now baked into the flattened baseline;
            // each undo step carries its own pre/post snapshot of those for restoration on undo.
            Annotations = null;
            TextAnnotations = null;
            ImageLayers = null;
            PruneDecodeCache();
            SetPreviewComposite(null);
            IsDirty = false;
        }

        public void RevertToOriginal()
        {
            if (original != null)
            {
                current = original;
                committed = original;
            }

            IsDirty = false;
            UndoSnapshot = null;
            Annotations = null;
            TextAnnotations = null;
            ImageLayers = null;
            PruneDecodeCache();
            SetPreviewComposite(null);
        }

        public ClipboardHistoryItem CloneCurrentAsNew()
        {
            var clone = new ClipboardHistoryItem(Kind);
            clone.SystemHistoryId = null;
            clone.IsSeededFallback = false;
            clone.IsUserDuplicate = true;

            foreach (var suppressedId in suppressedSystemHistoryIds)
            {
                clone.suppressedSystemHistoryIds.Add(suppressedId);
            }

            // Blobs are immutable, so the clone shares them outright (copy-on-write splits them only
            // if the clone is later edited). No decode, no re-encode, no large allocation.
            clone.original = original;
            clone.committed = committed;
            clone.current = current;
            clone.PreviewComposite = PreviewComposite == null ? null : new Bitmap(PreviewComposite);

            clone.Annotations = Annotations?.Select(shape => shape.Clone()).ToList();
            clone.TextAnnotations = TextAnnotations?.Select(text => text.Clone()).ToList();
            clone.ImageLayers = ImageLayers?.Select(layer => layer.Clone()).ToList();
            clone.UndoSnapshot = UndoRedo.CloneSnapshot(UndoSnapshot);
            clone.IsDirty = IsDirty;
            clone.lastThumbMaxWidth = lastThumbMaxWidth;
            clone.lastThumbMaxHeight = lastThumbMaxHeight;
            clone.thumbnailSource = thumbnailSource == null ? null : new Bitmap(thumbnailSource);
            clone.thumbnailSourcePngContent = thumbnailSourcePngContent;
            clone.Thumbnail = Thumbnail == null ? null : new Bitmap(Thumbnail);
            return clone;
        }

        internal void AssignSystemHistoryId(string? systemHistoryId)
        {
            if (string.IsNullOrWhiteSpace(systemHistoryId))
            {
                return;
            }

            if (!string.IsNullOrEmpty(SystemHistoryId) && !string.Equals(SystemHistoryId, systemHistoryId, StringComparison.Ordinal))
            {
                suppressedSystemHistoryIds.Add(SystemHistoryId);
            }

            SystemHistoryId = systemHistoryId;
        }

        internal void AddSuppressedSystemHistoryId(string? systemHistoryId)
        {
            if (!string.IsNullOrWhiteSpace(systemHistoryId))
            {
                suppressedSystemHistoryIds.Add(systemHistoryId);
            }
        }

        internal bool ContainsSuppressedSystemHistoryId(string? systemHistoryId)
        {
            return !string.IsNullOrWhiteSpace(systemHistoryId) && suppressedSystemHistoryIds.Contains(systemHistoryId);
        }

        internal bool ContentMatches(ClipboardHistoryItem other)
        {
            if (other == null) return false;
            // Compares cached 16×16 signatures — never decodes the full images.
            return StoredImage.ContentEquals(current, other.current);
        }

        internal void SetDirtyFlagForRestore(bool isDirty)
        {
            IsDirty = isDirty;
        }

        internal Size GetThumbnailDisplaySize(int maxWidth, int maxHeight)
        {
            var sourceSize = thumbnailSource?.Size ?? current?.PixelSize ?? Size.Empty;
            return CalculateThumbnailSize(sourceSize, maxWidth, maxHeight);
        }

        internal bool HasLoadedPngBytesForDiagnostics =>
            (original?.Content.HasLoadedBytes ?? false)
            || (committed?.Content.HasLoadedBytes ?? false)
            || (current?.Content.HasLoadedBytes ?? false);

        internal bool HasLoadedThumbnailForDiagnostics => thumbnailSource != null || Thumbnail != null;

        /// <summary>
        /// Dispose any decoded full bitmaps, keeping the compressed blobs and thumbnail. Call when the
        /// item is no longer the active/edited entry so resident full bitmaps stay bounded.
        /// </summary>
        internal void ReleaseDecodedImages()
        {
            if (decodeCache.Count == 0) return;
            foreach (var bmp in decodeCache.Values)
            {
                bmp.Dispose();
            }
            decodeCache.Clear();
        }

        public void RebuildThumbnail()
        {
            RebuildThumbnail(lastThumbMaxWidth, lastThumbMaxHeight);
        }

        public void RebuildThumbnail(int maxWidth, int maxHeight)
        {
            maxWidth = Math.Max(1, maxWidth);
            maxHeight = Math.Max(1, maxHeight);

            lastThumbMaxWidth = maxWidth;
            lastThumbMaxHeight = maxHeight;

            using var perf = LoggerPerfScope(maxWidth, maxHeight);

            EnsureThumbnailSource();

            // Rescale from the small cached source — no full-image decode on panel resize.
            var previous = Thumbnail;
            Thumbnail = RenderImageThumbnail(thumbnailSource, maxWidth, maxHeight);
            previous?.Dispose();
        }

        private Bitmap? GetDecoded(StoredImage? stored)
        {
            if (stored == null) return null;
            if (decodeCache.TryGetValue(stored, out var cached))
            {
                return cached;
            }

            var bmp = stored.Decode();
            decodeCache[stored] = bmp;
            return bmp;
        }

        private void EnsureThumbnailSource()
        {
            if (thumbnailSource != null || TryRestoreThumbnailSource())
            {
                return;
            }

            if (PreviewComposite != null)
            {
                thumbnailSource = RenderImageThumbnail(PreviewComposite, ThumbnailSourceMaxEdge, ThumbnailSourceMaxEdge);
                return;
            }

            if (current == null)
            {
                return;
            }

            // Legacy/corrupt entries without a persisted thumbnail pay for one full decode only
            // when they become visible, never while the editor window is being constructed.
            using var full = current.Decode();
            thumbnailSource = RenderImageThumbnail(full, ThumbnailSourceMaxEdge, ThumbnailSourceMaxEdge);
        }

        // Drop decoded bitmaps whose blob is no longer referenced by any role (e.g. after an edit
        // replaced `current`).
        private void PruneDecodeCache()
        {
            if (decodeCache.Count == 0) return;
            List<StoredImage>? dead = null;
            foreach (var key in decodeCache.Keys)
            {
                if (!ReferenceEquals(key, original) && !ReferenceEquals(key, committed) && !ReferenceEquals(key, current))
                {
                    (dead ??= new List<StoredImage>()).Add(key);
                }
            }
            if (dead == null) return;
            foreach (var key in dead)
            {
                decodeCache[key].Dispose();
                decodeCache.Remove(key);
            }
        }

        // Rebuild the thumbnail source from whatever content is current, decoding once if needed.
        private void RefreshThumbnailSource()
        {
            if (PreviewComposite != null)
            {
                SetThumbnailSourceFrom(PreviewComposite);
                return;
            }

            if (current == null)
            {
                SetThumbnailSourceFrom(null);
                return;
            }

            // One-shot decode (not cached) just to derive the small source; the full bitmap is freed
            // immediately so it doesn't add to resident memory.
            using var full = current.Decode();
            SetThumbnailSourceFrom(full);
        }

        private void SetThumbnailSourceFrom(Bitmap? full)
        {
            thumbnailSource?.Dispose();
            thumbnailSource = full == null ? null : RenderImageThumbnail(full, ThumbnailSourceMaxEdge, ThumbnailSourceMaxEdge);
            thumbnailSourcePngContent = null;
            RebuildThumbnail(lastThumbMaxWidth, lastThumbMaxHeight);
        }

        private IDisposable LoggerPerfScope(int maxWidth, int maxHeight)
        {
            return lib.PerfTrace.Scope(
                "ClipboardHistoryItem.RebuildThumbnail",
                () => $"kind={Kind} max={maxWidth}x{maxHeight}",
                slowMs: 20,
                summaryEvery: 100);
        }

        private static Bitmap RenderImageThumbnail(Bitmap? source, int maxWidth, int maxHeight)
        {
            if (source == null || source.Width <= 0 || source.Height <= 0)
            {
                var fallback = new Bitmap(maxWidth, maxHeight, PixelFormat.Format32bppArgb);
                using var fg = Graphics.FromImage(fallback);
                fg.Clear(Color.FromArgb(32, 32, 32));
                return fallback;
            }

            var thumbnailSize = CalculateThumbnailSize(source.Size, maxWidth, maxHeight);
            int w = thumbnailSize.Width;
            int h = thumbnailSize.Height;

            var thumb = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(thumb);
            g.Clear(Color.FromArgb(32, 32, 32));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(source, new Rectangle(0, 0, w, h));
            return thumb;
        }

        private static Size CalculateThumbnailSize(Size sourceSize, int maxWidth, int maxHeight)
        {
            maxWidth = Math.Max(1, maxWidth);
            maxHeight = Math.Max(1, maxHeight);
            if (sourceSize.Width <= 0 || sourceSize.Height <= 0)
            {
                return new Size(maxWidth, maxHeight);
            }

            // Fit inside the thumbnail bounds while preserving aspect ratio.
            float widthScale = (float)maxWidth / sourceSize.Width;
            float heightScale = (float)maxHeight / sourceSize.Height;
            float scale = Math.Min(widthScale, heightScale);
            return new Size(
                Math.Max(1, (int)Math.Round(sourceSize.Width * scale)),
                Math.Max(1, (int)Math.Round(sourceSize.Height * scale)));
        }

        public void Dispose()
        {
            ReleaseDecodedImages();
            thumbnailSource?.Dispose();
            PreviewComposite?.Dispose();
            Thumbnail?.Dispose();
            ImageLayers = null; // setter disposes layer bitmaps
            original = null;
            committed = null;
            current = null;
            thumbnailSource = null;
            thumbnailSourcePngContent = null;
            PreviewComposite = null;
            Thumbnail = null;
            suppressedSystemHistoryIds.Clear();
        }

        /// <summary>
        /// Immutable, PNG-compressed image content plus a cheap 16×16 signature for equality checks.
        /// Shared across roles and across cloned items (copy-on-write) since it never mutates.
        /// </summary>
        private sealed class StoredImage
        {
            public PngContent Content { get; }
            public Size PixelSize { get; }
            private readonly byte[] signature; // 16×16 ARGB downsample

            private StoredImage(PngContent content, Size pixelSize, byte[] signature)
            {
                Content = content;
                PixelSize = pixelSize;
                this.signature = signature;
            }

            public static StoredImage FromBitmap(Bitmap bmp)
            {
                byte[] png;
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    png = ms.ToArray();
                }
                return new StoredImage(PngContent.FromBytes(png), bmp.Size, ComputeSignature(bmp));
            }

            public static StoredImage FromPng(byte[] png)
            {
                using var ms = new MemoryStream(png, writable: false);
                using var bmp = new Bitmap(ms);
                return new StoredImage(PngContent.FromBytes((byte[])png.Clone()), bmp.Size, ComputeSignature(bmp));
            }

            /// <summary>
            /// Wraps persisted bytes with their persisted size + signature — no decode. Takes
            /// ownership of <paramref name="png"/>; callers must not mutate it afterwards.
            /// </summary>
            public static StoredImage FromPngTrusted(byte[] png, Size pixelSize, byte[] signature)
            {
                return new StoredImage(PngContent.FromBytes(png), pixelSize, signature);
            }

            public static StoredImage FromPngFile(string path, Size pixelSize, byte[] signature)
            {
                return new StoredImage(PngContent.FromFile(path), pixelSize, signature);
            }

            public byte[] SignatureBytes => signature;

            public Bitmap Decode()
            {
                using var stream = Content.OpenRead();
                using var decoded = new Bitmap(stream);
                // Detach from the backing stream so the returned bitmap owns its pixels.
                return new Bitmap(decoded);
            }

            public static bool ContentEquals(StoredImage? a, StoredImage? b)
            {
                if (ReferenceEquals(a, b)) return true;
                if (a == null || b == null) return false;
                if (a.PixelSize != b.PixelSize) return false;
                return a.signature.AsSpan().SequenceEqual(b.signature);
            }

            private static byte[] ComputeSignature(Bitmap bmp)
            {
                // Downsample to 16x16 and dump ARGB bytes for a cheap, re-encode-tolerant equality check.
                using var small = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
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
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<StoredImage>
        {
            public static readonly ReferenceEqualityComparer Instance = new();
            public bool Equals(StoredImage? x, StoredImage? y) => ReferenceEquals(x, y);
            public int GetHashCode(StoredImage obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
