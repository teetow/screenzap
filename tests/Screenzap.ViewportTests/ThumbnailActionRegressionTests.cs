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
                    using var presenter = new StubImagePresenter();
                    using var host = new ClipboardEditorHostForm(true, presenter)
                    {
                        SuppressActivation = true,
                        ShowInTaskbar = false
                    };

                    host.CreateControl();
                    host.HistoryStore.ReplaceAll(Array.Empty<ClipboardHistoryItem>());

                    var first = AddImage(host.HistoryStore, Color.Red);
                    var second = AddImage(host.HistoryStore, Color.Blue);

                    Assert.True(host.ActivateHistoryItem(first));
                    presenter.CurrentColor = Color.Green; // simulate a live edit on the active presenter

                    var duplicateMethod = typeof(ClipboardEditorHostForm).GetMethod("DuplicateItem", BindingFlags.Instance | BindingFlags.NonPublic);
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
        public void SetItemAsClipboard_PrefersPreviewComposite_ForImageItems()
        {
            Exception? failure = null;

            RunInSta(() =>
            {
                try
                {
                    using var imagePresenter = new screenzap.ImageEditor();
                    using var host = new ClipboardEditorHostForm(true, imagePresenter)
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
        public void ObservedClipboardItem_DoesNotReplaceDirtyActiveItem_WhenHostIsHidden()
        {
            Exception? failure = null;

            RunInSta(() =>
            {
                try
                {
                    using var presenter = new StubImagePresenter();
                    using var host = new ClipboardEditorHostForm(true, presenter)
                    {
                        SuppressActivation = true,
                        ShowInTaskbar = false
                    };

                    host.CreateControl();
                    Assert.False(host.Visible);

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

            RunInSta(() =>
            {
                try
                {
                    using var presenter = new StubImagePresenter();
                    using var host = new ClipboardEditorHostForm(true, presenter)
                    {
                        SuppressActivation = true,
                        ShowInTaskbar = false
                    };

                    host.CreateControl();

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

            RunInSta(() =>
            {
                try
                {
                    using var presenter = new StubImagePresenter();
                    using var host = new ClipboardEditorHostForm(true, presenter)
                    {
                        SuppressActivation = true,
                        ShowInTaskbar = false
                    };

                    host.CreateControl();

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

            RunInSta(() =>
            {
                try
                {
                    using var presenter = new StubImagePresenter();
                    using var host = new ClipboardEditorHostForm(true, presenter)
                    {
                        SuppressActivation = true,
                        ShowInTaskbar = false
                    };

                    host.CreateControl();
                    host.HistoryStore.ReplaceAll(Array.Empty<ClipboardHistoryItem>());

                    var first = AddImage(host.HistoryStore, Color.Red);
                    var second = AddImage(host.HistoryStore, Color.Blue);
                    var third = AddImage(host.HistoryStore, Color.Green);

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
        public void DeleteKey_OnFocusedThumbnail_RemovesOnlyThatItem()
        {
            Exception? failure = null;

            RunInSta(() =>
            {
                try
                {
                    using var presenter = new StubImagePresenter();
                    using var host = new ClipboardEditorHostForm(true, presenter)
                    {
                        SuppressActivation = true,
                        ShowInTaskbar = false
                    };

                    host.HistoryStore.ReplaceAll(Array.Empty<ClipboardHistoryItem>());

                    var first = AddImage(host.HistoryStore, Color.Red);
                    var second = AddImage(host.HistoryStore, Color.Blue);
                    var third = AddImage(host.HistoryStore, Color.Green);

                    // Create handles after the buttons exist so the focused-Delete path runs its real
                    // deferred (BeginInvoke) branch rather than the handleless fallback.
                    host.CreateControl();
                    Application.DoEvents();

                    // A non-Delete key on a focused thumbnail must not remove anything.
                    Assert.True(host.SendKeyToHistoryItemForDiagnostics(second, Keys.A));
                    Application.DoEvents();
                    Assert.Contains(host.HistoryStore.Items, i => ReferenceEquals(i, second));

                    // Delete on the focused thumbnail removes exactly that item (deferred via
                    // BeginInvoke, hence the DoEvents pump), leaving the others untouched.
                    Assert.True(host.SendKeyToHistoryItemForDiagnostics(second, Keys.Delete));
                    Application.DoEvents();

                    Assert.DoesNotContain(host.HistoryStore.Items, i => ReferenceEquals(i, second));
                    Assert.Contains(host.HistoryStore.Items, i => ReferenceEquals(i, first));
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
        public void ClickFocusesThumbnail_AndDeleteThroughFocus_RemovesItemsSerially()
        {
            Exception? failure = null;

            RunInSta(() =>
            {
                try
                {
                    using var presenter = new StubImagePresenter();
                    using var host = new ClipboardEditorHostForm(true, presenter)
                    {
                        SuppressActivation = true,
                        ShowInTaskbar = false
                    };

                    host.HistoryStore.ReplaceAll(Array.Empty<ClipboardHistoryItem>());
                    var first = AddImage(host.HistoryStore, Color.Red);
                    var second = AddImage(host.HistoryStore, Color.Blue);
                    var third = AddImage(host.HistoryStore, Color.Green);

                    host.CreateControl();
                    Application.DoEvents();

                    // A real click must leave keyboard focus on the thumbnail — that focus is the
                    // only thing that routes Delete to the list (regression: it never landed there).
                    Assert.True(host.ClickHistoryItemForDiagnostics(second));
                    Assert.Same(second, host.FocusedHistoryItemForDiagnostics);

                    // Route Delete through whatever holds focus, not a hand-picked button.
                    int index = host.HistoryStore.Items.ToList().FindIndex(i => ReferenceEquals(i, second));
                    Assert.True(host.SendKeyThroughHistoryFocusForDiagnostics(Keys.Delete));
                    Application.DoEvents();

                    Assert.DoesNotContain(host.HistoryStore.Items, i => ReferenceEquals(i, second));

                    // Selection lands on the item now occupying the deleted slot, so Delete chains.
                    var successor = host.HistoryStore.Items[Math.Min(index, host.HistoryStore.Items.Count - 1)];
                    Assert.Same(successor, host.FocusedHistoryItemForDiagnostics);
                    Assert.Same(successor, host.HistoryStore.ActiveItem);

                    Assert.True(host.SendKeyThroughHistoryFocusForDiagnostics(Keys.Delete));
                    Application.DoEvents();

                    Assert.DoesNotContain(host.HistoryStore.Items, i => ReferenceEquals(i, successor));
                    Assert.Single(host.HistoryStore.Items);
                    Assert.Same(host.HistoryStore.Items[0], host.FocusedHistoryItemForDiagnostics);
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
        public void ArrowKeys_MoveTheSelection_FocusAndActivationTogether()
        {
            Exception? failure = null;

            RunInSta(() =>
            {
                try
                {
                    using var presenter = new StubImagePresenter();
                    using var host = new ClipboardEditorHostForm(true, presenter)
                    {
                        SuppressActivation = true,
                        ShowInTaskbar = false
                    };

                    host.HistoryStore.ReplaceAll(Array.Empty<ClipboardHistoryItem>());
                    AddImage(host.HistoryStore, Color.Red);
                    AddImage(host.HistoryStore, Color.Blue);
                    AddImage(host.HistoryStore, Color.Green);

                    host.CreateControl();
                    Application.DoEvents();

                    // Items in display (store) order — flow order mirrors it.
                    var top = host.HistoryStore.Items[0];
                    var mid = host.HistoryStore.Items[1];
                    var bottom = host.HistoryStore.Items[2];

                    Assert.True(host.ClickHistoryItemForDiagnostics(top));
                    Assert.Same(top, host.FocusedHistoryItemForDiagnostics);
                    Assert.Same(top, host.HistoryStore.ActiveItem);

                    // One selection: the keyboard, the focus cue and the active (blue) item all
                    // travel together.
                    Assert.True(host.SendKeyThroughHistoryFocusForDiagnostics(Keys.Down));
                    Assert.Same(mid, host.FocusedHistoryItemForDiagnostics);
                    Assert.Same(mid, host.HistoryStore.ActiveItem);

                    Assert.True(host.SendKeyThroughHistoryFocusForDiagnostics(Keys.Down));
                    Assert.Same(bottom, host.FocusedHistoryItemForDiagnostics);
                    Assert.Same(bottom, host.HistoryStore.ActiveItem);

                    // Clamps at the bottom instead of wrapping or escaping the list.
                    Assert.True(host.SendKeyThroughHistoryFocusForDiagnostics(Keys.Down));
                    Assert.Same(bottom, host.FocusedHistoryItemForDiagnostics);
                    Assert.Same(bottom, host.HistoryStore.ActiveItem);

                    Assert.True(host.SendKeyThroughHistoryFocusForDiagnostics(Keys.Up));
                    Assert.Same(mid, host.FocusedHistoryItemForDiagnostics);
                    Assert.Same(mid, host.HistoryStore.ActiveItem);

                    Assert.True(host.SendKeyThroughHistoryFocusForDiagnostics(Keys.Home));
                    Assert.Same(top, host.FocusedHistoryItemForDiagnostics);
                    Assert.Same(top, host.HistoryStore.ActiveItem);

                    Assert.True(host.SendKeyThroughHistoryFocusForDiagnostics(Keys.End));
                    Assert.Same(bottom, host.FocusedHistoryItemForDiagnostics);
                    Assert.Same(bottom, host.HistoryStore.ActiveItem);
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
        public void ListKeepsFocus_WhenEditorGrabsItAsynchronously_OnLoad()
        {
            Exception? failure = null;

            RunInSta(() =>
            {
                try
                {
                    using var presenter = new StubImagePresenter();
                    using var host = new ClipboardEditorHostForm(true, presenter)
                    {
                        SuppressActivation = true,
                        ShowInTaskbar = false
                    };

                    host.HistoryStore.ReplaceAll(Array.Empty<ClipboardHistoryItem>());
                    AddImage(host.HistoryStore, Color.Red);
                    AddImage(host.HistoryStore, Color.Blue);
                    AddImage(host.HistoryStore, Color.Green);

                    // CreateControl is a no-op on invisible forms; the deferred (BeginInvoke) focus
                    // races need a real handle, so force one.
                    _ = host.Handle;
                    Application.DoEvents();

                    // Arm the editor-style deferred focus grab (real ImageEditor.LoadImage posts a
                    // re-center ending in pictureBox1.Focus(), which beats any synchronous reclaim).
                    var editorFocusTarget = new Button();
                    presenter.View.Controls.Add(editorFocusTarget);
                    presenter.AsyncFocusStealTarget = editorFocusTarget;

                    // Click an item that is NOT already active (activating the active item takes a
                    // short-circuit path that never loads, and so never steals), and not the last
                    // one so Down has somewhere to go.
                    var items = host.HistoryStore.Items;
                    int clickIndex = ReferenceEquals(items[0], host.HistoryStore.ActiveItem) ? 1 : 0;
                    var clickTarget = items[clickIndex];
                    var downTarget = items[clickIndex + 1];

                    // Click: even after the editor's posted grab runs, the keyboard must stay on
                    // the clicked thumbnail — this was the "have to click twice" regression.
                    Assert.True(host.ClickHistoryItemForDiagnostics(clickTarget));
                    Assert.Equal(1, presenter.StealsPosted);
                    Application.DoEvents();
                    Assert.Equal(1, presenter.StealsRun);
                    Assert.Same(clickTarget, host.FocusedHistoryItemForDiagnostics);

                    // Same for arrow navigation, which also loads the destination item.
                    Assert.True(host.SendKeyThroughHistoryFocusForDiagnostics(Keys.Down));
                    Application.DoEvents();
                    Assert.Equal(2, presenter.StealsRun);
                    Assert.Same(downTarget, host.FocusedHistoryItemForDiagnostics);
                    Assert.Same(downTarget, host.HistoryStore.ActiveItem);

                    // And for Delete, where the successor's activation loads it into the editor.
                    Assert.True(host.SendKeyThroughHistoryFocusForDiagnostics(Keys.Delete));
                    Application.DoEvents();
                    Assert.Equal(3, presenter.StealsRun);
                    Assert.DoesNotContain(host.HistoryStore.Items, i => ReferenceEquals(i, downTarget));
                    Assert.NotNull(host.FocusedHistoryItemForDiagnostics);
                    Assert.Same(host.HistoryStore.ActiveItem, host.FocusedHistoryItemForDiagnostics);
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
        public void DeleteKey_DoesNotReachHistoryList_WhenFocusIsElsewhere()
        {
            Exception? failure = null;

            RunInSta(() =>
            {
                try
                {
                    using var presenter = new StubImagePresenter();
                    using var host = new ClipboardEditorHostForm(true, presenter)
                    {
                        SuppressActivation = true,
                        ShowInTaskbar = false
                    };

                    host.HistoryStore.ReplaceAll(Array.Empty<ClipboardHistoryItem>());
                    var first = AddImage(host.HistoryStore, Color.Red);
                    var second = AddImage(host.HistoryStore, Color.Blue);

                    host.CreateControl();
                    Application.DoEvents();

                    Assert.True(host.ClickHistoryItemForDiagnostics(second));
                    Assert.Same(second, host.FocusedHistoryItemForDiagnostics);

                    // Move focus out of the history list, as a click into the editor would.
                    var editorFocusTarget = new Button();
                    presenter.View.Controls.Add(editorFocusTarget);
                    editorFocusTarget.Select();
                    Assert.Null(host.FocusedHistoryItemForDiagnostics);

                    // With focus elsewhere there is no focused thumbnail for Delete to act on.
                    Assert.False(host.SendKeyThroughHistoryFocusForDiagnostics(Keys.Delete));
                    Application.DoEvents();

                    Assert.Equal(2, host.HistoryStore.Items.Count);
                    Assert.Contains(host.HistoryStore.Items, i => ReferenceEquals(i, first));
                    Assert.Contains(host.HistoryStore.Items, i => ReferenceEquals(i, second));
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
                    using var presenter = new StubImagePresenter();
                    using var host = new ClipboardEditorHostForm(true, presenter)
                    {
                        SuppressActivation = true,
                        ShowInTaskbar = false
                    };

                    host.CreateControl();
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

                    var first = AddImage(store, Color.Red);
                    var second = AddImage(store, Color.Blue);
                    var third = AddImage(store, Color.Green);

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
                    using (var extra = MakeSolid(Color.SlateGray))
                    {
                        store.AddObservedImage(extra);
                    }

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
        /// image editor. Mirrors how the real ImageEditor round-trips an item's CurrentImage.
        /// </summary>
        private sealed class StubImagePresenter : IClipboardDocumentPresenter
        {
            private readonly System.Windows.Forms.Panel view = new System.Windows.Forms.Panel();

            public Color CurrentColor { get; set; } = Color.Empty;

            public System.Windows.Forms.Control View => view;

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

            public void OnActivated()
            {
            }

            public void OnDeactivated()
            {
            }

            public bool CanPresent(ClipboardHistoryItem item)
            {
                return item.Kind == ClipboardItemKind.Image;
            }

            /// <summary>
            /// When set, LoadHistoryItem posts a deferred focus grab on this control — mirroring
            /// the real ImageEditor, whose LoadImage posts a re-center that ends in
            /// pictureBox1.Focus() and so steals focus AFTER any synchronous reclaim.
            /// </summary>
            public System.Windows.Forms.Control? AsyncFocusStealTarget { get; set; }

            // Let tests assert the steal really happened — a steal that never runs would make any
            // "list keeps focus" assertion pass vacuously.
            public int StealsPosted { get; private set; }
            public int StealsRun { get; private set; }

            public void LoadHistoryItem(ClipboardHistoryItem item)
            {
                CurrentColor = item.CurrentImage != null ? item.CurrentImage.GetPixel(0, 0) : Color.Empty;

                if (AsyncFocusStealTarget is { } steal
                    && view.FindForm() is { IsHandleCreated: true } form)
                {
                    StealsPosted++;
                    form.BeginInvoke(new Action(() =>
                    {
                        if (!steal.IsDisposed)
                        {
                            steal.Select();
                            StealsRun++;
                        }
                    }));
                }
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

            public System.Drawing.Size? GetNaturalContentSize() => null;

            public void Dispose()
            {
                view.Dispose();
            }
        }
    }
}
