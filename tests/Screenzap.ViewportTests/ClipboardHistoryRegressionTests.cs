using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using screenzap;
using screenzap.Components;
using screenzap.Components.Shared;

using Xunit;

namespace Screenzap.ViewportTests
{
    public class ClipboardHistoryRegressionTests
    {
        [Fact]
        public void Activate_RaisesActiveItemChanged_WithoutChangedEvent()
        {
            var store = new ClipboardHistoryStore();
            int changedCalls = 0;
            int activeChangedCalls = 0;

            store.Changed += (_, _) => changedCalls++;
            store.ActiveItemChanged += (_, _) => activeChangedCalls++;

            using var firstImg = CreateSolidBitmap(Color.Red);
            using var secondImg = CreateSolidBitmap(Color.Blue);
            var first = store.AddObservedImage(firstImg);
            var second = store.AddObservedImage(secondImg);
            changedCalls = 0;

            store.Activate(first);
            store.Activate(first);
            store.Activate(second);

            Assert.Equal(0, changedCalls);
            Assert.Equal(2, activeChangedCalls);
            Assert.Same(second, store.ActiveItem);
        }

        [Fact]
        public void EnsureTopObservedImage_DeduplicatesUnchangedClipboard_AndAddsChangedImage()
        {
            var store = new ClipboardHistoryStore();
            using var firstImage = CreateSolidBitmap(Color.Red);
            using var sameImage = CreateSolidBitmap(Color.Red);
            using var changedImage = CreateSolidBitmap(Color.Blue);

            var first = store.AddObservedImage(firstImage);
            var (same, sameAdded) = store.EnsureTopObservedImage(sameImage);

            Assert.False(sameAdded);
            Assert.Same(first, same);
            Assert.Single(store.Items);

            var (changed, changedAdded) = store.EnsureTopObservedImage(changedImage);

            Assert.True(changedAdded);
            Assert.Same(changed, store.TopItem);
            Assert.Equal(2, store.Items.Count);
        }

