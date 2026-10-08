using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using screenzap;
using screenzap.Components;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests
{
    /// <summary>
    /// A floating paste is part of an item's state, not a scratch overlay — it has to survive the
    /// trip through the manifest and come back the way it went in.
    /// </summary>
    public class ImageLayerPersistenceTests
    {
        private static Bitmap SolidBitmap(int width, int height, Color color)
        {
            var bitmap = new Bitmap(width, height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(color);
            return bitmap;
        }

        private static void WithTempRoot(Action<string> body)
        {
            var root = Path.Combine(Path.GetTempPath(), "screenzap-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                body(root);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
                catch
                {
                    // A leftover temp directory must not fail the test.
                }
            }
        }

        [Fact]
        public void SaveThenLoad_RestoresFloatingPastes_WithPlacementAndState()
        {
            WithTempRoot(root =>
            {
                ClipboardHistoryItem? item = null;
                try
                {
                    using (var canvas = SolidBitmap(60, 40, Color.White))
                    {
                        item = ClipboardHistoryItem.FromImage(canvas);
                    }

                    item.Overlay = new DocumentOverlay
                    {
                        Layers = new List<ImageLayer>
                        {
                            new ImageLayer(SolidBitmap(8, 6, Color.Lime), new RectangleF(3f, 4f, 8f, 6f))
                            {
                                Name = "Paste 1",
                            },
                            new ImageLayer(
                                SolidBitmap(10, 10, Color.Magenta),
                                new RectangleF(20.5f, 11.25f, 30f, 12f),
                                new RectangleF(2f, 3f, 6f, 7f),
                                37.5f,
                                null)
                            {
                                Name = "Paste 2",
                                IsVisible = false,
                            },
                        },
                    };

                    new ClipboardHistoryPersistence(root).Save(new[] { item }, item);

                    var restored = new ClipboardHistoryPersistence(root).Load();
                    try
                    {
                        var reloaded = Assert.Single(restored.Items);
                        Assert.NotNull(reloaded.Overlay);
                        var layers = reloaded.Overlay!.Layers;
                        Assert.Equal(2, layers.Count);

                        // Stack order is content: the second paste has to come back on top.
                        Assert.Equal(new[] { "Paste 1", "Paste 2" }, layers.Select(l => l.Name).ToArray());

                        Assert.Equal(new RectangleF(3f, 4f, 8f, 6f), layers[0].Frame);
                        Assert.True(layers[0].IsVisible);
                        Assert.Equal(Color.Lime.ToArgb(), layers[0].Source.GetPixel(4, 3).ToArgb());

                        // Sub-pixel placement, crop and rotation all round-trip, not just the box.
                        Assert.Equal(new RectangleF(20.5f, 11.25f, 30f, 12f), layers[1].Frame);
                        Assert.Equal(new RectangleF(2f, 3f, 6f, 7f), layers[1].Fill);
                        Assert.Equal(37.5f, layers[1].RotationDeg);
                        Assert.False(layers[1].IsVisible);
                        Assert.Equal(Color.Magenta.ToArgb(), layers[1].Source.GetPixel(5, 5).ToArgb());
                    }
                    finally
                    {
                        foreach (var restoredItem in restored.Items)
                        {
                            restoredItem.Dispose();
                        }
                    }
                }
                finally
                {
                    item?.Dispose();
                }
            });
        }

        [Fact]
        public void SaveThenLoad_ItemWithoutLayers_StaysWithoutLayers()
        {
            WithTempRoot(root =>
            {
                ClipboardHistoryItem? item = null;
                try
                {
                    using (var canvas = SolidBitmap(20, 20, Color.White))
                    {
                        item = ClipboardHistoryItem.FromImage(canvas);
                    }

                    new ClipboardHistoryPersistence(root).Save(new[] { item }, item);

                    var restored = new ClipboardHistoryPersistence(root).Load();
                    try
                    {
                        Assert.Null(Assert.Single(restored.Items).Overlay);
                    }
                    finally
                    {
                        foreach (var restoredItem in restored.Items)
                        {
                            restoredItem.Dispose();
                        }
                    }
                }
                finally
                {
                    item?.Dispose();
                }
            });
        }

        [Fact]
        public void Save_DropsLayerFilesOnceTheLayersAreGone()
        {
            WithTempRoot(root =>
            {
                ClipboardHistoryItem? item = null;
                try
                {
                    using (var canvas = SolidBitmap(20, 20, Color.White))
                    {
                        item = ClipboardHistoryItem.FromImage(canvas);
                    }

                    var persistence = new ClipboardHistoryPersistence(root);
                    item.Overlay = new DocumentOverlay
                    {
                        Layers = new List<ImageLayer>
                        {
                            new ImageLayer(SolidBitmap(8, 8, Color.Lime), new RectangleF(0f, 0f, 8f, 8f)),
                        },
                    };
                    persistence.Save(new[] { item }, item);
                    Assert.NotEmpty(Directory.GetFiles(root, "*_layer_*.png"));

                    // Gluing the paste down clears the layers; the orphaned PNG must be swept up
                    // with everything else the manifest no longer references.
                    item.Overlay = null;
                    persistence.Save(new[] { item }, item);
                    Assert.Empty(Directory.GetFiles(root, "*_layer_*.png"));
                }
                finally
                {
                    item?.Dispose();
                }
            });
        }

        [Fact]
        public void ClosingTheHost_WritesAFloatingPasteToDisk_AndTheNextStartRestoresIt()
        {
            WithTempRoot(root =>
            {
                // The whole reported bug end to end: paste onto a canvas, quit, come back.
                var persistence = new ClipboardHistoryPersistence(root);

                StaTest.Run(() =>
                {
                    using var editor = new screenzap.ImageDocumentEditor();
                    using var host = new ClipboardDocumentHost(
                        new IClipboardDocumentPresenter[] { editor },
                        persistence,
                        restorePersistedHistory: false,
                        persistHistoryChanges: true,
                        allowSystemClipboardWrites: false);

                    host.HistoryStore.ReplaceAll(Array.Empty<ClipboardHistoryItem>());
                    ClipboardHistoryItem seeded;
                    using (var canvas = SolidBitmap(60, 40, Color.White))
                    {
                        seeded = host.HistoryStore.AddObservedImage(canvas);
                    }

                    Application.DoEvents();

                    // Activate through the host so the presenter loads and host services attach.
                    Assert.True(host.ActivateHistoryItem(seeded));
                    Application.DoEvents();

                    using (var pasted = SolidBitmap(8, 8, Color.Lime))
                    {
                        editor.SetInternalClipboardImageForDiagnostics(pasted);
                        Assert.True(editor.PasteFromClipboardForDiagnostics());
                    }

                    editor.SetSelectedLayerXForTests(5f);
                    editor.SetSelectedLayerYForTests(7f);
                    Assert.Equal(1, editor.ImageLayerCountForTests);

                    host.Dispose();
                });

                var restored = new ClipboardHistoryPersistence(root).Load();
                try
                {
                    var item = Assert.Single(restored.Items);
                    var layers = item.Overlay?.Layers;
                    Assert.NotNull(layers);
                    var layer = Assert.Single(layers!);
                    Assert.Equal(new RectangleF(5f, 7f, 8f, 8f), layer.Frame);
                    Assert.Equal(Color.Lime.ToArgb(), layer.Source.GetPixel(4, 4).ToArgb());

                    // The base must still be the unflattened canvas — a restored paste that was
                    // also burned into the background would show up twice.
                    using var baseImage = item.CurrentImage!;
                    Assert.Equal(Color.White.ToArgb(), baseImage.GetPixel(6, 8).ToArgb());
                }
                finally
                {
                    foreach (var restoredItem in restored.Items)
                    {
                        restoredItem.Dispose();
                    }
                }
            });
        }

        [Fact]
        public void DebouncedSave_PutsAFloatingPasteOnDisk_WithoutAnyCleanShutdown()
        {
            WithTempRoot(root =>
            {
                // The crash case: nothing closes, nothing deactivates, the process just stops
                // existing. Whatever the autosave tick wrote is all there is.
                var persistence = new ClipboardHistoryPersistence(root);

                StaTest.Run(() =>
                {
                    using var editor = new screenzap.ImageDocumentEditor();
                    using var host = new ClipboardDocumentHost(
                        new IClipboardDocumentPresenter[] { editor },
                        persistence,
                        restorePersistedHistory: false,
                        persistHistoryChanges: true,
                        allowSystemClipboardWrites: false);

                    host.HistoryStore.ReplaceAll(Array.Empty<ClipboardHistoryItem>());

                    ClipboardHistoryItem seeded;
                    using (var canvas = SolidBitmap(60, 40, Color.White))
                    {
                        seeded = host.HistoryStore.AddObservedImage(canvas);
                    }

                    Application.DoEvents();
                    Assert.True(host.ActivateHistoryItem(seeded));
                    Application.DoEvents();

                    using (var pasted = SolidBitmap(8, 8, Color.Lime))
                    {
                        editor.SetInternalClipboardImageForDiagnostics(pasted);
                        Assert.True(editor.PasteFromClipboardForDiagnostics());
                    }
                    editor.SetSelectedLayerXForTests(5f);
                    editor.SetSelectedLayerYForTests(7f);
                    Application.DoEvents();

                    Assert.True(host.HasUncapturedEditsForTests);
                    host.TriggerPersistedHistorySaveForTests();
                    Assert.False(host.HasUncapturedEditsForTests);

                    // Undo history is a move, not a copy — capturing must not have taken it.
                    var presenter = (IClipboardDocumentPresenter)editor;
                    Assert.True(presenter.CanExecute(EditorCommandId.Undo));

                    // Read the manifest back while the editor is still live and untouched: no
                    // close, no deactivate, no stash.
                    var restored = new ClipboardHistoryPersistence(root).Load();
                    try
                    {
                        var item = Assert.Single(restored.Items);
                        var layer = Assert.Single(item.Overlay!.Layers);
                        Assert.Equal(new RectangleF(5f, 7f, 8f, 8f), layer.Frame);
                        Assert.Equal(Color.Lime.ToArgb(), layer.Source.GetPixel(4, 4).ToArgb());
                    }
                    finally
                    {
                        foreach (var restoredItem in restored.Items)
                        {
                            restoredItem.Dispose();
                        }
                    }
                });
            });
        }

        [Fact]
        public void Save_DoesNotRecaptureWhenNothingWasEdited()
        {
            WithTempRoot(root =>
            {
                StaTest.Run(() =>
                {
                    // An idle session must not re-encode and rewrite the base image every tick.
                    var persistence = new ClipboardHistoryPersistence(root);
                    using var editor = new screenzap.ImageDocumentEditor();
                    using var host = new ClipboardDocumentHost(
                        new IClipboardDocumentPresenter[] { editor },
                        persistence,
                        restorePersistedHistory: false,
                        persistHistoryChanges: true,
                        allowSystemClipboardWrites: false);

                    host.HistoryStore.ReplaceAll(Array.Empty<ClipboardHistoryItem>());

                    ClipboardHistoryItem seeded;
                    using (var canvas = SolidBitmap(60, 40, Color.White))
                    {
                        seeded = host.HistoryStore.AddObservedImage(canvas);
                    }

                    Application.DoEvents();
                    Assert.True(host.ActivateHistoryItem(seeded));
                    Application.DoEvents();

                    host.TriggerPersistedHistorySaveForTests();
                    Assert.False(host.HasUncapturedEditsForTests);

                    var currentBefore = seeded.CurrentPngContentForTests;
                    host.TriggerPersistedHistorySaveForTests();

                    // Nothing edited, so the item's content was not re-encoded.
                    Assert.Same(currentBefore, seeded.CurrentPngContentForTests);
                });
            });
        }

        [Fact]
        public void Capture_RefreshesTheThumbnail_WithoutSchedulingAnotherSave()
        {
            WithTempRoot(root =>
            {
                StaTest.Run(() =>
                {
                    // The preview composite moved off the per-edit path and onto the capture.
                    // It still has to reach the panel, and the signal that carries it must not
                    // be the one that means "persist me" — that would schedule a save from
                    // inside the save that produced it.
                    using var editor = new screenzap.ImageDocumentEditor();
                    using var host = new ClipboardDocumentHost(
                        new IClipboardDocumentPresenter[] { editor },
                        new ClipboardHistoryPersistence(root),
                        restorePersistedHistory: false,
                        persistHistoryChanges: true,
                        allowSystemClipboardWrites: false);

                    host.HistoryStore.ReplaceAll(Array.Empty<ClipboardHistoryItem>());

                    ClipboardHistoryItem seeded;
                    using (var canvas = SolidBitmap(60, 40, Color.White))
                    {
                        seeded = host.HistoryStore.AddObservedImage(canvas);
                    }

                    Application.DoEvents();
                    Assert.True(host.ActivateHistoryItem(seeded));
                    Application.DoEvents();

                    int previewRefreshes = 0;
                    int itemUpdates = 0;
                    host.HistoryStore.ItemPreviewRefreshed += (_, _) => previewRefreshes++;
                    host.HistoryStore.ItemUpdated += (_, _) => itemUpdates++;

                    using (var pasted = SolidBitmap(8, 8, Color.Lime))
                    {
                        editor.SetInternalClipboardImageForDiagnostics(pasted);
                        Assert.True(editor.PasteFromClipboardForDiagnostics());
                    }
                    editor.SetSelectedLayerXForTests(0f);
                    editor.SetSelectedLayerYForTests(0f);
                    Application.DoEvents();

                    int updatesBeforeSave = itemUpdates;
                    host.TriggerPersistedHistorySaveForTests();

                    // The floating paste reached the thumbnail source...
                    Assert.Equal(1, previewRefreshes);
                    Assert.NotNull(seeded.PreviewComposite);
                    Assert.Equal(Color.Lime.ToArgb(), seeded.PreviewComposite!.GetPixel(4, 4).ToArgb());

                    // ...and the save raised no persistence-scheduling event of its own.
                    Assert.Equal(updatesBeforeSave, itemUpdates);
                });
            });
        }

        [Fact]
        public void Capture_KeepsTheItemInStep_EvenWithPersistenceOff()
        {
            StaTest.Run(() =>
            {
                // Tracking the editor is not a persistence concern: with saving switched off the
                // item still has to follow what is on screen, or the thumbnail goes stale.
                using var editor = new screenzap.ImageDocumentEditor();
                using var host = new ClipboardDocumentHost(true, editor);

                host.HistoryStore.ReplaceAll(Array.Empty<ClipboardHistoryItem>());

                ClipboardHistoryItem seeded;
                using (var canvas = SolidBitmap(60, 40, Color.White))
                {
                    seeded = host.HistoryStore.AddObservedImage(canvas);
                }

                Application.DoEvents();
                Assert.True(host.ActivateHistoryItem(seeded));
                Application.DoEvents();

                using (var pasted = SolidBitmap(8, 8, Color.Lime))
                {
                    editor.SetInternalClipboardImageForDiagnostics(pasted);
                    Assert.True(editor.PasteFromClipboardForDiagnostics());
                }
                editor.SetSelectedLayerXForTests(0f);
                editor.SetSelectedLayerYForTests(0f);
                Application.DoEvents();

                host.TriggerPersistedHistorySaveForTests();

                var layer = Assert.Single(seeded.Overlay!.Layers);
                Assert.Equal(new RectangleF(0f, 0f, 8f, 8f), layer.Frame);
            });
        }

        [Fact]
        public void RestoredLayers_DoNotCollideWithTheNextPasteName()
        {
            StaTest.Run(() =>
            {
                // Names come off the live stack, not a counter that restarts at 1 on restore —
                // otherwise a restored "Paste 2" gets a twin the moment you paste again.
                using var editor = EditorFixture.WithCanvas(60, 40);
                using var restored = SolidBitmap(8, 8, Color.Lime);

                var item = ClipboardHistoryItem.FromImage(editor.CloneBaseBitmapForTests()!);
                try
                {
                    item.Overlay = new DocumentOverlay
                    {
                        Layers = new List<ImageLayer>
                        {
                            new ImageLayer(SolidBitmap(8, 8, Color.Lime), new RectangleF(0f, 0f, 8f, 8f))
                            {
                                Name = "Paste 1",
                            },
                            new ImageLayer(SolidBitmap(8, 8, Color.Cyan), new RectangleF(9f, 0f, 8f, 8f))
                            {
                                Name = "Paste 2",
                            },
                        },
                    };
                    ((IClipboardDocumentPresenter)editor).LoadHistoryItem(item);
                    Assert.Equal(2, editor.ImageLayerCountForTests);

                    using var pasted = SolidBitmap(6, 6, Color.Magenta);
                    editor.SetInternalClipboardImageForDiagnostics(pasted);
                    Assert.True(editor.PasteFromClipboardForDiagnostics());

                    Assert.Equal(
                        new[] { "Paste 3", "Paste 2", "Paste 1", "Background" },
                        Enumerable.Range(0, editor.SurfaceLayerCount).Select(editor.SurfaceLayerAt).Reverse().Select(l => l.Name).Append("Background").ToArray());
                }
                finally
                {
                    item.Dispose();
                }
            });
        }
    }
}
