using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;

namespace screenzap.Components
{
    internal enum ClipboardItemKind
    {
        Image,
        Text
    }

    /// <summary>
    /// Represents a single entry in Screenzap's clipboard history list.
    /// Tracks original + current content, dirty state and stashed undo stack so
    /// users can revisit a thumbnail and pick up where they left off.
    /// </summary>
    internal sealed class ClipboardHistoryItem : IDisposable
    {
        private const int DefaultThumbnailMaxWidth = 64;
        private const int DefaultThumbnailMaxHeight = 64;
        private const float TextThumbnailAspect = 2f / 3f; // 2:3 portrait (w:h)
        private readonly HashSet<string> suppressedSystemHistoryIds = new(StringComparer.Ordinal);

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

        // Originals (immutable after construction via setters from store).
        public Bitmap? OriginalImage { get; private set; }
        public string? OriginalText { get; private set; }

        // Last committed baseline (what "Accept edits" wrote to clipboard).
        public Bitmap? CommittedImage { get; private set; }
        public string? CommittedText { get; private set; }

        // Current edited state.
        public Bitmap? CurrentImage { get; private set; }
        public string? CurrentText { get; private set; }

        public bool IsDirty { get; private set; }
        public IReadOnlyCollection<string> SuppressedSystemHistoryIds => suppressedSystemHistoryIds;
        public bool CanRevertToOriginal
        {
            get
            {
                if (Kind == ClipboardItemKind.Image)
                {
                    return IsDirty || !AreImagesEqual(OriginalImage, CommittedImage);
                }

                return IsDirty || !string.Equals(OriginalText ?? string.Empty, CommittedText ?? string.Empty, StringComparison.Ordinal);
            }
        }

        /// <summary>32x32 thumbnail. Owned by the item; regenerated on content change.</summary>
        public Bitmap? Thumbnail { get; private set; }

        /// <summary>Stashed undo state so re-activating this item restores its history.</summary>
        internal UndoRedo.Snapshot? UndoSnapshot { get; set; }

        /// <summary>Stashed annotation state (arrow/rectangle shapes) for image items. Null if never touched.</summary>
        internal List<AnnotationShape>? Annotations { get; set; }

        /// <summary>Stashed text annotations for image items. Null if never touched.</summary>
        internal List<TextAnnotation>? TextAnnotations { get; set; }

        /// <summary>Optional composited preview (base image + annotations) used only for thumbnail rendering. Does not replace CurrentImage.</summary>
        internal Bitmap? PreviewComposite { get; private set; }

        /// <summary>Update the preview composite (takes ownership of a clone). Used when annotations change but CurrentImage stays the base.</summary>
        internal void SetPreviewComposite(Bitmap? composite)
        {
            PreviewComposite?.Dispose();
            PreviewComposite = composite == null ? null : new Bitmap(composite);
            RebuildThumbnail();
        }

        public static ClipboardHistoryItem FromImage(Bitmap source)
        {
            var item = new ClipboardHistoryItem(ClipboardItemKind.Image);
            item.OriginalImage = new Bitmap(source);
            item.CommittedImage = new Bitmap(source);
            item.CurrentImage = new Bitmap(source);
            item.RebuildThumbnail();
            return item;
        }

        public static ClipboardHistoryItem FromText(string source)
        {
            var item = new ClipboardHistoryItem(ClipboardItemKind.Text);
            item.OriginalText = source ?? string.Empty;
            item.CommittedText = source ?? string.Empty;
            item.CurrentText = source ?? string.Empty;
            item.RebuildThumbnail();
            return item;
        }

        internal static ClipboardHistoryItem FromPersistedImage(Guid id, DateTime createdUtc, Bitmap original, Bitmap committed, Bitmap current)
        {
            var item = new ClipboardHistoryItem(ClipboardItemKind.Image, id, createdUtc)
            {
                OriginalImage = new Bitmap(original),
                CommittedImage = new Bitmap(committed),
                CurrentImage = new Bitmap(current)
            };
            item.IsDirty = !AreImagesEqual(item.CommittedImage, item.CurrentImage);
            item.RebuildThumbnail();
            return item;
        }

