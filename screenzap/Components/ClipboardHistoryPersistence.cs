using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.Json;

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

        public ClipboardHistoryPersistence()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            rootDirectory = Path.Combine(localAppData, "Screenzap", "ClipboardHistory", "v1");
        }

        public (List<ClipboardHistoryItem> Items, Guid? ActiveItemId) Load()
        {
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
                Annotations = item.Annotations?.Select(ToDto).ToList(),
                TextAnnotations = item.TextAnnotations?.Select(ToDto).ToList()
            };

            if (item.Kind == ClipboardItemKind.Image)
            {
                entry.OriginalImagePath = SaveImage(item.Id, "original", item.OriginalImage, keepFiles);
                entry.CommittedImagePath = SaveImage(item.Id, "committed", item.CommittedImage, keepFiles);
                entry.CurrentImagePath = SaveImage(item.Id, "current", item.CurrentImage, keepFiles);
            }
            else
            {
                entry.OriginalText = item.OriginalText ?? string.Empty;
                entry.CommittedText = item.CommittedText ?? string.Empty;
                entry.CurrentText = item.CurrentText ?? string.Empty;
            }

            return entry;
        }

        private string? SaveImage(Guid itemId, string role, Bitmap? image, HashSet<string> keepFiles)
        {
            if (image == null)
            {
                return null;
            }

            var fileName = $"{itemId:N}_{role}.png";
            var path = Path.Combine(rootDirectory, fileName);
            image.Save(path, ImageFormat.Png);
            keepFiles.Add(fileName);
            return fileName;
        }

        private ClipboardHistoryItem? LoadItem(ClipboardHistoryItemEntry entry)
        {
            if (entry.Kind == ClipboardItemKind.Image)
            {
                var original = LoadBitmap(entry.OriginalImagePath);
                var committed = LoadBitmap(entry.CommittedImagePath);
                var current = LoadBitmap(entry.CurrentImagePath);

                if (original == null || committed == null || current == null)
                {
                    original?.Dispose();
                    committed?.Dispose();
                    current?.Dispose();
                    return null;
                }

                using (original)
                using (committed)
                using (current)
                {
                    var item = ClipboardHistoryItem.FromPersistedImage(entry.Id, entry.CreatedUtc, original, committed, current);
                    ApplyCommonEntryState(item, entry);
                    return item;
                }
            }

            var textItem = ClipboardHistoryItem.FromPersistedText(
                entry.Id,
                entry.CreatedUtc,
                entry.OriginalText ?? string.Empty,
                entry.CommittedText ?? string.Empty,
                entry.CurrentText ?? string.Empty);
            ApplyCommonEntryState(textItem, entry);
            return textItem;
        }

        private void ApplyCommonEntryState(ClipboardHistoryItem item, ClipboardHistoryItemEntry entry)
        {
            item.IsSeededFallback = entry.IsSeededFallback;
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

        private Bitmap? LoadBitmap(string? relativePath)
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

            using var loaded = new Bitmap(path);
            return new Bitmap(loaded);
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
            public string? SystemHistoryId { get; set; }
            public List<string>? SuppressedSystemHistoryIds { get; set; }
            public string? OriginalImagePath { get; set; }
            public string? CommittedImagePath { get; set; }
            public string? CurrentImagePath { get; set; }
            public string? OriginalText { get; set; }
            public string? CommittedText { get; set; }
            public string? CurrentText { get; set; }
            public List<AnnotationShapeDto>? Annotations { get; set; }
            public List<TextAnnotationDto>? TextAnnotations { get; set; }
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
