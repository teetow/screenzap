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
using screenzap.Testing;
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

            var first = store.AddObservedText("first");
            var second = store.AddObservedText("second");
            changedCalls = 0;

            store.Activate(first);
            store.Activate(first);
            store.Activate(second);

            Assert.Equal(0, changedCalls);
            Assert.Equal(2, activeChangedCalls);
            Assert.Same(second, store.ActiveItem);
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
                first = ClipboardHistoryItem.FromText("first");
                second = ClipboardHistoryItem.FromText("second");

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
                    using var imagePresenter = new ImageEditor();
                    using var textPresenter = new TextEditor();
                    using var host = new ClipboardEditorHostForm(
                        new IClipboardDocumentPresenter[] { imagePresenter, textPresenter },
                        persistence,
                        restorePersistedHistory: true,
                        persistHistoryChanges: false,
                        allowSystemClipboardWrites: false)
                    {
                        SuppressActivation = true,
                        ShowInTaskbar = false
                    };

                    host.CreateControl();

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
        public void SystemHistoryRefresh_OrdersNewestSystemItemAboveSeededFallback()
        {
            StaTest.Run(() =>
            {
                var store = new ClipboardHistoryStore();
                using var host = new Form();
                host.CreateControl();

                using var fallbackImage = CreateSolidBitmap(Color.DarkCyan);
                var fallback = store.AddObservedImage(fallbackImage);
                fallback.IsSeededFallback = true;

                using var newestImage = CreateSolidBitmap(Color.OrangeRed);
                var newest = ClipboardHistoryItem.FromImage(newestImage);
                newest.AssignSystemHistoryId("system-newest");

                using var service = new SystemClipboardHistoryService(
                    store,
                    host,
                    onItemObserved: null,
                    tryBindPendingCommittedItem: null,
                    isInternalWriteWindow: null,
                    includeNonBitmapItems: null);

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
                using var host = new Form();
                host.CreateControl();

                using var localImage = CreateSolidBitmap(Color.MidnightBlue);
                var localOnly = store.AddObservedImage(localImage);

                using var newestImage = CreateSolidBitmap(Color.Gold);
                var newest = ClipboardHistoryItem.FromImage(newestImage);
                newest.AssignSystemHistoryId("system-newest");

                using var service = new SystemClipboardHistoryService(
                    store,
                    host,
                    onItemObserved: null,
                    tryBindPendingCommittedItem: null,
                    isInternalWriteWindow: null,
                    includeNonBitmapItems: null);

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
                using var host = new Form();
                host.CreateControl();

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
                    host,
                    onItemObserved: null,
                    tryBindPendingCommittedItem: null,
                    isInternalWriteWindow: null,
                    includeNonBitmapItems: null);

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
        public void SystemHistoryRefresh_KeepsUserDuplicateDistinctFromReimport()
        {
            StaTest.Run(() =>
            {
                var store = new ClipboardHistoryStore();
                using var host = new Form();
                host.CreateControl();

                using var image = CreateSolidBitmap(Color.SeaGreen);
                var original = store.AddObservedImage(image);
                var userDuplicate = store.Duplicate(original); // explicit user copy -> IsUserDuplicate

                // Same content re-imported from Windows history under a fresh system id.
                using var reimportImage = CreateSolidBitmap(Color.SeaGreen);
                var reimport = ClipboardHistoryItem.FromImage(reimportImage);
                reimport.AssignSystemHistoryId("sys-id");

                using var service = new SystemClipboardHistoryService(
                    store,
                    host,
                    onItemObserved: null,
                    tryBindPendingCommittedItem: null,
                    isInternalWriteWindow: null,
                    includeNonBitmapItems: null);

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
                    using var kit = new UiTestKit(new Size(800, 600), withHost: true, visible: false);
                    kit.Host!.HistoryStore.ReplaceAll(Array.Empty<ClipboardHistoryItem>());

                    var first = kit.LoadCanvas(96, 64, Color.LightCyan);
                    using var pasted = new Bitmap(20, 14);
                    using (var g = Graphics.FromImage(pasted))
                    {
                        g.Clear(Color.Purple);
                    }

                    kit.PasteImage(pasted);
                    Assert.Equal(1, kit.Editor.ImageLayerCountForTests);

                    using var secondImage = new Bitmap(96, 64);
                    using (var g = Graphics.FromImage(secondImage))
                    {
                        g.Clear(Color.LightSalmon);
                    }

                    var second = kit.Host.HistoryStore.AddObservedImage(secondImage);
                    Assert.True(kit.ClickHistoryThumbnail(second));
                    Assert.Same(second, kit.Host.HistoryStore.ActiveItem);
                    Assert.Equal(0, kit.Editor.ImageLayerCountForTests);

                    Assert.True(kit.ClickHistoryThumbnail(first));
                    Assert.Same(first, kit.Host.HistoryStore.ActiveItem);
                    Assert.Equal(1, kit.Editor.ImageLayerCountForTests);
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
