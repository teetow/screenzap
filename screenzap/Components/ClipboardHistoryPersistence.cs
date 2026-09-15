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

        // Maps a newly-saved image file to the exact PNG byte[] written to it. In-memory content is
        // replaced with a new array whenever it changes, making reference identity a reliable
        // unchanged signal. Restored content takes the even cheaper file-backed path and needs no
        // byte read at all.
        private readonly Dictionary<string, byte[]> lastSavedBytesByFile = new(StringComparer.OrdinalIgnoreCase);

        // Same unchanged-content trick for layer sources, keyed on the Bitmap instance. A layer's
        // Source is never mutated — transforms are sidecars — so reference identity means the
        // file on disk is still current and the PNG encode can be skipped entirely.
        private readonly Dictionary<string, Bitmap> lastSavedLayerBitmapByFile = new(StringComparer.OrdinalIgnoreCase);

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

                foreach (var stale in lastSavedLayerBitmapByFile.Keys.Where(k => !keepFiles.Contains(k)).ToList())
                {
                    lastSavedLayerBitmapByFile.Remove(stale);
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

            // Floating pastes are part of the item's state, not a scratch overlay: without this
            // an item with a paste in flight came back after a restart as a bare canvas.
            entry.ImageLayers = item.ImageLayers
                ?.Select(layer => ToDto(item.Id, layer, keepFiles))
                .ToList();

            entry.OriginalImagePath = SaveImageContent(item.Id, "original", item.OriginalPngContent, keepFiles);
            entry.CommittedImagePath = SaveImageContent(item.Id, "committed", item.CommittedPngContent, keepFiles);
            entry.CurrentImagePath = SaveImageContent(item.Id, "current", item.CurrentPngContent, keepFiles);

            // Size + signature per role and a small thumbnail file let Load rebuild the item
            // without decoding any full image.
            entry.OriginalImageMeta = ToMetaDto(item.OriginalImageMeta);
            entry.CommittedImageMeta = ToMetaDto(item.CommittedImageMeta);
            entry.CurrentImageMeta = ToMetaDto(item.CurrentImageMeta);
            entry.ThumbnailImagePath = SaveImageContent(item.Id, "thumb", item.ThumbnailSourcePngContent, keepFiles);

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

        private string? SaveImageContent(Guid itemId, string role, PngContent? content, HashSet<string> keepFiles)
        {
            if (content == null)
            {
                return null;
            }

            var fileName = $"{itemId:N}_{role}.png";
            var path = Path.Combine(rootDirectory, fileName);

            // The normal restored-history path is already backed by this exact file. Keep it in
            // place without reading its bytes and without rewriting hundreds of unchanged PNGs.
            if (PathsEqual(content.BackingFilePath, path) && File.Exists(path))
            {
                keepFiles.Add(fileName);
                return fileName;
            }

            var png = content.GetBytes();

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

        private static bool PathsEqual(string? first, string second)
        {
            if (string.IsNullOrWhiteSpace(first))
            {
                return false;
            }

            return string.Equals(
                Path.GetFullPath(first),
                Path.GetFullPath(second),
                StringComparison.OrdinalIgnoreCase);
        }

        private ClipboardHistoryItem? LoadItem(ClipboardHistoryItemEntry entry)
        {
            if (entry.Kind != ClipboardItemKind.Image)
            {
                // Legacy text entry written by an older build. Screenzap is image-only now, so skip
                // it gracefully — the next save drops it from the manifest.
                return null;
            }

            var originalPath = ResolveExistingPath(entry.OriginalImagePath);
            var committedPath = ResolveExistingPath(entry.CommittedImagePath);
            var currentPath = ResolveExistingPath(entry.CurrentImagePath);

            if (originalPath == null || committedPath == null || currentPath == null)
            {
                return null;
            }

            var originalMeta = FromMetaDto(entry.OriginalImageMeta);
            var committedMeta = FromMetaDto(entry.CommittedImageMeta);
            var currentMeta = FromMetaDto(entry.CurrentImageMeta);
            var thumbnailPath = ResolveExistingPath(entry.ThumbnailImagePath);

            ClipboardHistoryItem item;
            if (originalMeta.HasValue && committedMeta.HasValue && currentMeta.HasValue)
            {
                // Current manifests carry enough metadata to rebuild every item as a tiny set of
                // file-backed descriptors. No full PNG or thumbnail is read on the startup path.
                item = ClipboardHistoryItem.FromPersistedFiles(
                    entry.Id,
                    entry.CreatedUtc,
                    originalPath,
                    committedPath,
                    currentPath,
                    originalMeta.Value,
                    committedMeta.Value,
                    currentMeta.Value,
                    thumbnailPath);
            }
            else
            {
                // One-time compatibility path for older manifests. Their first save upgrades the
                // metadata, after which subsequent starts use the lazy path above.
                item = ClipboardHistoryItem.FromPersistedPng(
                    entry.Id,
                    entry.CreatedUtc,
                    File.ReadAllBytes(originalPath),
                    File.ReadAllBytes(committedPath),
                    File.ReadAllBytes(currentPath),
                    originalMeta,
                    committedMeta,
                    currentMeta,
                    thumbnailPath == null ? null : File.ReadAllBytes(thumbnailPath));
            }

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
            item.ImageLayers = LoadImageLayers(entry.ImageLayers);
            item.SetDirtyFlagForRestore(entry.IsDirty);
        }

        private string? ResolveExistingPath(string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return null;
            }

            var path = Path.GetFullPath(Path.Combine(rootDirectory, relativePath));
            return File.Exists(path) ? path : null;
        }

        private ImageLayerDto ToDto(Guid itemId, ImageLayer layer, HashSet<string> keepFiles)
        {
            return new ImageLayerDto
            {
                Id = layer.Id,
                Name = layer.Name,
                IsVisible = layer.IsVisible,
                RotationDeg = layer.RotationDeg,
                FrameX = layer.Frame.X,
                FrameY = layer.Frame.Y,
                FrameWidth = layer.Frame.Width,
                FrameHeight = layer.Frame.Height,
                FillX = layer.Fill.X,
                FillY = layer.Fill.Y,
                FillWidth = layer.Fill.Width,
                FillHeight = layer.Fill.Height,
                SourceImagePath = SaveLayerBitmap(itemId, $"layer_{layer.Id:N}", layer.Source, keepFiles),
                MaskImagePath = layer.Mask == null
                    ? null
                    : SaveLayerBitmap(itemId, $"layer_{layer.Id:N}_mask", layer.Mask, keepFiles),
            };
        }

        private string? SaveLayerBitmap(Guid itemId, string role, Bitmap bitmap, HashSet<string> keepFiles)
        {
            var fileName = $"{itemId:N}_{role}.png";
            var path = Path.Combine(rootDirectory, fileName);

            if (lastSavedLayerBitmapByFile.TryGetValue(fileName, out var previous)
                && ReferenceEquals(previous, bitmap)
                && File.Exists(path))
            {
                keepFiles.Add(fileName);
                return fileName;
            }

            using var perf = PerfTrace.Scope(
                "ClipboardHistoryPersistence.SaveLayerImage",
                () => $"role={role} size={bitmap.Width}x{bitmap.Height}",
                slowMs: 30,
                summaryEvery: 50);

            try
            {
                using var buffer = new MemoryStream();
                bitmap.Save(buffer, ImageFormat.Png);
                File.WriteAllBytes(path, buffer.ToArray());
            }
            catch (Exception ex)
            {
                // A layer we cannot encode is dropped from the manifest rather than taking the
                // whole save down with it.
                Logger.Log($"ClipboardHistoryPersistence: layer {role} failed to save: {ex.Message}");
                return null;
            }

            lastSavedLayerBitmapByFile[fileName] = bitmap;
            keepFiles.Add(fileName);
            return fileName;
        }

        private List<ImageLayer>? LoadImageLayers(List<ImageLayerDto>? dtos)
        {
            if (dtos == null || dtos.Count == 0)
            {
                return null;
            }

            var layers = new List<ImageLayer>();
            foreach (var dto in dtos)
            {
                var sourcePath = ResolveExistingPath(dto.SourceImagePath);
                if (sourcePath == null)
                {
                    // No pixels, no layer. Better a missing paste than an empty frame with
                    // grips the user cannot fill.
                    continue;
                }

                Bitmap source;
                try
                {
                    source = LoadStandaloneBitmap(sourcePath);
                }
                catch (Exception ex)
                {
                    Logger.Log($"ClipboardHistoryPersistence: layer source {dto.SourceImagePath} failed to load: {ex.Message}");
                    continue;
                }

                Bitmap? mask = null;
                var maskPath = ResolveExistingPath(dto.MaskImagePath);
                if (maskPath != null)
                {
                    try
                    {
                        mask = LoadStandaloneBitmap(maskPath);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"ClipboardHistoryPersistence: layer mask {dto.MaskImagePath} failed to load: {ex.Message}");
                    }
                }

                layers.Add(new ImageLayer(
                    source,
                    new RectangleF(dto.FrameX, dto.FrameY, dto.FrameWidth, dto.FrameHeight),
                    new RectangleF(dto.FillX, dto.FillY, dto.FillWidth, dto.FillHeight),
                    dto.RotationDeg,
                    mask)
                {
                    Id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id,
                    IsVisible = dto.IsVisible,
                    Name = string.IsNullOrWhiteSpace(dto.Name) ? "Paste" : dto.Name,
                });
            }

            return layers.Count > 0 ? layers : null;
        }

        /// <summary>
        /// Decode a PNG into a bitmap that owns its own pixels. Constructing a Bitmap over a file
        /// or a stream leaves it tied to that source for life — the copy is what makes it safe to
        /// hold, draw and later re-encode.
        /// </summary>
        private static Bitmap LoadStandaloneBitmap(string path)
        {
            var bytes = File.ReadAllBytes(path);
            using var buffer = new MemoryStream(bytes, writable: false);
            using var decoded = new Bitmap(buffer);
            return new Bitmap(decoded);
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
            public List<ImageLayerDto>? ImageLayers { get; set; }
        }

        private sealed class ImageLayerDto
        {
            public Guid Id { get; set; }
            public string? Name { get; set; }
            public bool IsVisible { get; set; } = true;
            public float RotationDeg { get; set; }
            public float FrameX { get; set; }
            public float FrameY { get; set; }
            public float FrameWidth { get; set; }
            public float FrameHeight { get; set; }
            public float FillX { get; set; }
            public float FillY { get; set; }
            public float FillWidth { get; set; }
            public float FillHeight { get; set; }
            public string? SourceImagePath { get; set; }
            public string? MaskImagePath { get; set; }
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
