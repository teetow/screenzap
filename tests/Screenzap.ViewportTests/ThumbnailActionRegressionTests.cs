using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
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

            RunInSta(() =>
            {
                try
                {
                    using var presenter = new StubTextPresenter();
                    using var host = new ClipboardEditorHostForm(true, presenter)
                    {
                        SuppressActivation = true,
                        ShowInTaskbar = false
                    };

                    host.CreateControl();
                    host.HistoryStore.ReplaceAll(Array.Empty<ClipboardHistoryItem>());

                    var first = host.HistoryStore.AddObservedText("first");
                    var second = host.HistoryStore.AddObservedText("second");

                    Assert.True(host.ActivateHistoryItem(first));
                    presenter.CurrentText = "first-edited-in-presenter";

                    var duplicateMethod = typeof(ClipboardEditorHostForm).GetMethod("DuplicateItem", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(duplicateMethod);
                    duplicateMethod!.Invoke(host, new object[] { second });

                    Assert.Equal("second", second.CurrentText);

                    var clones = host.HistoryStore.Items.Where(i => i.Kind == ClipboardItemKind.Text && i.Id != first.Id && i.Id != second.Id).ToList();
                    Assert.Single(clones);
                    Assert.Equal("second", clones[0].CurrentText);
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
        public void SetItemAsClipboard_PrefersPreviewComposite_ForImageItems()
        {
            Exception? failure = null;

            RunInSta(() =>
            {
                try
                {
                    using var imagePresenter = new screenzap.ImageEditor();
                    using var textPresenter = new screenzap.TextEditor();
                    using var host = new ClipboardEditorHostForm(true, imagePresenter, textPresenter)
                    {
                        SuppressActivation = true,
                        ShowInTaskbar = false
                    };

                    host.CreateControl();
                    host.HistoryStore.ReplaceAll(Array.Empty<ClipboardHistoryItem>());

                    using var baseImage = new Bitmap(10, 10);
                    using (var g = Graphics.FromImage(baseImage))
                    {
                        g.Clear(Color.DarkBlue);
                    }

                    using var previewComposite = new Bitmap(10, 10);
                    using (var g = Graphics.FromImage(previewComposite))
                    {
                        g.Clear(Color.OrangeRed);
                    }

                    var item = ClipboardHistoryItem.FromImage(baseImage);
                    item.SetPreviewComposite(previewComposite);
                    item.MarkDirtyExternally();

                    host.HistoryStore.ReplaceAll(new[] { item });

                    Bitmap? written = null;
                    host.ClipboardImageWriterForDiagnostics = image =>
                    {
                        written?.Dispose();
                        written = new Bitmap(image);
                        return true;
                    };

                    var setItemMethod = typeof(ClipboardEditorHostForm).GetMethod("SetItemAsClipboard", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(setItemMethod);
                    setItemMethod!.Invoke(host, new object[] { item });

                    try
                    {
                        Assert.NotNull(written);
                        Assert.Equal(Color.OrangeRed.ToArgb(), written!.GetPixel(0, 0).ToArgb());
                    }
                    finally
                    {
                        written?.Dispose();
                    }
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

            RunInSta(() =>
            {
                try
                {
                    using var presenter = new StubTextPresenter();
                    using var host = new ClipboardEditorHostForm(true, presenter)
                    {
                        SuppressActivation = true,
                        ShowInTaskbar = false
                    };

                    host.CreateControl();
                    host.HistoryStore.ReplaceAll(Array.Empty<ClipboardHistoryItem>());

                    var first = host.HistoryStore.AddObservedText("first");
                    var second = host.HistoryStore.AddObservedText("second");
                    var third = host.HistoryStore.AddObservedText("third");

                    Assert.True(host.ActivateHistoryItem(second));
                    Assert.Same(second, host.HistoryStore.ActiveItem);

                    var deleteMethod = typeof(ClipboardEditorHostForm).GetMethod("DeleteItemAsync", BindingFlags.Instance | BindingFlags.NonPublic);
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

            RunInSta(() =>
            {
                try
                {
                    using var presenter = new StubTextPresenter();
                    using var host = new ClipboardEditorHostForm(true, presenter)
                    {
                        SuppressActivation = true,
                        ShowInTaskbar = false
                    };

                    host.CreateControl();
                    host.HistoryStore.ReplaceAll(Array.Empty<ClipboardHistoryItem>());

                    var first = host.HistoryStore.AddObservedText("first");
                    var second = host.HistoryStore.AddObservedText("second");
                    second.AssignSystemHistoryId("{TEST-SYSTEM-ID}");

                    Assert.True(host.ActivateHistoryItem(second));
                    Assert.Same(second, host.HistoryStore.ActiveItem);

                    var releaseSystemDelete = new TaskCompletionSource<bool>();
                    bool systemDeleteStarted = false;
                    host.TryDeleteFromSystemHistoryAsync = async item =>
                    {
                        systemDeleteStarted = true;
                        Assert.Equal("{TEST-SYSTEM-ID}", item.SystemHistoryId);
                        await releaseSystemDelete.Task;
                        return true;
                    };

                    var deleteMethod = typeof(ClipboardEditorHostForm).GetMethod("DeleteItemAsync", BindingFlags.Instance | BindingFlags.NonPublic);
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

        [Fact]
        public void ThumbnailClick_ActivatesMatchingItem()
        {
            Exception? failure = null;

            RunInSta(() =>
            {
                try
                {
                    using var panel = new ClipboardHistoryPanel();
                    var store = new ClipboardHistoryStore();
                    panel.AttachStore(store);

                    var first = store.AddObservedText("first");
                    var second = store.AddObservedText("second");
                    var third = store.AddObservedText("third");

                    ClipboardHistoryItem? activated = null;
                    panel.ItemActivated += (_, item) => activated = item;

                    var buttonsField = typeof(ClipboardHistoryPanel).GetField("buttons", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(buttonsField);
                    var buttons = buttonsField!.GetValue(panel);
                    Assert.NotNull(buttons);

                    var dictionaryType = buttons!.GetType();
                    var tryGetValue = dictionaryType.GetMethod("TryGetValue");
                    Assert.NotNull(tryGetValue);

                    foreach (var item in new[] { first, second, third })
                    {
                        var args = new object?[] { item.Id, null };
                        var found = (bool)tryGetValue!.Invoke(buttons, args)!;
                        Assert.True(found);

                        var button = args[1];
                        Assert.NotNull(button);

                        var onClick = button!.GetType().GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic);
                        Assert.NotNull(onClick);
                        onClick!.Invoke(button, new object[] { EventArgs.Empty });

                        Assert.Same(item, activated);
                    }
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
        public void SwitchingActiveItem_DoesNotMutateExistingThumbnailButtonSizes()
        {
            Exception? failure = null;

            RunInSta(() =>
            {
                try
                {
                    using var host = new Form
                    {
                        ClientSize = new Size(220, 420),
                        ShowInTaskbar = false,
                        StartPosition = FormStartPosition.Manual,
                        Location = new Point(-32000, -32000)
                    };

                    using var panel = new ClipboardHistoryPanel
                    {
                        Dock = DockStyle.Fill
                    };

                    host.Controls.Add(panel);
                    host.Show();
                    Application.DoEvents();

                    var store = new ClipboardHistoryStore();
                    panel.AttachStore(store);

                    using var wide = new Bitmap(320, 80);
                    using var tall = new Bitmap(80, 320);
                    using (var g = Graphics.FromImage(wide))
                    {
                        g.Clear(Color.DarkOrange);
                    }

                    using (var g = Graphics.FromImage(tall))
                    {
                        g.Clear(Color.CadetBlue);
                    }

                    var first = store.AddObservedImage(wide);
                    var second = store.AddObservedImage(tall);
                    store.AddObservedText("text item");

                    Application.DoEvents();

                    var buttonsField = typeof(ClipboardHistoryPanel).GetField("buttons", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(buttonsField);
                    var buttons = (System.Collections.IDictionary?)buttonsField!.GetValue(panel);
                    Assert.NotNull(buttons);

                    Size firstSizeBefore = GetButtonSize(buttons!, first.Id);
                    Size secondSizeBefore = GetButtonSize(buttons!, second.Id);

                    store.Activate(first);
                    Application.DoEvents();
                    store.Activate(second);
                    Application.DoEvents();

                    Size firstSizeAfter = GetButtonSize(buttons!, first.Id);
                    Size secondSizeAfter = GetButtonSize(buttons!, second.Id);

                    Assert.Equal(firstSizeBefore, firstSizeAfter);
                    Assert.Equal(secondSizeBefore, secondSizeAfter);
                    Assert.Equal(firstSizeAfter.Width, secondSizeAfter.Width);
                    Assert.True(firstSizeAfter.Height < secondSizeAfter.Height);
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
        public void ThumbnailSizing_DoesNotChange_WhenScrollbarAppears()
        {
            Exception? failure = null;

            RunInSta(() =>
            {
                try
                {
                    using var hostWithoutScroll = CreatePanelHost(out var panelWithoutScroll);
                    using var hostWithScroll = CreatePanelHost(out var panelWithScroll);

                    using var wide = new Bitmap(320, 80);
                    using var filler = new Bitmap(80, 320);
                    using (var g = Graphics.FromImage(wide))
                    {
                        g.Clear(Color.DarkOrange);
                    }

                    using (var g = Graphics.FromImage(filler))
                    {
                        g.Clear(Color.CadetBlue);
                    }

                    var storeWithoutScroll = new ClipboardHistoryStore();
                    panelWithoutScroll.AttachStore(storeWithoutScroll);
                    var singleWide = storeWithoutScroll.AddObservedImage(wide);

                    var storeWithScroll = new ClipboardHistoryStore();
                    panelWithScroll.AttachStore(storeWithScroll);
                    var wideAmongOverflow = storeWithScroll.AddObservedImage(wide);
                    for (int i = 0; i < 8; i++)
                    {
                        storeWithScroll.AddObservedImage(filler);
                    }

                    Application.DoEvents();

                    var sizeWithoutScroll = panelWithoutScroll.GetItemButtonSizeForDiagnostics(singleWide.Id);
                    var sizeWithScroll = panelWithScroll.GetItemButtonSizeForDiagnostics(wideAmongOverflow.Id);

                    Assert.Equal(sizeWithoutScroll, sizeWithScroll);
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

        private static Size GetButtonSize(System.Collections.IDictionary buttons, Guid id)
        {
            var button = (Control?)buttons[id];
            Assert.NotNull(button);
            return button!.Size;
        }

        private static Form CreatePanelHost(out ClipboardHistoryPanel panel)
        {
            var host = new Form
            {
                ClientSize = new Size(93, 180),
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000)
            };

            panel = new ClipboardHistoryPanel
            {
                Dock = DockStyle.Fill
            };

            host.Controls.Add(panel);
            host.Show();
            Application.DoEvents();
            return host;
        }

        private static void RunInSta(ThreadStart action)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
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

        private sealed class StubTextPresenter : IClipboardDocumentPresenter
        {
            private readonly System.Windows.Forms.Panel view = new System.Windows.Forms.Panel();

            public string CurrentText { get; set; } = string.Empty;

            public System.Windows.Forms.Control View => view;

            public string DisplayName => "StubText";

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

            public void OnActivated()
            {
            }

            public void OnDeactivated()
            {
            }

            public bool CanPresent(ClipboardHistoryItem item)
            {
                return item.Kind == ClipboardItemKind.Text;
            }

            public void LoadHistoryItem(ClipboardHistoryItem item)
            {
                CurrentText = item.CurrentText ?? string.Empty;
            }

            public void StashHistoryItemState(ClipboardHistoryItem item)
            {
                item.UpdateCurrentText(CurrentText);
            }

            public object? GetCurrentContent()
            {
                return CurrentText;
            }

            public void Dispose()
            {
                view.Dispose();
            }
        }
    }
}
