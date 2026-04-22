using System;
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
        private const int ThumbnailSize = 32;

        public ClipboardHistoryItem(ClipboardItemKind kind)
        {
            Id = Guid.NewGuid();
            Kind = kind;
            CreatedUtc = DateTime.UtcNow;
        }

        public Guid Id { get; }
        public ClipboardItemKind Kind { get; }
        public DateTime CreatedUtc { get; }

        // Originals (immutable after construction via setters from store).
        public Bitmap? OriginalImage { get; private set; }
        public string? OriginalText { get; private set; }

        // Current edited state.
        public Bitmap? CurrentImage { get; private set; }
        public string? CurrentText { get; private set; }

        public bool IsDirty { get; private set; }

        /// <summary>32x32 thumbnail. Owned by the item; regenerated on content change.</summary>
        public Bitmap? Thumbnail { get; private set; }

        /// <summary>Stashed undo state so re-activating this item restores its history.</summary>
        internal UndoRedo.Snapshot? UndoSnapshot { get; set; }

        public static ClipboardHistoryItem FromImage(Bitmap source)
        {
            var item = new ClipboardHistoryItem(ClipboardItemKind.Image);
            item.OriginalImage = new Bitmap(source);
            item.CurrentImage = new Bitmap(source);
            item.RebuildThumbnail();
            return item;
        }

        public static ClipboardHistoryItem FromText(string source)
        {
            var item = new ClipboardHistoryItem(ClipboardItemKind.Text);
            item.OriginalText = source ?? string.Empty;
            item.CurrentText = source ?? string.Empty;
            item.RebuildThumbnail();
            return item;
        }

        public void UpdateCurrentImage(Bitmap updated)
        {
            if (Kind != ClipboardItemKind.Image) return;
            var replacement = new Bitmap(updated);
            CurrentImage?.Dispose();
            CurrentImage = replacement;
            IsDirty = !AreImagesEqual(OriginalImage, CurrentImage);
            RebuildThumbnail();
        }

        public void UpdateCurrentText(string updated)
        {
            if (Kind != ClipboardItemKind.Text) return;
            CurrentText = updated ?? string.Empty;
            IsDirty = !string.Equals(OriginalText ?? string.Empty, CurrentText, StringComparison.Ordinal);
            RebuildThumbnail();
        }

        public void MarkClean()
        {
            // Treat current state as the new baseline.
            if (Kind == ClipboardItemKind.Image && CurrentImage != null)
            {
                OriginalImage?.Dispose();
                OriginalImage = new Bitmap(CurrentImage);
            }
            else if (Kind == ClipboardItemKind.Text)
            {
                OriginalText = CurrentText ?? string.Empty;
            }

            IsDirty = false;
        }

        public void RevertToOriginal()
        {
            if (Kind == ClipboardItemKind.Image && OriginalImage != null)
            {
                CurrentImage?.Dispose();
                CurrentImage = new Bitmap(OriginalImage);
            }
            else if (Kind == ClipboardItemKind.Text)
            {
                CurrentText = OriginalText ?? string.Empty;
            }

            IsDirty = false;
            UndoSnapshot = null;
            RebuildThumbnail();
        }

        public ClipboardHistoryItem CloneCurrentAsNew()
        {
            if (Kind == ClipboardItemKind.Image && CurrentImage != null)
            {
                return FromImage(CurrentImage);
            }

            return FromText(CurrentText ?? string.Empty);
        }

        public void RebuildThumbnail()
        {
            var previous = Thumbnail;
            Thumbnail = Kind == ClipboardItemKind.Image
                ? RenderImageThumbnail(CurrentImage)
                : RenderTextThumbnail(CurrentText ?? string.Empty);
            previous?.Dispose();
        }

        private static Bitmap RenderImageThumbnail(Bitmap? source)
        {
            var thumb = new Bitmap(ThumbnailSize, ThumbnailSize, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(thumb);
            g.Clear(Color.FromArgb(32, 32, 32));
            if (source == null || source.Width <= 0 || source.Height <= 0)
            {
                return thumb;
            }

            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // Fit preserving aspect ratio.
            float scale = Math.Min((float)ThumbnailSize / source.Width, (float)ThumbnailSize / source.Height);
            int w = Math.Max(1, (int)(source.Width * scale));
            int h = Math.Max(1, (int)(source.Height * scale));
            int x = (ThumbnailSize - w) / 2;
            int y = (ThumbnailSize - h) / 2;
            g.DrawImage(source, new Rectangle(x, y, w, h));
            return thumb;
        }

        private static Bitmap RenderTextThumbnail(string text)
        {
            var thumb = new Bitmap(ThumbnailSize, ThumbnailSize, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(thumb);
            g.Clear(Color.FromArgb(40, 50, 65));
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            var preview = (text ?? string.Empty).Trim();
            if (preview.Length == 0)
            {
                using var emptyFont = new Font("Segoe UI", 5.5f, FontStyle.Italic);
                TextRenderer.DrawText(g, "(empty)", emptyFont, new Rectangle(0, 0, ThumbnailSize, ThumbnailSize),
                    Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return thumb;
            }

            if (preview.Length > 30) preview = preview.Substring(0, 30);
            using var font = new Font("Segoe UI", 4.5f, FontStyle.Regular);
            TextRenderer.DrawText(
                g, preview, font,
                new Rectangle(1, 1, ThumbnailSize - 2, ThumbnailSize - 2),
                Color.White,
                TextFormatFlags.WordBreak | TextFormatFlags.Top | TextFormatFlags.Left | TextFormatFlags.NoPrefix);

            // Subtle "T" badge bottom-right so text items are recognizable at a glance.
            using var badgeFont = new Font("Segoe UI", 5.5f, FontStyle.Bold);
            TextRenderer.DrawText(g, "T", badgeFont, new Point(ThumbnailSize - 10, ThumbnailSize - 11),
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
            CurrentImage?.Dispose();
            Thumbnail?.Dispose();
            OriginalImage = null;
            CurrentImage = null;
            Thumbnail = null;
        }
    }
}
