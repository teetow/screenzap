using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using screenzap;
using screenzap.Components;
using screenzap.Components.Shared;
using Xunit;

namespace Screenzap.ViewportTests
{
    public class ThumbnailActionRegressionTests
    {





        [Fact]
        public void RevertToOriginal_ClearsPreviewComposite()
        {
            using var source = new Bitmap(8, 8);
            using (var g = Graphics.FromImage(source))
            {
                g.Clear(Color.DarkBlue);
            }

            using var preview = new Bitmap(8, 8);
            using (var g = Graphics.FromImage(preview))
            {
                g.Clear(Color.OrangeRed);
            }

            var item = ClipboardHistoryItem.FromImage(source);
            try
            {
                item.SetPreviewComposite(preview);
                Assert.NotNull(item.PreviewComposite);

                item.RevertToOriginal();

                Assert.Null(item.PreviewComposite);
                Assert.False(item.IsDirty);
            }
            finally
            {
                item.Dispose();
            }
        }

        [Fact]
        public void DuplicateFromNonActiveItem_DoesNotCaptureActivePresenterState()
        {
            Exception? failure = null;

            StaTest.Run(() =>
            {
                try
                {
                    using var presenter = new StubImagePresenter();
                    using var host = new ClipboardDocumentHost(true, presenter);

                    host.HistoryStore.ReplaceAll(Array.Empty<ClipboardHistoryItem>());

                    var first = AddImage(host.HistoryStore, Color.Red);
                    var second = AddImage(host.HistoryStore, Color.Blue);

                    Assert.True(host.ActivateHistoryItem(first));
                    presenter.CurrentColor = Color.Green; // simulate a live edit on the active presenter

                    var duplicateMethod = typeof(ClipboardDocumentHost).GetMethod("DuplicateItem", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(duplicateMethod);
                    duplicateMethod!.Invoke(host, new object[] { second });

                    // Duplicating a non-active item must not capture the active presenter's edits.
                    Assert.Equal(Argb(Color.Blue), PixelArgb(second));

                    var clones = host.HistoryStore.Items.Where(i => i.Id != first.Id && i.Id != second.Id).ToList();
                    Assert.Single(clones);
                    Assert.Equal(Argb(Color.Blue), PixelArgb(clones[0]));
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });

            if (failure != null)
            {
                throw new TargetInvocationException(failure);
            }
        }

        [Fact]
        public void SetItemAsClipboard_ExportsLiveComposite_PromotesWithoutFlattening()
        {
            StaTest.Run(() =>
            {
                using var editor = new ImageDocumentEditor();
                using var host = new ClipboardDocumentHost(true, editor);
                using var source = EditorFixture.Canvas(100, 80, Color.DarkBlue);
                var item = host.HistoryStore.AddObservedImage(source);
                Assert.True(host.ActivateHistoryItem(item));
                editor.ResizeSurface(new Size(400, 300));
                EditorFixture.PinModifiers(editor);
                editor.TestToggleRectTool();
                editor.TestFireMouseDownAtImagePixel(new Point(10, 10), MouseButtons.Left);
                editor.TestFireMouseMoveAtImagePixel(new Point(70, 50), MouseButtons.Left);
                editor.TestFireMouseUpAtImagePixel(new Point(70, 50), MouseButtons.Left);
                editor.TestDeactivateDrawingTool();
                using var expected = editor.BuildCompositeImageForTests();
                var newer = host.HistoryStore.AddObservedImage(source);
                item.AssignSystemHistoryId("old-system-entry");
                host.ClipboardImageWriterForDiagnostics = image =>
                {
                    var actual = (Bitmap)image;
                    Assert.Equal(expected.Size, actual.Size);
                    for (int y = 0; y < actual.Height; y++)
                        for (int x = 0; x < actual.Width; x++)
                            Assert.Equal(expected.GetPixel(x, y), actual.GetPixel(x, y));
                    return true;
                };
                Assert.True(host.SetItemAsClipboard(item));
                Assert.Same(item, host.HistoryStore.TopItem);
                Assert.Same(item, host.HistoryStore.ActiveItem);
                Assert.Equal(2, host.HistoryStore.Items.Count);
                Assert.Same(newer, host.HistoryStore.Items[1]);
                Assert.False(item.IsDirty);
                Assert.Null(item.SystemHistoryId);
                Assert.Equal(1, editor.TestAnnotationShapeCount);
                using var observed = ClipboardHistoryItem.FromImage(expected);
                observed.AssignSystemHistoryId("new-system-entry");
                Assert.Same(item, host.TryBindPendingCommittedSystemItem(observed));
                Assert.True(host.ExecuteHostCommand(EditorCommandId.Undo));
                Assert.Equal(0, editor.TestAnnotationShapeCount);
                Assert.True(item.IsDirty);
            });
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SetItemAsClipboard_FailedWrite_DoesNotPromoteOrLoseSystemBinding(bool throws)
        {
            StaTest.Run(() =>
            {
                using var presenter = new StubImagePresenter();
                using var host = new ClipboardDocumentHost(true, presenter);
                var item = AddImage(host.HistoryStore, Color.Red);
                item.AssignSystemHistoryId("existing-system-entry");
                item.MarkDirtyExternally();
                var top = AddImage(host.HistoryStore, Color.Blue);
                host.ClipboardImageWriterForDiagnostics = _ => throws ? throw new InvalidOperationException("Locked") : false;
                Assert.False(host.SetItemAsClipboard(item));
                Assert.Same(top, host.HistoryStore.TopItem);
                Assert.Equal("existing-system-entry", item.SystemHistoryId);
                Assert.True(item.IsDirty);
                Assert.False(host.IsInternalClipboardWriteWindow());
            });
        }

        [Fact]
        public void SetItemAsClipboard_CleanOlderItem_CanBePublishedRepeatedly()
        {
            StaTest.Run(() =>
            {
                using var editor = new ImageDocumentEditor();
                using var host = new ClipboardDocumentHost(true, editor);
                using var image = EditorFixture.Canvas(20, 30, Color.Teal);
                var item = host.HistoryStore.AddObservedImage(image);
                host.HistoryStore.AddObservedImage(image);
                int writes = 0;
                host.ClipboardImageWriterForDiagnostics = _ => { writes++; return true; };
                Assert.True(host.SetItemAsClipboard(item));
                Assert.True(host.SetItemAsClipboard(item));
                Assert.Equal(2, writes);
                Assert.Same(item, host.HistoryStore.TopItem);
                Assert.False(item.IsDirty);
                Assert.False(host.ExecuteHostCommand(EditorCommandId.Undo));
            });
        }

        [Fact]
        public void ObservedClipboardItem_DoesNotReplaceDirtyActiveItem_WhenHostIsHidden()
        {
            Exception? failure = null;

            StaTest.Run(() =>
            {
                try
                {
                    using var presenter = new StubImagePresenter();
                    using var host = new ClipboardDocumentHost(true, presenter);



                    var edited = AddImage(host.HistoryStore, Color.Red);
                    var newCapture = AddImage(host.HistoryStore, Color.Blue);

                    Assert.True(host.ActivateHistoryItem(edited));
                    presenter.CurrentColor = Color.Green; // live annotation edit, not yet stashed
                    edited.MarkDirtyExternally();

                    host.OnObservedClipboardItem(newCapture);

                    Assert.Same(edited, host.HistoryStore.ActiveItem);
                    Assert.Equal(Argb(Color.Green), Argb(presenter.CurrentColor));
                    Assert.Equal(newCapture, host.HistoryStore.TopItem);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });

            if (failure != null)
            {
                throw new TargetInvocationException(failure);
            }
        }

        [Fact]
        public void ActivatePreferredHistoryItem_PrefersDirtyActiveItemOverNewestCapture()
        {
            Exception? failure = null;

            StaTest.Run(() =>
            {
                try
                {
                    using var presenter = new StubImagePresenter();
                    using var host = new ClipboardDocumentHost(true, presenter);

                    var edited = AddImage(host.HistoryStore, Color.Red);
                    Assert.True(host.ActivateHistoryItem(edited));

                    presenter.CurrentColor = Color.Green; // live annotations, not yet stashed
                    edited.MarkDirtyExternally();

                    var newCapture = AddImage(host.HistoryStore, Color.Blue);
                    Assert.Same(newCapture, host.HistoryStore.TopItem);

                    Assert.True(host.ActivatePreferredHistoryItem());

                    Assert.Same(edited, host.HistoryStore.ActiveItem);
                    Assert.Equal(Argb(Color.Green), Argb(presenter.CurrentColor));
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });

            if (failure != null)
            {
                throw new TargetInvocationException(failure);
            }
        }

        [Fact]
        public void ActivateHistoryItem_DoesNotReloadAlreadyActivePresenter()
        {
            Exception? failure = null;

            StaTest.Run(() =>
            {
                try
                {
                    using var presenter = new StubImagePresenter();
                    using var host = new ClipboardDocumentHost(true, presenter);

                    var item = AddImage(host.HistoryStore, Color.Red);
                    Assert.True(host.ActivateHistoryItem(item));

                    presenter.CurrentColor = Color.Green; // live edit, not stashed yet
                    item.MarkDirtyExternally();

                    Assert.True(host.ActivateHistoryItem(item));

                    // Re-activating the already-active item must not reload the presenter (which would
                    // discard the live edit back to the item's stored Red).
                    Assert.Same(item, host.HistoryStore.ActiveItem);
                    Assert.Equal(Argb(Color.Green), Argb(presenter.CurrentColor));
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });

            if (failure != null)
            {
                throw new TargetInvocationException(failure);
            }
        }

        [Fact]
        public void DeleteActiveItem_ActivatesNearestRemainingItem()
        {
            Exception? failure = null;

            StaTest.Run(() =>
            {
                try
                {
                    using var presenter = new StubImagePresenter();
                    using var host = new ClipboardDocumentHost(true, presenter);

                    host.HistoryStore.ReplaceAll(Array.Empty<ClipboardHistoryItem>());

                    var first = AddImage(host.HistoryStore, Color.Red);
                    var second = AddImage(host.HistoryStore, Color.Blue);
                    var third = AddImage(host.HistoryStore, Color.Green);

                    Assert.True(host.ActivateHistoryItem(second));
                    Assert.Same(second, host.HistoryStore.ActiveItem);

                    var deleteMethod = typeof(ClipboardDocumentHost).GetMethod("DeleteItemAsync", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(deleteMethod);
                    var task = (System.Threading.Tasks.Task?)deleteMethod!.Invoke(host, new object[] { second });
                    Assert.NotNull(task);
                    task!.GetAwaiter().GetResult();

                    Assert.DoesNotContain(host.HistoryStore.Items, i => ReferenceEquals(i, second));
                    Assert.Same(first, host.HistoryStore.ActiveItem);
                    Assert.Contains(host.HistoryStore.Items, i => ReferenceEquals(i, third));
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });

            if (failure != null)
            {
                throw new TargetInvocationException(failure);
            }
        }













        [Fact]
        public void DeleteSystemHistoryItem_RemovesLocalItemBeforeSystemDeleteCompletes()
        {
            Exception? failure = null;

            StaTest.Run(() =>
            {
                try
                {
                    using var presenter = new StubImagePresenter();
                    using var host = new ClipboardDocumentHost(true, presenter);

                    host.HistoryStore.ReplaceAll(Array.Empty<ClipboardHistoryItem>());

                    var first = AddImage(host.HistoryStore, Color.Red);
                    var second = AddImage(host.HistoryStore, Color.Blue);
                    second.AssignSystemHistoryId("{TEST-SYSTEM-ID}");

                    Assert.True(host.ActivateHistoryItem(second));
                    Assert.Same(second, host.HistoryStore.ActiveItem);

                    var releaseSystemDelete = new TaskCompletionSource<bool>();
                    bool systemDeleteStarted = false;
                    host.TryDeleteFromSystemHistoryAsync = async systemHistoryId =>
                    {
                        systemDeleteStarted = true;
                        Assert.Equal("{TEST-SYSTEM-ID}", systemHistoryId);
                        await releaseSystemDelete.Task;
                        return true;
                    };

                    var deleteMethod = typeof(ClipboardDocumentHost).GetMethod("DeleteItemAsync", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(deleteMethod);
                    var task = (Task?)deleteMethod!.Invoke(host, new object[] { second });
                    Assert.NotNull(task);
                    Assert.True(task!.IsCompleted);

                    Assert.DoesNotContain(host.HistoryStore.Items, i => ReferenceEquals(i, second));
                    Assert.Same(first, host.HistoryStore.ActiveItem);
                    Assert.True(host.HistoryStore.ContainsSuppressedSystemHistoryId("{TEST-SYSTEM-ID}"));
                    Assert.True(systemDeleteStarted);

                    releaseSystemDelete.SetResult(true);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });

            if (failure != null)
            {
                throw new TargetInvocationException(failure);
            }
        }











        internal static Bitmap MakeSolid(Color color)
        {
            var bmp = new Bitmap(8, 8);
            using var g = Graphics.FromImage(bmp);
            g.Clear(color);
            return bmp;
        }

        private static ClipboardHistoryItem AddImage(ClipboardHistoryStore store, Color color)
        {
            using var bmp = MakeSolid(color);
            return store.AddObservedImage(bmp);
        }

        private static int Argb(Color color) => color.ToArgb();

        private static int PixelArgb(ClipboardHistoryItem item) => item.CurrentImage!.GetPixel(0, 0).ToArgb();

        /// <summary>
        /// Minimal image presenter for host/store behavior tests. Tracks the currently-displayed
        /// content as a single color so tests can simulate a live in-presenter edit
        /// (<see cref="CurrentColor"/>) and assert which content was loaded/stashed without a real
        /// image editor. Mirrors how the real ImageDocumentEditor round-trips an item's CurrentImage.
        /// </summary>
        private sealed class StubImagePresenter : IClipboardDocumentPresenter
        {



            public Color CurrentColor { get; set; } = Color.Empty;



            public string DisplayName => "StubImage";

            public void AttachHostServices(EditorHostServices services)
            {
            }

            public bool CanHandleClipboard(System.Windows.Forms.IDataObject dataObject)
            {
                return false;
            }

            public void LoadFromClipboard(System.Windows.Forms.IDataObject dataObject)
            {
            }

            public bool CanExecute(EditorCommandId commandId)
            {
                return false;
            }

            public bool TryExecute(EditorCommandId commandId)
            {
                return false;
            }





            public bool CanPresent(ClipboardHistoryItem item)
            {
                return item.Kind == ClipboardItemKind.Image;
            }

            public void LoadHistoryItem(ClipboardHistoryItem item)
            {
                CurrentColor = item.CurrentImage != null ? item.CurrentImage.GetPixel(0, 0) : Color.Empty;
            }

            public void StashHistoryItemState(ClipboardHistoryItem item)
            {
                using var bmp = MakeSolid(CurrentColor);
                item.UpdateCurrentImage(bmp);
            }

            public object? GetCurrentContent()
            {
                return CurrentColor == Color.Empty ? null : MakeSolid(CurrentColor);
            }



            public void Dispose() { }
        }
    }
}
