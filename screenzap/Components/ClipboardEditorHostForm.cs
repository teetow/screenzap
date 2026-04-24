using FontAwesome.Sharp;
using screenzap.Components.Shared;
using screenzap.lib;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace screenzap.Components
{
    internal sealed class ClipboardEditorHostForm : Form
    {
        private readonly Dictionary<EditorCommandId, IconToolStripButton> commandButtons = new();
        private readonly List<IClipboardDocumentPresenter> presenters = new();
        private readonly ToolStrip toolbar;
        private readonly ToolStripLabel reloadIndicatorLabel;
        private readonly ToolStripLabel dirtyIndicatorLabel;
        private readonly Panel presenterHostPanel;
        private readonly StatusStrip statusStrip;
        private readonly ToolStripStatusLabel statusLabel;
        private readonly Splitter historySplitter;
        private readonly EditorHostServices hostServices;
        private readonly ClipboardHistoryStore historyStore;
        private readonly ClipboardHistoryPersistence historyPersistence;
        private readonly ClipboardHistoryPanel historyPanel;
        private IClipboardDocumentPresenter? activePresenter;
        private bool hasPendingReloadIndicator;
        private DateTime? suppressExternalClipboardUntilUtc;
        private Guid? pendingCommittedItemId;
        private DateTime? pendingCommittedItemUntilUtc;
        internal bool SuppressActivation { get; set; }
        internal Func<ClipboardHistoryItem, Task<bool>>? TryDeleteFromSystemHistoryAsync { get; set; }
        /// <summary>When true, a clipboard event arriving via the store won't override the currently-active dirty item.</summary>
        internal bool IsHostVisibleForAutoSwitch => Visible && !SuppressActivation;
        internal ClipboardHistoryStore HistoryStore => historyStore;

        private const int InternalClipboardWriteSuppressMs = 2000;
        private const int PendingCommittedItemMatchMs = 10000;
        private const int HistoryPanelMinWidth = 72;
        private const int HistoryPanelDefaultWidth = 93;

        internal void BeginInternalClipboardWrite()
        {
            suppressExternalClipboardUntilUtc = DateTime.UtcNow.AddMilliseconds(InternalClipboardWriteSuppressMs);
        }

        /// <summary>True when a recent internal clipboard write should suppress inbound history observation.</summary>
        internal bool IsInternalClipboardWriteWindow()
        {
            return suppressExternalClipboardUntilUtc.HasValue && DateTime.UtcNow < suppressExternalClipboardUntilUtc.Value;
        }

        public ClipboardEditorHostForm(IEnumerable<IClipboardDocumentPresenter>? presentersToHost)
        {
            toolbar = CreateToolbar();
            reloadIndicatorLabel = CreateReloadIndicatorLabel();
            dirtyIndicatorLabel = CreateDirtyIndicatorLabel();
            presenterHostPanel = CreatePresenterHostPanel();
            statusStrip = CreateStatusStrip(out statusLabel);
            historyStore = new ClipboardHistoryStore();
            historyPersistence = new ClipboardHistoryPersistence();
            RestorePersistedHistory();
            historyPanel = new ClipboardHistoryPanel();
            historyPanel.Width = HistoryPanelDefaultWidth;
            historyPanel.MinimumSize = new Size(HistoryPanelMinWidth, 0);
            historySplitter = CreateHistorySplitter();
            historyPanel.AttachStore(historyStore);
            historyPanel.ItemActivated += OnHistoryItemActivated;
            historyPanel.ItemSetActive  += (_, item) => SetItemAsClipboard(item);
            historyPanel.ItemDuplicate  += (_, item) => DuplicateItem(item);
            historyPanel.ItemRevert     += (_, item) => RevertItem(item);
            historyPanel.ItemDelete     += (_, item) => _ = DeleteItemAsync(item);
            historyStore.ItemUpdated += OnStoreItemUpdated;
            historyStore.Changed += OnStoreChanged;

            InitializeComponent();
            ApplyPersistedHistoryPanelWidth();

            hostServices = new EditorHostServices
            {
                SetReloadIndicator = UpdateReloadIndicator,
                RequestClipboardReload = () => ExecuteCommand(EditorCommandId.Reload),
                UpdateStatusText = UpdateStatusText,
                FocusHost = FocusHostWindow,
                ActivatePresenter = ActivatePresenter,
                NotifyContentEdited = OnActivePresenterContentEdited
            };

            if (presentersToHost != null)
            {
                foreach (var presenter in presentersToHost)
                {
                    AddPresenter(presenter);
                }
            }

            Application.Idle += OnApplicationIdle;
            FormClosed += OnHostFormClosed;

            WindowLayoutHelper.ApplyInitialGeometry(this);
            if (presenters.FirstOrDefault() is IClipboardDocumentPresenter firstPresenter)
            {
                ActivatePresenter(firstPresenter);
            }
        }

        public ClipboardEditorHostForm(params IClipboardDocumentPresenter[] presenters)
            : this((presenters ?? Array.Empty<IClipboardDocumentPresenter>()).AsEnumerable())
        {
        }

        public IClipboardDocumentPresenter? ActivePresenter => activePresenter;
        internal bool HasPendingReloadIndicator => hasPendingReloadIndicator;
        internal string? CurrentStatusText => statusLabel.Text;

        public void AddPresenter(IClipboardDocumentPresenter presenter)
        {
            if (presenter == null)
            {
                return;
            }

            if (presenters.Contains(presenter))
            {
                presenter.AttachHostServices(hostServices);
                return;
            }

            presenters.Add(presenter);
            presenter.AttachHostServices(hostServices);
            var view = presenter.View;
            if (view.Parent is Control parent)
            {
                parent.Controls.Remove(view);
            }

            view.Dock = DockStyle.Fill;
            view.Visible = false;
        }

        public bool TryShowClipboardData(IDataObject? dataObject)
        {
            if (dataObject == null)
            {
                return false;
            }

            var presenter = presenters.FirstOrDefault(p => p.CanHandleClipboard(dataObject));
            if (presenter == null)
            {
                return false;
            }

            ActivatePresenter(presenter);
            presenter.LoadFromClipboard(dataObject);
            FocusHostWindow();
            return true;
        }

        internal bool ExecuteHostCommand(EditorCommandId commandId)
        {
            var result = ExecuteCommand(commandId);
            UpdateCommandStates();
            return result;
        }

        internal bool CanExecuteHostCommand(EditorCommandId commandId)
        {
            return activePresenter?.CanExecute(commandId) == true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Application.Idle -= OnApplicationIdle;
                FormClosed -= OnHostFormClosed;
                foreach (var presenter in presenters)
                {
                    presenter.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            SuspendLayout();

            toolbar.Dock = DockStyle.Top;
            toolbar.GripStyle = ToolStripGripStyle.Hidden;
            toolbar.ImageScalingSize = new Size(20, 20);

            AddCommandButton(EditorCommandId.Save);
            AddCommandButton(EditorCommandId.SaveAs);
            toolbar.Items.Add(new ToolStripSeparator());
            AddCommandButton(EditorCommandId.Copy);
            AddCommandButton(EditorCommandId.Reload);
            toolbar.Items.Add(reloadIndicatorLabel);
            toolbar.Items.Add(new ToolStripSeparator());
            AddCommandButton(EditorCommandId.Undo);
            AddCommandButton(EditorCommandId.Redo);
            toolbar.Items.Add(new ToolStripSeparator());
            AddCommandButton(EditorCommandId.Find);
            toolbar.Items.Add(new ToolStripSeparator());
            AddCommandButton(EditorCommandId.CommitEdits);
            AddCommandButton(EditorCommandId.Duplicate);
            AddCommandButton(EditorCommandId.Revert);
            toolbar.Items.Add(new ToolStripSeparator());
            AddCommandButton(EditorCommandId.Delete);
            toolbar.Items.Add(dirtyIndicatorLabel);

            presenterHostPanel.Dock = DockStyle.Fill;
            presenterHostPanel.BackColor = SystemColors.ControlDarkDark;

            historyPanel.Dock = DockStyle.Right;
            historySplitter.Dock = DockStyle.Right;

            statusStrip.Dock = DockStyle.Bottom;
            statusStrip.Items.Add(statusLabel);

            Controls.Add(presenterHostPanel);
            Controls.Add(historySplitter);
            Controls.Add(historyPanel);
            Controls.Add(statusStrip);
            Controls.Add(toolbar);

            KeyPreview = true;
            DoubleBuffered = true;
            MinimumSize = new Size(900, 600);
            ClientSize = new Size(1100, 700);
            Text = "Screenzap Clipboard Editor";
            Resize += (_, _) => ClampHistoryPanelWidth();

            ResumeLayout(false);
            PerformLayout();
        }

        private ToolStrip CreateToolbar()
        {
            return new ToolStrip();
        }

        private ToolStripLabel CreateReloadIndicatorLabel()
        {
            return new ToolStripLabel
            {
                Text = "●",
                ForeColor = Color.OrangeRed,
                Visible = false,
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
                ToolTipText = "Clipboard content changed",
                Margin = new Padding(-10, 0, 4, 0),
                AutoSize = false,
                Width = 14,
                TextAlign = ContentAlignment.MiddleCenter
            };
        }

        private ToolStripLabel CreateDirtyIndicatorLabel()
        {
            return new ToolStripLabel
            {
                Text = "• Edited",
                ForeColor = Color.OrangeRed,
                Visible = false,
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
                ToolTipText = "This item has unsaved edits",
                Margin = new Padding(8, 0, 4, 0),
                AutoSize = true
            };
        }

        private Panel CreatePresenterHostPanel()
        {
            return new Panel();
        }

        private StatusStrip CreateStatusStrip(out ToolStripStatusLabel label)
        {
            label = new ToolStripStatusLabel
            {
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft
            };

            return new StatusStrip
            {
                SizingGrip = false
            };
        }

        private Splitter CreateHistorySplitter()
        {
            var splitter = new Splitter
            {
                Width = 6,
                MinSize = HistoryPanelMinWidth,
                MinExtra = 300,
                BackColor = SystemColors.ControlDark,
                Cursor = Cursors.VSplit,
                TabStop = false
            };

            splitter.SplitterMoved += (_, _) =>
            {
                ClampHistoryPanelWidth();
                SaveHistoryPanelWidth();
            };
            return splitter;
        }

        private void ClampHistoryPanelWidth()
        {
            historyPanel.Width = ClampHistoryPanelWidthValue(historyPanel.Width);
        }

        private int ClampHistoryPanelWidthValue(int proposedWidth)
        {
            int min = HistoryPanelMinWidth;
            int max = Math.Max(min, ClientSize.Width - 300);
            return Math.Min(Math.Max(proposedWidth, min), max);
        }

        private void ApplyPersistedHistoryPanelWidth()
        {
            int persisted = Properties.Settings.Default.clipboardHistoryPanelWidth;
            if (persisted <= 0)
            {
                persisted = HistoryPanelDefaultWidth;
            }

            historyPanel.Width = ClampHistoryPanelWidthValue(persisted);
        }

        private void SaveHistoryPanelWidth()
        {
            int clamped = ClampHistoryPanelWidthValue(historyPanel.Width);
            if (Properties.Settings.Default.clipboardHistoryPanelWidth == clamped)
            {
                return;
            }

            Properties.Settings.Default.clipboardHistoryPanelWidth = clamped;
            Properties.Settings.Default.Save();
        }

        private void AddCommandButton(EditorCommandId commandId)
        {
            if (!EditorCommandCatalog.All.TryGetValue(commandId, out var descriptor))
            {
                return;
            }

            var button = new IconToolStripButton
            {
                Tag = descriptor.Id,
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                IconChar = descriptor.Icon,
                IconColor = SystemColors.ControlText,
                IconFont = IconFont.Auto,
                IconSize = 18,
                ToolTipText = descriptor.ToolTip,
                AutoSize = false,
                Width = 32,
                Height = 32,
                Margin = new Padding(1)
            };

            button.Click += OnCommandButtonClick;
            toolbar.Items.Add(button);
            commandButtons[descriptor.Id] = button;
        }

        private void OnCommandButtonClick(object? sender, EventArgs e)
        {
            if (sender is ToolStripItem item && item.Tag is EditorCommandId commandId)
            {
                ExecuteCommand(commandId);
            }
        }

        private bool ExecuteCommand(EditorCommandId commandId)
        {
            switch (commandId)
            {
                case EditorCommandId.CommitEdits:
                    return CommitActiveItemEdits();
                case EditorCommandId.Duplicate:
                    return DuplicateActiveItem();
                case EditorCommandId.Revert:
                    return RevertActiveItem();
                case EditorCommandId.Delete:
                    var active = historyStore.ActiveItem;
                    if (active == null) return false;
                    _ = DeleteItemAsync(active);
                    return true;
                default:
                    return activePresenter?.TryExecute(commandId) == true;
            }
        }

        private void UpdateCommandStates()
        {
            var activeItem = historyStore.ActiveItem;
            foreach (var pair in commandButtons)
            {
                switch (pair.Key)
                {
                    case EditorCommandId.CommitEdits:
                        pair.Value.Enabled = activeItem?.IsDirty == true;
                        break;
                    case EditorCommandId.Revert:
                        pair.Value.Enabled = activeItem?.CanRevertToOriginal == true;
                        break;
                    case EditorCommandId.Duplicate:
                        pair.Value.Enabled = activeItem != null;
                        break;
                    case EditorCommandId.Delete:
                        pair.Value.Enabled = activeItem != null;
                        break;
                    default:
                        pair.Value.Enabled = activePresenter?.CanExecute(pair.Key) == true;
                        break;
                }
            }

            dirtyIndicatorLabel.Visible = activeItem?.IsDirty == true;
        }

        private bool CommitActiveItemEdits()
        {
            var item = historyStore.ActiveItem;
            if (item == null || !item.IsDirty) return false;

            // Stash first so Annotations + base image are captured. Then flatten for clipboard.
            activePresenter?.StashHistoryItemState(item);

            Bitmap? flattened = null;
            string? flattenedText = null;
            if (item.Kind == ClipboardItemKind.Image && activePresenter?.GetCurrentContent() is Bitmap bmp)
            {
                flattened = bmp;
            }
            else if (item.Kind == ClipboardItemKind.Text)
            {
                flattenedText = item.CurrentText ?? string.Empty;
            }

            // Mark internal write so the system-history observer won't create a duplicate entry.
            BeginInternalClipboardWrite();
            try
            {
                if (flattened != null)
                {
                    Clipboard.SetImage(flattened);
                }
                else if (flattenedText != null)
                {
                    Clipboard.SetText(flattenedText);
                }
            }
            catch (Exception ex)
            {
                screenzap.lib.Logger.Log($"CommitActiveItemEdits clipboard write failed: {ex.Message}");
            }

            // Bake the flattened state into the item as the new baseline; annotations are consumed.
            if (flattened != null)
            {
                item.UpdateCurrentImage(flattened);
                flattened.Dispose();
            }
            else if (flattenedText != null)
            {
                item.UpdateCurrentText(flattenedText);
            }

            if (!string.IsNullOrEmpty(item.SystemHistoryId))
            {
                item.AddSuppressedSystemHistoryId(item.SystemHistoryId);
                item.SystemHistoryId = null;
            }

            historyStore.MarkClean(item);
            TrackPendingCommittedItem(item.Id);

            // Reload cleaned state into presenter; undo is intentionally flattened on commit.
            activePresenter?.LoadHistoryItem(item);
            UpdateCommandStates();
            UpdateStatusText("Edits committed to clipboard.");
            return true;
        }

        private bool DuplicateActiveItem()
        {
            var item = historyStore.ActiveItem;
            if (item == null) return false;
            // Capture latest presenter content into the source before cloning.
            activePresenter?.StashHistoryItemState(item);
            var clone = historyStore.Duplicate(item);
            ActivateHistoryItem(clone);
            UpdateStatusText("Duplicated to new history entry.");
            return true;
        }

        private bool RevertActiveItem()
        {
            var item = historyStore.ActiveItem;
            if (item == null || !item.CanRevertToOriginal) return false;
            historyStore.Revert(item);
            activePresenter?.LoadHistoryItem(item);
            UpdateCommandStates();
            UpdateStatusText("Reverted to original.");
            return true;
        }

        /// <summary>
        /// Called by <see cref="EditorHostServices.NotifyContentEdited"/> when the active presenter dirties its content.
        /// We update the item's preview composite (for thumbnails) and flag it dirty without flattening the base image.
        /// </summary>
        private void OnActivePresenterContentEdited()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(OnActivePresenterContentEdited));
                return;
            }

            var item = historyStore.ActiveItem;
            if (item == null || activePresenter == null) return;

            var content = activePresenter.GetCurrentContent();
            if (content is Bitmap bmp)
            {
                using (bmp)
                {
                    // For images, treat presenter output as a preview composite only; base image lives in
                    // the presenter until StashHistoryItemState flushes it. We still mark dirty + refresh thumb.
                    item.SetPreviewComposite(bmp);
                }
                item.MarkDirtyExternally();
                historyStore.NotifyItemUpdated(item);
            }
            else if (content is string text)
            {
                historyStore.NotifyTextEdited(item, text);
            }

            UpdateCommandStates();
        }

        private void OnStoreItemUpdated(object? sender, ClipboardHistoryItem e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<object?, ClipboardHistoryItem>(OnStoreItemUpdated), sender, e);
                return;
            }
            UpdateCommandStates();
            SavePersistedHistory();
        }

        private void OnStoreChanged(object? sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<object?, EventArgs>(OnStoreChanged), sender, e);
                return;
            }

            SavePersistedHistory();
        }

        private void OnHistoryItemActivated(object? sender, ClipboardHistoryItem item)
        {
            ActivateHistoryItem(item);
        }

        private void SetItemAsClipboard(ClipboardHistoryItem item)
        {
            // Activate in the editor first, then write to the system clipboard.
            ActivateHistoryItem(item);

            BeginInternalClipboardWrite();
            try
            {
                if (item.Kind == ClipboardItemKind.Image && item.CurrentImage != null)
                    Clipboard.SetImage(item.CurrentImage);
                else if (item.Kind == ClipboardItemKind.Text && item.CurrentText != null)
                    Clipboard.SetText(item.CurrentText);
            }
            catch (Exception ex)
            {
                screenzap.lib.Logger.Log($"SetItemAsClipboard failed: {ex.Message}");
            }

            UpdateStatusText("Set as active clipboard content.");
        }

        private void DuplicateItem(ClipboardHistoryItem item)
        {
            activePresenter?.StashHistoryItemState(item);
            var clone = historyStore.DuplicateAbove(item);
            ActivateHistoryItem(clone);
            UpdateStatusText("Duplicated.");
        }

        private void RevertItem(ClipboardHistoryItem item)
        {
            if (!item.CanRevertToOriginal)
            {
                return;
            }

            historyStore.Revert(item);
            if (ReferenceEquals(historyStore.ActiveItem, item))
                activePresenter?.LoadHistoryItem(item);
            UpdateCommandStates();
            UpdateStatusText("Reverted to original.");
        }

        private async Task DeleteItemAsync(ClipboardHistoryItem item)
        {
            if (!string.IsNullOrEmpty(item.SystemHistoryId))
            {
                bool removedFromSystem = TryDeleteFromSystemHistoryAsync != null
                    && await TryDeleteFromSystemHistoryAsync(item);
                if (!removedFromSystem)
                {
                    UpdateStatusText("Could not delete this Windows clipboard history item.");
                    return;
                }
            }

            bool wasActive = ReferenceEquals(historyStore.ActiveItem, item);
            var items = historyStore.Items;
            int idx = -1;
            for (int i = 0; i < items.Count; i++)
            {
                if (ReferenceEquals(items[i], item)) { idx = i; break; }
            }

            historyStore.Remove(item);

            if (wasActive)
            {
                var remaining = historyStore.Items;
                var next = remaining.Count > 0
                    ? remaining[Math.Min(idx, remaining.Count - 1)]
                    : null;
                if (next != null)
                    ActivateHistoryItem(next);
                else
                    UpdateCommandStates();
            }

            UpdateStatusText("Deleted from history.");
        }

        /// <summary>
        /// Activates the given history item: stashes outgoing state, loads content into the matching presenter.
        /// </summary>
        internal bool ActivateHistoryItem(ClipboardHistoryItem item)
        {
            if (item == null) return false;

            // Stash current active item state first.
            var outgoing = historyStore.ActiveItem;
            if (outgoing != null && outgoing.Id != item.Id)
            {
                activePresenter?.StashHistoryItemState(outgoing);
            }

            // Find a presenter that can handle this kind.
            var presenter = presenters.FirstOrDefault(p => p.CanPresent(item));
            if (presenter == null) return false;

            historyStore.Activate(item);
            ActivatePresenter(presenter);
            presenter.LoadHistoryItem(item);
            UpdateCommandStates();
            return true;
        }

        /// <summary>
        /// Applies the auto-switch rule for a newly-observed clipboard item.
        /// Rule: if host is visible AND the current active item is dirty, keep the current item active.
        /// Otherwise, activate the new item.
        /// </summary>
        internal void OnObservedClipboardItem(ClipboardHistoryItem newItem)
        {
            if (newItem == null) return;

            var activeItem = historyStore.ActiveItem;
            bool hostVisible = Visible;
            bool shouldPreserve = hostVisible && activeItem?.IsDirty == true;

            if (shouldPreserve)
            {
                // Keep the dirty item active; the new one is still in the list.
                UpdateCommandStates();
                return;
            }

            ActivateHistoryItem(newItem);
        }

        private void UpdateWindowTitle()
        {
            var suffix = activePresenter?.DisplayName;
            Text = string.IsNullOrWhiteSpace(suffix)
                ? "Screenzap Clipboard Editor"
                : $"Screenzap Clipboard Editor - {suffix}";
        }

        private void UpdateReloadIndicator(bool hasPendingReload)
        {
            this.hasPendingReloadIndicator = hasPendingReload;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<bool>(UpdateReloadIndicator), hasPendingReload);
                return;
            }

            reloadIndicatorLabel.Visible = hasPendingReload;
            reloadIndicatorLabel.Text = hasPendingReload ? "●" : string.Empty;
        }

        private void UpdateStatusText(string? text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string?>(UpdateStatusText), text);
                return;
            }

            statusLabel.Text = text ?? string.Empty;
        }

        private void FocusHostWindow()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(FocusHostWindow));
                return;
            }

            if (SuppressActivation)
            {
                FocusActivePresenter();
                return;
            }

            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            if (!Visible)
            {
                Show();
            }

            Activate();
            FocusActivePresenter();
        }

        private bool ActivatePresenter(IClipboardDocumentPresenter presenter)
        {
            if (presenter == null)
            {
                return false;
            }

            if (InvokeRequired)
            {
                return (bool)Invoke(new Func<IClipboardDocumentPresenter, bool>(ActivatePresenter), presenter);
            }

            if (!presenters.Contains(presenter))
            {
                AddPresenter(presenter);
            }

            if (activePresenter == presenter)
            {
                presenter.OnActivated();
                FocusActivePresenter();
                return true;
            }

            activePresenter?.OnDeactivated();

            activePresenter = presenter;

            var view = presenter.View;
            if (view.Parent != presenterHostPanel)
            {
                if (view.Parent is Control parent)
                {
                    parent.Controls.Remove(view);
                }

                presenterHostPanel.Controls.Clear();
                view.Dock = DockStyle.Fill;
                presenterHostPanel.Controls.Add(view);
            }

            view.Visible = true;
            view.BringToFront();
            presenter.OnActivated();
            FocusActivePresenter();
            UpdateWindowTitle();
            UpdateCommandStates();
            return true;
        }

        private void FocusActivePresenter()
        {
            if (activePresenter?.View is Control control)
            {
                control.Focus();
            }
        }

        private void OnApplicationIdle(object? sender, EventArgs e)
        {
            UpdateCommandStates();
        }

        private void OnHostFormClosed(object? sender, FormClosedEventArgs e)
        {
            Application.Idle -= OnApplicationIdle;
            SaveHistoryPanelWidth();
            SavePersistedHistory();
        }

        internal ClipboardHistoryItem? TryBindPendingCommittedSystemItem(ClipboardHistoryItem incomingSystemItem)
        {
            if (pendingCommittedItemId == null || pendingCommittedItemUntilUtc == null)
            {
                return null;
            }

            if (DateTime.UtcNow > pendingCommittedItemUntilUtc.Value)
            {
                pendingCommittedItemId = null;
                pendingCommittedItemUntilUtc = null;
                return null;
            }

            var localItem = historyStore.FindById(pendingCommittedItemId.Value);
            if (localItem == null || !localItem.ContentMatches(incomingSystemItem))
            {
                return null;
            }

            localItem.AssignSystemHistoryId(incomingSystemItem.SystemHistoryId);
            historyStore.NotifyItemUpdated(localItem);

            pendingCommittedItemId = null;
            pendingCommittedItemUntilUtc = null;
            return localItem;
        }

        private void TrackPendingCommittedItem(Guid itemId)
        {
            pendingCommittedItemId = itemId;
            pendingCommittedItemUntilUtc = DateTime.UtcNow.AddMilliseconds(PendingCommittedItemMatchMs);
        }

        private void RestorePersistedHistory()
        {
            var restored = historyPersistence.Load();
            if (restored.Items.Count == 0)
            {
                return;
            }

            historyStore.LoadPersisted(restored.Items, restored.ActiveItemId);
        }

        private void SavePersistedHistory()
        {
            historyPersistence.Save(historyStore.Items, historyStore.ActiveItem);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Ctrl+PageUp: move to previous history item
            if (keyData == (Keys.Control | Keys.PageUp))
            {
                return NavigateHistoryPrevious();
            }

            // Ctrl+PageDown: move to next history item
            if (keyData == (Keys.Control | Keys.PageDown))
            {
                return NavigateHistoryNext();
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private bool NavigateHistoryPrevious()
        {
            var activeItem = historyStore.ActiveItem;
            var items = historyStore.Items;

            if (items.Count == 0) return false;

            // Find the current index
            int currentIndex = -1;
            for (int i = 0; i < items.Count; i++)
            {
                if (ReferenceEquals(items[i], activeItem))
                {
                    currentIndex = i;
                    break;
                }
            }

            // If no active item or at the beginning, go to the last item
            int nextIndex = currentIndex <= 0 ? items.Count - 1 : currentIndex - 1;

            return ActivateHistoryItem(items[nextIndex]);
        }

        private bool NavigateHistoryNext()
        {
            var activeItem = historyStore.ActiveItem;
            var items = historyStore.Items;

            if (items.Count == 0) return false;

            // Find the current index
            int currentIndex = -1;
            for (int i = 0; i < items.Count; i++)
            {
                if (ReferenceEquals(items[i], activeItem))
                {
                    currentIndex = i;
                    break;
                }
            }

            // If no active item or at the end, go to the first item
            int nextIndex = currentIndex < 0 || currentIndex >= items.Count - 1 ? 0 : currentIndex + 1;

            return ActivateHistoryItem(items[nextIndex]);
        }
    }
}
