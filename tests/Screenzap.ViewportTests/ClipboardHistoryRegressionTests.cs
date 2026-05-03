using System;
using System.Drawing;
using System.IO;
using screenzap;
using screenzap.Components;
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
    }
}