        [Fact]
        public void SaveActiveItemOnly_UpdatesManifestActiveId_WithoutDroppingItems()
        {
            var root = Path.Combine(Path.GetTempPath(), "screenzap-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            ClipboardHistoryItem? first = null;
            ClipboardHistoryItem? second = null;

            try
            {
                var persistence = new ClipboardHistoryPersistence(root);
                using (var firstImg = CreateSolidBitmap(Color.Red))
                using (var secondImg = CreateSolidBitmap(Color.Blue))
                {
                    first = ClipboardHistoryItem.FromImage(firstImg);
                    second = ClipboardHistoryItem.FromImage(secondImg);
                }

                persistence.Save(new[] { first, second }, first);
                persistence.SaveActiveItemOnly(second.Id);

                var restored = persistence.Load();
                try
                {
                    Assert.Equal(second.Id, restored.ActiveItemId);
                    Assert.Equal(2, restored.Items.Count);
                }
                finally
                {
                    foreach (var item in restored.Items)
                    {
                        item.Dispose();
                    }
                }
            }
            finally
            {
                first?.Dispose();
                second?.Dispose();

                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
                catch
                {
                    // Best-effort cleanup for test artifacts.
                }
            }
        }

        [Fact]
        public void Load_KeepsPersistedPngsFileBacked_AndSaveDoesNotRewriteThem()
        {
            var root = Path.Combine(Path.GetTempPath(), "screenzap-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            ClipboardHistoryItem? source = null;
            List<ClipboardHistoryItem>? restoredItems = null;

            try
            {
                using (var image = new Bitmap(320, 180))
                {
                    using var graphics = Graphics.FromImage(image);
                    graphics.Clear(Color.MediumPurple);
                    source = ClipboardHistoryItem.FromImage(image);
                }

                new ClipboardHistoryPersistence(root).Save(new[] { source }, source);
                var pngPaths = Directory.GetFiles(root, "*.png");
                Assert.Equal(4, pngPaths.Length);

                var untouchedTimestamp = new DateTime(2020, 1, 2, 3, 4, 6, DateTimeKind.Utc);
                foreach (var path in pngPaths)
                {
                    File.SetLastWriteTimeUtc(path, untouchedTimestamp);
                }

                var persistence = new ClipboardHistoryPersistence(root);
                var restored = persistence.Load();
                restoredItems = restored.Items;

                var item = Assert.Single(restored.Items);
                Assert.False(item.HasLoadedPngBytesForDiagnostics);
                Assert.False(item.HasLoadedThumbnailForDiagnostics);

                // Decoding the active bitmap streams straight from its backing file; it should not
                // pull the compressed PNG into the managed blob cache.
                Assert.Equal(Color.MediumPurple.ToArgb(), item.CurrentImage!.GetPixel(0, 0).ToArgb());
                Assert.False(item.HasLoadedPngBytesForDiagnostics);

                persistence.Save(restored.Items, item);

                Assert.False(item.HasLoadedPngBytesForDiagnostics);
                foreach (var path in pngPaths)
                {
                    Assert.Equal(untouchedTimestamp, File.GetLastWriteTimeUtc(path));
                }
            }
            finally
            {
                source?.Dispose();
                if (restoredItems != null)
                {
                    foreach (var item in restoredItems)
                    {
                        item.Dispose();
                    }
                }

                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
                catch
                {
                    // Best-effort cleanup for test artifacts.
                }
            }
        }

        [Fact]
        public void MarkClean_PreservesUndoSnapshot_SoUndoRemainsAvailableAfterCommit()
        {
            using var original = new Bitmap(4, 4);
            using var edited = new Bitmap(4, 4);
            using (var g = Graphics.FromImage(edited))
            {
                g.Clear(Color.Red);
            }

            using var item = ClipboardHistoryItem.FromImage(original);
            item.UpdateCurrentImage(edited);
            Assert.True(item.IsDirty);

            var snapshot = new UndoRedo.Snapshot { Index = 0 };
            snapshot.Steps.Add(new ImageUndoStep(
                new Rectangle(0, 0, 4, 4),
                new Bitmap(original),
                new Bitmap(edited),
                Rectangle.Empty,
                Rectangle.Empty,
                true,
                null,
                null));
            item.UndoSnapshot = snapshot;

            item.MarkClean();

            Assert.False(item.IsDirty);
            Assert.NotNull(item.UndoSnapshot);
            Assert.Single(item.UndoSnapshot!.Steps);
            Assert.Equal(0, item.UndoSnapshot.Index);
        }

        [Fact]
        public void RestoredActiveImageItem_LoadsIntoPresenterDuringHostConstruction()
        {
            var root = Path.Combine(Path.GetTempPath(), "screenzap-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            ClipboardHistoryItem? persistedItem = null;

            try
            {
                var persistence = new ClipboardHistoryPersistence(root);
                using var persistedImage = CreateSolidBitmap(Color.LimeGreen);
                var expectedSize = persistedImage.Size;
                var expectedArgb = persistedImage.GetPixel(0, 0).ToArgb();

                persistedItem = ClipboardHistoryItem.FromImage(persistedImage);
                persistence.Save(new[] { persistedItem }, persistedItem);

                StaTest.Run(() =>
                {
                    using var imagePresenter = new ImageDocumentEditor();
                    using var host = new ClipboardDocumentHost(
                        new IClipboardDocumentPresenter[] { imagePresenter },
                        persistence,
                        restorePersistedHistory: true,
                        persistHistoryChanges: false,
                        allowSystemClipboardWrites: false);

                    Assert.Same(imagePresenter, host.ActivePresenter);

                    using var loaded = imagePresenter.CloneBaseBitmapForTests();
                    Assert.NotNull(loaded);
                    Assert.Equal(expectedSize, loaded!.Size);
                    Assert.Equal(expectedArgb, loaded.GetPixel(0, 0).ToArgb());
                });
            }
            finally
            {
                persistedItem?.Dispose();

                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
                catch
                {
                    // Best-effort cleanup for test artifacts.
                }
            }
        }

        [Fact]
        public void SystemHistoryService_PrimesKnownIdsFromExistingStore()
        {
            StaTest.Run(() =>
            {
                var store = new ClipboardHistoryStore();

                using var knownImage = CreateSolidBitmap(Color.DarkSlateBlue);
                var known = store.AddObservedImage(knownImage);
                known.AssignSystemHistoryId("already-restored");

                using var localImage = CreateSolidBitmap(Color.DarkKhaki);
                store.AddObservedImage(localImage);

                using var service = new SystemClipboardHistoryService(
                    store,
                    action => action(),
                    onItemObserved: null,
                    tryBindPendingCommittedItem: null,
                    isInternalWriteWindow: null);

                Assert.Equal(
                    new[] { "already-restored" },
                    service.KnownSystemHistoryIdsForDiagnostics);
            });
        }

        [Fact]
        public void SystemHistoryRefresh_OrdersNewestSystemItemAboveSeededFallback()
        {
            StaTest.Run(() =>
            {
                var store = new ClipboardHistoryStore();

                using var fallbackImage = CreateSolidBitmap(Color.DarkCyan);
                var fallback = store.AddObservedImage(fallbackImage);
                fallback.IsSeededFallback = true;

                using var newestImage = CreateSolidBitmap(Color.OrangeRed);
                var newest = ClipboardHistoryItem.FromImage(newestImage);
                newest.AssignSystemHistoryId("system-newest");

                using var service = new SystemClipboardHistoryService(
                    store,
                    action => action(),
                    onItemObserved: null,
                    tryBindPendingCommittedItem: null,
                    isInternalWriteWindow: null);

                ApplySystemSnapshot(
                    service,
                    new List<(string id, DateTimeOffset timestamp, ClipboardHistoryItem built)?>
                    {
                        ("system-newest", DateTimeOffset.UtcNow.AddSeconds(1), newest)
                    });

                Assert.Same(newest, store.TopItem);
                Assert.Equal(new[] { newest.Id, fallback.Id }, store.Items.Select(item => item.Id).ToArray());
            });
        }

        [Fact]
        public void SystemHistoryRefresh_DoesNotPinCleanLocalItemAboveNewerSystemItem()
        {
            StaTest.Run(() =>
            {
                var store = new ClipboardHistoryStore();

                using var localImage = CreateSolidBitmap(Color.MidnightBlue);
                var localOnly = store.AddObservedImage(localImage);

                using var newestImage = CreateSolidBitmap(Color.Gold);
                var newest = ClipboardHistoryItem.FromImage(newestImage);
                newest.AssignSystemHistoryId("system-newest");

                using var service = new SystemClipboardHistoryService(
                    store,
                    action => action(),
                    onItemObserved: null,
                    tryBindPendingCommittedItem: null,
                    isInternalWriteWindow: null);

                ApplySystemSnapshot(
                    service,
                    new List<(string id, DateTimeOffset timestamp, ClipboardHistoryItem built)?>
                    {
                        ("system-newest", DateTimeOffset.UtcNow.AddSeconds(1), newest)
                    });

                Assert.Same(newest, store.TopItem);
                Assert.Equal(new[] { newest.Id, localOnly.Id }, store.Items.Select(item => item.Id).ToArray());
            });
        }

        [Fact]
        public void SystemHistoryRefresh_DoesNotDuplicateCommittedLocalItemOnReimport()
        {
            StaTest.Run(() =>
            {
                var store = new ClipboardHistoryStore();

                // A "set as active"/committed item: local-only (no SystemHistoryId), not a seeded
                // fallback, carrying a suppressed old system id. This is the on-disk shape of 539e706c.
                using var committedImage = CreateSolidBitmap(Color.SteelBlue);
                var committed = store.AddObservedImage(committedImage);
                committed.AddSuppressedSystemHistoryId("old-system-id");

                // Windows re-imports the SAME content under a fresh system id — e.g. the entry created
                // by the set-as-active write that never rebound because the app restarted (the pending-
                // commit window is in-memory only). tryBindPendingCommittedItem is null here to model
                // that lost state.
                using var reimportImage = CreateSolidBitmap(Color.SteelBlue);
                var reimport = ClipboardHistoryItem.FromImage(reimportImage);
                reimport.AssignSystemHistoryId("new-system-id");

                using var service = new SystemClipboardHistoryService(
                    store,
                    action => action(),
                    onItemObserved: null,
                    tryBindPendingCommittedItem: null,
                    isInternalWriteWindow: null);

                ApplySystemSnapshot(
                    service,
                    new List<(string id, DateTimeOffset timestamp, ClipboardHistoryItem built)?>
                    {
                        ("new-system-id", DateTimeOffset.UtcNow.AddSeconds(1), reimport)
                    });

                // The committed item should absorb the re-imported system id rather than produce a
                // second row with identical content.
                Assert.Single(store.Items);
                Assert.Equal("new-system-id", store.Items[0].SystemHistoryId);
            });
        }

        [Fact]
        public void SystemHistoryRefresh_DoesNotBlackholeFreshContentUnderPreviouslySuppressedId()
        {
            // Regression test: Windows appears to report one stable id for "whatever is currently
            // on the clipboard" rather than minting a fresh id per content snapshot. A commit/
            // set-active write suppresses the id an item is moving off of, expecting a fresh id to
            // show up for whatever comes next - but if that "next" content keeps arriving under the
            // SAME (now-suppressed) id, it must not be silently and permanently dropped forever.
            StaTest.Run(() =>
            {
                var store = new ClipboardHistoryStore();

                using var committedImage = CreateSolidBitmap(Color.SteelBlue);
                var committed = store.AddObservedImage(committedImage);
                committed.AddSuppressedSystemHistoryId("live-id");

                // Later, unrelated content (e.g. copied from another app entirely) is reported
                // under that same "live-id".
                using var freshImage = CreateSolidBitmap(Color.Orange);
                var fresh = ClipboardHistoryItem.FromImage(freshImage);
                fresh.AssignSystemHistoryId("live-id");

                using var service = new SystemClipboardHistoryService(
                    store,
                    action => action(),
                    onItemObserved: null,
                    tryBindPendingCommittedItem: null,
                    isInternalWriteWindow: null);

                ApplySystemSnapshot(
                    service,
                    new List<(string id, DateTimeOffset timestamp, ClipboardHistoryItem built)?>
                    {
                        ("live-id", DateTimeOffset.UtcNow.AddSeconds(1), fresh)
                    });

                Assert.Contains(store.Items, item => ReferenceEquals(item, fresh));
                Assert.Same(fresh, store.TopItem);
            });
        }

        [Fact]
        public void SystemHistoryRefresh_ReplacesFreshContentWhenNewestSlotReusesKnownId()
        {
            StaTest.Run(() =>
            {
                var store = new ClipboardHistoryStore();

                using var oldImage = CreateSolidBitmap(Color.SteelBlue);
                var old = store.AddObservedImage(oldImage);
                old.AssignSystemHistoryId("reused-live-id");

                using var freshImage = CreateSolidBitmap(Color.Orange);
                var fresh = ClipboardHistoryItem.FromImage(freshImage);
                fresh.AssignSystemHistoryId("reused-live-id");

                using var service = new SystemClipboardHistoryService(
                    store,
                    action => action(),
                    onItemObserved: null,
                    tryBindPendingCommittedItem: null,
                    isInternalWriteWindow: null);

                ApplySystemSnapshot(
                    service,
                    new List<(string id, DateTimeOffset timestamp, ClipboardHistoryItem built)?>
                    {
                        ("reused-live-id", DateTimeOffset.UtcNow.AddSeconds(1), fresh)
                    });

                Assert.Same(fresh, store.TopItem);
                Assert.Equal("reused-live-id", fresh.SystemHistoryId);
                Assert.Contains(store.Items, item => ReferenceEquals(item, old));
                Assert.Null(old.SystemHistoryId);
            });
        }

        [Fact]
        public void SystemHistoryRefresh_KeepsUserDuplicateDistinctFromReimport()
        {
            StaTest.Run(() =>
            {
                var store = new ClipboardHistoryStore();

                using var image = CreateSolidBitmap(Color.SeaGreen);
                var original = store.AddObservedImage(image);
                var userDuplicate = store.Duplicate(original); // explicit user copy -> IsUserDuplicate

                // Same content re-imported from Windows history under a fresh system id.
                using var reimportImage = CreateSolidBitmap(Color.SeaGreen);
                var reimport = ClipboardHistoryItem.FromImage(reimportImage);
                reimport.AssignSystemHistoryId("sys-id");

                using var service = new SystemClipboardHistoryService(
                    store,
                    action => action(),
                    onItemObserved: null,
                    tryBindPendingCommittedItem: null,
                    isInternalWriteWindow: null);

                ApplySystemSnapshot(
                    service,
                    new List<(string id, DateTimeOffset timestamp, ClipboardHistoryItem built)?>
                    {
                        ("sys-id", DateTimeOffset.UtcNow.AddSeconds(1), reimport)
                    });

                // The non-duplicate original absorbs the system id; the intentional duplicate stays.
                Assert.Equal(2, store.Items.Count);
                Assert.Contains(store.Items, item => item.IsUserDuplicate);
                Assert.Contains(store.Items, item => item.SystemHistoryId == "sys-id" && !item.IsUserDuplicate);
            });
        }

        [Fact]
        public void HistoryThumbnailClick_StashesAndRestoresImageLayerState()
        {
            Exception? failure = null;

            var thread = new Thread(() =>
            {
                try
                {
                    using var editor = new ImageDocumentEditor();
                    using var host = new ClipboardDocumentHost(true, editor);
                    using var image = EditorFixture.Canvas(96, 64, Color.LightCyan);
                    var first = host.HistoryStore.AddObservedImage(image);
                    host.ActivateHistoryItem(first);
                    using var pasted = new Bitmap(20, 14);
                    using (var g = Graphics.FromImage(pasted))
                    {
                        g.Clear(Color.Purple);
                    }

                    editor.SetInternalClipboardImageForDiagnostics(pasted);
                    Assert.True(editor.PasteFromClipboardForDiagnostics());
                    Assert.Equal(1, editor.ImageLayerCountForTests);

                    using var secondImage = new Bitmap(96, 64);
                    using (var g = Graphics.FromImage(secondImage))
                    {
                        g.Clear(Color.LightSalmon);
                    }

                    var second = host.HistoryStore.AddObservedImage(secondImage);
                    Assert.True(host.ActivateHistoryItem(second));
                    Assert.Same(second, host.HistoryStore.ActiveItem);
                    Assert.Equal(0, editor.ImageLayerCountForTests);

                    Assert.True(host.ActivateHistoryItem(first));
                    Assert.Same(first, host.HistoryStore.ActiveItem);
                    Assert.Equal(1, editor.ImageLayerCountForTests);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (failure != null)
            {
                throw failure;
            }
        }

        private static Bitmap CreateSolidBitmap(Color color)
        {
            var bitmap = new Bitmap(8, 8);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(color);
            return bitmap;
        }

        private static void ApplySystemSnapshot(
            SystemClipboardHistoryService service,
            List<(string id, DateTimeOffset timestamp, ClipboardHistoryItem built)?> translated)
        {
            var method = typeof(SystemClipboardHistoryService).GetMethod(
                "ApplySnapshot",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method!.Invoke(service, new object[] { translated });
        }
    }
}