        internal static ClipboardHistoryItem FromPersistedText(Guid id, DateTime createdUtc, string original, string committed, string current)
        {
            var item = new ClipboardHistoryItem(ClipboardItemKind.Text, id, createdUtc)
            {
                OriginalText = original ?? string.Empty,
                CommittedText = committed ?? string.Empty,
                CurrentText = current ?? string.Empty
            };
            item.IsDirty = !string.Equals(item.CommittedText ?? string.Empty, item.CurrentText ?? string.Empty, StringComparison.Ordinal);
            item.RebuildThumbnail();
            return item;
        }

        public void UpdateCurrentImage(Bitmap updated)
        {
            if (Kind != ClipboardItemKind.Image) return;
            var replacement = new Bitmap(updated);
            CurrentImage?.Dispose();
            CurrentImage = replacement;
            IsDirty = !AreImagesEqual(CommittedImage, CurrentImage);
            RebuildThumbnail();
        }

        /// <summary>Update only the base image without recomputing dirty state (caller manages dirty).</summary>
        internal void UpdateCurrentImageWithoutDirty(Bitmap updated)
        {
            if (Kind != ClipboardItemKind.Image) return;
            var replacement = new Bitmap(updated);
            CurrentImage?.Dispose();
            CurrentImage = replacement;
        }

        /// <summary>Mark the item as dirty from an external signal (e.g. annotation-only edit).</summary>
        internal void MarkDirtyExternally()
        {
            IsDirty = true;
        }

        public void UpdateCurrentText(string updated)
        {
            if (Kind != ClipboardItemKind.Text) return;
            CurrentText = updated ?? string.Empty;
            IsDirty = !string.Equals(CommittedText ?? string.Empty, CurrentText, StringComparison.Ordinal);
            RebuildThumbnail();
        }

        public void MarkClean()
        {
            // Treat current state as the new committed baseline, but keep Original* immutable.
            if (Kind == ClipboardItemKind.Image && CurrentImage != null)
            {
                CommittedImage?.Dispose();
                CommittedImage = new Bitmap(CurrentImage);
            }
            else if (Kind == ClipboardItemKind.Text)
            {
                CommittedText = CurrentText ?? string.Empty;
            }

            UndoSnapshot = null;
            Annotations = null;
            TextAnnotations = null;
            SetPreviewComposite(null);
            IsDirty = false;
        }

        public void RevertToOriginal()
        {
            if (Kind == ClipboardItemKind.Image && OriginalImage != null)
            {
                CurrentImage?.Dispose();
                CurrentImage = new Bitmap(OriginalImage);
                CommittedImage?.Dispose();
                CommittedImage = new Bitmap(OriginalImage);
            }
            else if (Kind == ClipboardItemKind.Text)
            {
                CurrentText = OriginalText ?? string.Empty;
                CommittedText = OriginalText ?? string.Empty;
            }

            IsDirty = false;
            UndoSnapshot = null;
            Annotations = null;
            TextAnnotations = null;
            RebuildThumbnail();
        }

        public ClipboardHistoryItem CloneCurrentAsNew()
        {
            var clone = new ClipboardHistoryItem(Kind);
            clone.SystemHistoryId = null;
            clone.IsSeededFallback = false;

            foreach (var suppressedId in suppressedSystemHistoryIds)
            {
                clone.suppressedSystemHistoryIds.Add(suppressedId);
            }

            if (Kind == ClipboardItemKind.Image)
            {
                clone.OriginalImage = OriginalImage == null ? null : new Bitmap(OriginalImage);
                clone.CommittedImage = CommittedImage == null ? null : new Bitmap(CommittedImage);
                clone.CurrentImage = CurrentImage == null ? null : new Bitmap(CurrentImage);
                clone.PreviewComposite = PreviewComposite == null ? null : new Bitmap(PreviewComposite);
            }
            else
            {
                clone.OriginalText = OriginalText ?? string.Empty;
                clone.CommittedText = CommittedText ?? string.Empty;
                clone.CurrentText = CurrentText ?? string.Empty;
            }

            clone.Annotations = Annotations?.Select(shape => shape.Clone()).ToList();
            clone.TextAnnotations = TextAnnotations?.Select(text => text.Clone()).ToList();
            clone.UndoSnapshot = UndoRedo.CloneSnapshot(UndoSnapshot);
            clone.IsDirty = IsDirty;
            clone.RebuildThumbnail();
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
            if (other == null || Kind != other.Kind)
            {
                return false;
            }

            if (Kind == ClipboardItemKind.Image)
            {
                return AreImagesEqual(CurrentImage, other.CurrentImage);
            }

            return string.Equals(CurrentText ?? string.Empty, other.CurrentText ?? string.Empty, StringComparison.Ordinal);
        }

