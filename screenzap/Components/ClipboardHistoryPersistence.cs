using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.Json;
using screenzap.lib;

namespace screenzap.Components
{
    internal sealed class ClipboardHistoryPersistence
    {
        private const string ManifestFileName = "manifest.json";
        private readonly string rootDirectory;
        private readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false
        };

        // Maps a saved image file name to the exact PNG byte[] last written to it. An item's role
        // blobs are replaced with brand-new arrays whenever their content changes, so reference
        // identity is a reliable "unchanged since last save" signal. This lets Save() skip rewriting
        // the up-to-384 files that didn't change — O(changed images) instead of O(all images) when
        // the history is full. Items now hold PNG-compressed bytes, so saving is a straight byte
        // copy with no encode and no decode.
        private readonly Dictionary<string, byte[]> lastSavedBytesByFile = new(StringComparer.OrdinalIgnoreCase);

        public ClipboardHistoryPersistence()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            rootDirectory = Path.Combine(localAppData, "Screenzap", "ClipboardHistory", "v1");
        }

        internal ClipboardHistoryPersistence(string persistenceDirectory)
        {
            rootDirectory = persistenceDirectory;
        }

        public (List<ClipboardHistoryItem> Items, Guid? ActiveItemId) Load()
        {
            using var perf = PerfTrace.Scope(
                "ClipboardHistoryPersistence.Load",
                slowMs: 200);

            try
            {
                var manifestPath = Path.Combine(rootDirectory, ManifestFileName);
                if (!File.Exists(manifestPath))
                {
                    return (new List<ClipboardHistoryItem>(), null);
                }

                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<ClipboardHistoryManifest>(json, jsonOptions);
                if (manifest == null)
                {
                    return (new List<ClipboardHistoryItem>(), null);
                }

                var items = new List<ClipboardHistoryItem>();
                foreach (var entry in manifest.Items)
                {
                    try
                    {
                        var item = LoadItem(entry);
                        if (item != null)
                        {
                            items.Add(item);
                        }
                    }
                    catch
                    {
                        // Skip malformed entries rather than failing the entire restore.
                    }
                }

                return (items, manifest.ActiveItemId);
            }
            catch
            {
                return (new List<ClipboardHistoryItem>(), null);
            }
        }

        public void Save(IReadOnlyList<ClipboardHistoryItem> items, ClipboardHistoryItem? activeItem)
        {
            using var perf = PerfTrace.Scope(
                "ClipboardHistoryPersistence.Save",
                () => $"items={items.Count} active={(activeItem != null)}",
                slowMs: 80);

            try
            {
                Directory.CreateDirectory(rootDirectory);

                var keepFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var entries = new List<ClipboardHistoryItemEntry>();
                foreach (var item in items)
                {
                    var entry = BuildEntry(item, keepFiles);
                    entries.Add(entry);
                }

                var manifest = new ClipboardHistoryManifest
                {
                    ActiveItemId = activeItem?.Id,
                    Items = entries
                };

                var manifestPath = Path.Combine(rootDirectory, ManifestFileName);
                var tmpPath = manifestPath + ".tmp";
                var json = JsonSerializer.Serialize(manifest, jsonOptions);
                File.WriteAllText(tmpPath, json);
                File.Copy(tmpPath, manifestPath, overwrite: true);
                File.Delete(tmpPath);

                foreach (var file in Directory.GetFiles(rootDirectory, "*.png", SearchOption.TopDirectoryOnly))
                {
                    if (!keepFiles.Contains(Path.GetFileName(file)))
                    {
                        File.Delete(file);
                    }
                }

                // Drop cache entries for files no longer referenced so the dictionary doesn't pin
                // byte arrays for removed items.
                if (lastSavedBytesByFile.Count > keepFiles.Count)
                {
                    foreach (var stale in lastSavedBytesByFile.Keys.Where(k => !keepFiles.Contains(k)).ToList())
                    {
                        lastSavedBytesByFile.Remove(stale);
                    }
                }
            }
            catch
            {
                // Persistence must not break editing workflow.
            }
        }

        public void SaveActiveItemOnly(Guid? activeItemId)
        {
            using var perf = PerfTrace.Scope(
                "ClipboardHistoryPersistence.SaveActiveItemOnly",
                () => $"active={(activeItemId.HasValue ? activeItemId.Value.ToString("N") : "none")}",
                slowMs: 30,
                summaryEvery: 50);

            try
            {
                Directory.CreateDirectory(rootDirectory);
                var manifestPath = Path.Combine(rootDirectory, ManifestFileName);
                ClipboardHistoryManifest manifest;

                if (File.Exists(manifestPath))
                {
                    var json = File.ReadAllText(manifestPath);
                    manifest = JsonSerializer.Deserialize<ClipboardHistoryManifest>(json, jsonOptions) ?? new ClipboardHistoryManifest();
                }
                else
                {
                    manifest = new ClipboardHistoryManifest();
                }

                manifest.ActiveItemId = activeItemId;

                var tmpPath = manifestPath + ".tmp";
                var updatedJson = JsonSerializer.Serialize(manifest, jsonOptions);
                File.WriteAllText(tmpPath, updatedJson);
                File.Copy(tmpPath, manifestPath, overwrite: true);
                File.Delete(tmpPath);
            }
            catch
            {
                // Persistence must not break editing workflow.
            }
        }

        private ClipboardHistoryItemEntry BuildEntry(ClipboardHistoryItem item, HashSet<string> keepFiles)
        {
            var entry = new ClipboardHistoryItemEntry
            {
                Id = item.Id,
                Kind = item.Kind,
                CreatedUtc = item.CreatedUtc,
                IsDirty = item.IsDirty,
                SystemHistoryId = item.SystemHistoryId,
                SuppressedSystemHistoryIds = item.SuppressedSystemHistoryIds.ToList(),
                IsSeededFallback = item.IsSeededFallback,
                IsUserDuplicate = item.IsUserDuplicate,
                Annotations = item.Annotations?.Select(ToDto).ToList(),
                TextAnnotations = item.TextAnnotations?.Select(ToDto).ToList()
            };

            entry.OriginalImagePath = SaveImageBytes(item.Id, "original", item.OriginalPngBytes, keepFiles);
            entry.CommittedImagePath = SaveImageBytes(item.Id, "committed", item.CommittedPngBytes, keepFiles);
            entry.CurrentImagePath = SaveImageBytes(item.Id, "current", item.CurrentPngBytes, keepFiles);

            // Size + signature per role and a small thumbnail file let Load rebuild the item
            // without decoding any full image.
            entry.OriginalImageMeta = ToMetaDto(item.OriginalImageMeta);
            entry.CommittedImageMeta = ToMetaDto(item.CommittedImageMeta);
            entry.CurrentImageMeta = ToMetaDto(item.CurrentImageMeta);
            entry.ThumbnailImagePath = SaveImageBytes(item.Id, "thumb", item.ThumbnailSourcePngBytes, keepFiles);

            return entry;
        }

        private static ImageRoleMetaDto? ToMetaDto(RoleImageMeta? meta)
        {
            if (meta == null)
            {
                return null;
            }

            return new ImageRoleMetaDto
            {
                Width = meta.Value.PixelSize.Width,
                Height = meta.Value.PixelSize.Height,
                Signature = Convert.ToBase64String(meta.Value.Signature)
            };
        }

        private static RoleImageMeta? FromMetaDto(ImageRoleMetaDto? dto)
        {
            if (dto == null || dto.Width <= 0 || dto.Height <= 0 || string.IsNullOrEmpty(dto.Signature))
            {
                return null;
            }

            byte[] signature;
            try
            {
                signature = Convert.FromBase64String(dto.Signature);
            }
            catch (FormatException)
            {
                return null;
            }

            if (signature.Length != RoleImageMeta.SignatureByteLength)
            {
                return null;
            }

            return new RoleImageMeta(new Size(dto.Width, dto.Height), signature);
        }

        private string? SaveImageBytes(Guid itemId, string role, byte[]? png, HashSet<string> keepFiles)
        {
            if (png == null)
            {
                return null;
            }

            var fileName = $"{itemId:N}_{role}.png";
            var path = Path.Combine(rootDirectory, fileName);

            // Skip rewriting when this exact byte array was already written to this file and the file
            // still exists. Content can only change by swapping in a new array, so reference identity
            // guarantees the on-disk copy is current. The bytes are already PNG-compressed by the
            // item, so a write is a straight copy — no encode.
            if (lastSavedBytesByFile.TryGetValue(fileName, out var previouslySaved)
                && ReferenceEquals(previouslySaved, png)
                && File.Exists(path))
            {
                keepFiles.Add(fileName);
                return fileName;
            }

            using var perf = PerfTrace.Scope(
                "ClipboardHistoryPersistence.SaveImage",
                () => $"role={role} bytes={png.Length}",
                slowMs: 30,
                summaryEvery: 50);

            File.WriteAllBytes(path, png);
            lastSavedBytesByFile[fileName] = png;
            keepFiles.Add(fileName);
            return fileName;
        }

        private ClipboardHistoryItem? LoadItem(ClipboardHistoryItemEntry entry)
        {
            if (entry.Kind != ClipboardItemKind.Image)
            {
                // Legacy text entry written by an older build. Screenzap is image-only now, so skip
                // it gracefully — the next save drops it from the manifest.
                return null;
            }

            var original = LoadBytes(entry.OriginalImagePath);
            var committed = LoadBytes(entry.CommittedImagePath);
            var current = LoadBytes(entry.CurrentImagePath);

            if (original == null || committed == null || current == null)
            {
                return null;
            }

            var item = ClipboardHistoryItem.FromPersistedPng(
                entry.Id,
                entry.CreatedUtc,
                original,
                committed,
                current,
                FromMetaDto(entry.OriginalImageMeta),
                FromMetaDto(entry.CommittedImageMeta),
                FromMetaDto(entry.CurrentImageMeta),
                LoadBytes(entry.ThumbnailImagePath));
            ApplyCommonEntryState(item, entry);
            return item;
        }

        private void ApplyCommonEntryState(ClipboardHistoryItem item, ClipboardHistoryItemEntry entry)
        {
            item.IsSeededFallback = entry.IsSeededFallback;
            item.IsUserDuplicate = entry.IsUserDuplicate;
            item.AssignSystemHistoryId(entry.SystemHistoryId);

            if (entry.SuppressedSystemHistoryIds != null)
            {
                foreach (var id in entry.SuppressedSystemHistoryIds)
                {
                    item.AddSuppressedSystemHistoryId(id);
                }
            }

            item.Annotations = entry.Annotations?.Select(FromDto).ToList();
            item.TextAnnotations = entry.TextAnnotations?.Select(FromDto).ToList();
            item.SetDirtyFlagForRestore(entry.IsDirty);
        }

        private byte[]? LoadBytes(string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return null;
            }

            var path = Path.Combine(rootDirectory, relativePath);
            if (!File.Exists(path))
            {
                return null;
            }

            return File.ReadAllBytes(path);
        }

        private static AnnotationShapeDto ToDto(AnnotationShape shape)
        {
            return new AnnotationShapeDto
            {
                Id = shape.Id,
                Type = (int)shape.Type,
                StartX = shape.Start.X,
                StartY = shape.Start.Y,
                EndX = shape.End.X,
                EndY = shape.End.Y,
                LineThickness = shape.LineThickness,
                ArrowSize = shape.ArrowSize,
                Selected = shape.Selected
            };
        }

        private static AnnotationShape FromDto(AnnotationShapeDto shape)
        {
            return new AnnotationShape
            {
                Id = shape.Id,
                Type = (AnnotationType)shape.Type,
                Start = new Point(shape.StartX, shape.StartY),
                End = new Point(shape.EndX, shape.EndY),
                LineThickness = shape.LineThickness,
                ArrowSize = shape.ArrowSize,
                Selected = shape.Selected
            };
        }

        private static TextAnnotationDto ToDto(TextAnnotation text)
        {
            return new TextAnnotationDto
            {
                Id = text.Id,
                PositionX = text.Position.X,
                PositionY = text.Position.Y,
                Text = text.Text,
                FontFamily = text.FontFamily,
                FontSize = text.FontSize,
                FontStyle = (int)text.FontStyle,
                TextColorArgb = text.TextColor.ToArgb(),
                OutlineThickness = text.OutlineThickness,
                OutlineColorArgb = text.OutlineColor.ToArgb(),
                Selected = text.Selected,
                IsEditing = text.IsEditing,
                CaretPosition = text.CaretPosition,
                SelectionAnchor = text.SelectionAnchor
            };
        }

        private static TextAnnotation FromDto(TextAnnotationDto text)
        {
            return new TextAnnotation
            {
                Id = text.Id,
                Position = new Point(text.PositionX, text.PositionY),
                Text = text.Text ?? string.Empty,
                FontFamily = text.FontFamily ?? "Segoe UI",
                FontSize = text.FontSize,
                FontStyle = (FontStyle)text.FontStyle,
                TextColor = Color.FromArgb(text.TextColorArgb),
                OutlineThickness = text.OutlineThickness,
                OutlineColor = Color.FromArgb(text.OutlineColorArgb),
                Selected = text.Selected,
                IsEditing = text.IsEditing,
                CaretPosition = text.CaretPosition,
                SelectionAnchor = text.SelectionAnchor
            };
        }

        private sealed class ClipboardHistoryManifest
        {
            public Guid? ActiveItemId { get; set; }
            public List<ClipboardHistoryItemEntry> Items { get; set; } = new List<ClipboardHistoryItemEntry>();
        }

        private sealed class ClipboardHistoryItemEntry
        {
            public Guid Id { get; set; }
            public ClipboardItemKind Kind { get; set; }
            public DateTime CreatedUtc { get; set; }
            public bool IsDirty { get; set; }
            public bool IsSeededFallback { get; set; }
            public bool IsUserDuplicate { get; set; }
            public string? SystemHistoryId { get; set; }
            public List<string>? SuppressedSystemHistoryIds { get; set; }
            public string? OriginalImagePath { get; set; }
            public string? CommittedImagePath { get; set; }
            public string? CurrentImagePath { get; set; }
            public ImageRoleMetaDto? OriginalImageMeta { get; set; }
            public ImageRoleMetaDto? CommittedImageMeta { get; set; }
            public ImageRoleMetaDto? CurrentImageMeta { get; set; }
            public string? ThumbnailImagePath { get; set; }
            public List<AnnotationShapeDto>? Annotations { get; set; }
            public List<TextAnnotationDto>? TextAnnotations { get; set; }
        }

        private sealed class ImageRoleMetaDto
        {
            public int Width { get; set; }
            public int Height { get; set; }
            public string? Signature { get; set; }
        }

        private sealed class AnnotationShapeDto
        {
            public Guid Id { get; set; }
            public int Type { get; set; }
            public int StartX { get; set; }
            public int StartY { get; set; }
            public int EndX { get; set; }
            public int EndY { get; set; }
            public float LineThickness { get; set; }
            public float ArrowSize { get; set; }
            public bool Selected { get; set; }
        }

        private sealed class TextAnnotationDto
        {
            public Guid Id { get; set; }
            public int PositionX { get; set; }
            public int PositionY { get; set; }
            public string? Text { get; set; }
            public string? FontFamily { get; set; }
            public float FontSize { get; set; }
            public int FontStyle { get; set; }
            public int TextColorArgb { get; set; }
            public float OutlineThickness { get; set; }
            public int OutlineColorArgb { get; set; }
            public bool Selected { get; set; }
            public bool IsEditing { get; set; }
            public int CaretPosition { get; set; }
            public int? SelectionAnchor { get; set; }
        }
    }
}