        internal void SetDirtyFlagForRestore(bool isDirty)
        {
            IsDirty = isDirty;
        }

        public void RebuildThumbnail()
        {
            RebuildThumbnail(DefaultThumbnailMaxWidth, DefaultThumbnailMaxHeight);
        }

        public void RebuildThumbnail(int maxWidth, int maxHeight)
        {
            maxWidth = Math.Max(1, maxWidth);
            maxHeight = Math.Max(1, maxHeight);

            var previous = Thumbnail;
            Thumbnail = Kind == ClipboardItemKind.Image
                ? RenderImageThumbnail(PreviewComposite ?? CurrentImage, maxWidth, maxHeight)
                : RenderTextThumbnail(CurrentText ?? string.Empty, maxWidth, maxHeight);
            previous?.Dispose();
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

            // Fit inside the thumbnail bounds while preserving aspect ratio.
            float widthScale = (float)maxWidth / source.Width;
            float heightScale = (float)maxHeight / source.Height;
            float scale = Math.Min(widthScale, heightScale);
            int w = Math.Max(1, (int)Math.Round(source.Width * scale));
            int h = Math.Max(1, (int)Math.Round(source.Height * scale));

            var thumb = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(thumb);
            g.Clear(Color.FromArgb(32, 32, 32));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(source, new Rectangle(0, 0, w, h));
            return thumb;
        }

        private static Bitmap RenderTextThumbnail(string text, int maxWidth, int maxHeight)
        {
            // Keep text cards in a portrait 2:3 shape while fitting the available bounds.
            int h = maxHeight;
            int w = Math.Max(1, (int)Math.Round(h * TextThumbnailAspect));
            if (w > maxWidth)
            {
                w = maxWidth;
                h = Math.Max(1, (int)Math.Round(w / TextThumbnailAspect));
            }

            var thumb = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(thumb);
            g.Clear(Color.FromArgb(40, 50, 65));
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            var preview = (text ?? string.Empty).Trim();
            if (preview.Length == 0)
            {
                using var emptyFont = new Font("Segoe UI", 7f, FontStyle.Italic);
                TextRenderer.DrawText(g, "(empty)", emptyFont, new Rectangle(0, 0, w, h),
                    Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return thumb;
            }

            if (preview.Length > 80) preview = preview.Substring(0, 80);
            using var font = new Font("Segoe UI", 6f, FontStyle.Regular);
            TextRenderer.DrawText(
                g, preview, font,
                new Rectangle(3, 3, w - 6, h - 6),
                Color.White,
                TextFormatFlags.WordBreak | TextFormatFlags.Top | TextFormatFlags.Left | TextFormatFlags.NoPrefix);

            // Subtle "T" badge bottom-right so text items are recognizable at a glance.
            using var badgeFont = new Font("Segoe UI", 7f, FontStyle.Bold);
            TextRenderer.DrawText(g, "T", badgeFont, new Point(w - 12, h - 14),
                Color.FromArgb(180, 220, 255), TextFormatFlags.NoPrefix);

            return thumb;
        }

        private static bool AreImagesEqual(Bitmap? a, Bitmap? b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Size != b.Size) return false;
            // Cheap signature: hash of downsampled bytes.
            return ImageSignature(a).SequenceEqual(ImageSignature(b));
        }

        private static byte[] ImageSignature(Bitmap bmp)
        {
            // Downsample to 16x16 and dump ARGB bytes for a cheap equality check.
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

        public void Dispose()
        {
            OriginalImage?.Dispose();
            CommittedImage?.Dispose();
            CurrentImage?.Dispose();
            PreviewComposite?.Dispose();
            Thumbnail?.Dispose();
            OriginalImage = null;
            CommittedImage = null;
            CurrentImage = null;
            PreviewComposite = null;
            Thumbnail = null;
            suppressedSystemHistoryIds.Clear();
        }
    }
}
