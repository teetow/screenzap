using FontAwesome.Sharp;
using screenzap.Components.Shared;
using screenzap.lib;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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
        private readonly EditorHostServices hostServices;
        private readonly ClipboardHistoryStore historyStore;
        private readonly ClipboardHistoryPanel historyPanel;
        private IClipboardDocumentPresenter? activePresenter;
        private bool hasPendingReloadIndicator;
        internal bool SuppressActivation { get; set; }
        /// <summary>When true, a clipboard event arriving via the store won't override the currently-active dirty item.</summary>
        internal bool IsHostVisibleForAutoSwitch => Visible && !SuppressActivation;
        internal ClipboardHistoryStore HistoryStore => historyStore;

        public ClipboardEditorHostForm(IEnumerable<IClipboardDocumentPresenter>? presentersToHost)
        {
            toolbar = CreateToolbar();
            reloadIndicatorLabel = CreateReloadIndicatorLabel();
            dirtyIndicatorLabel = CreateDirtyIndicatorLabel();
            presenterHostPanel = CreatePresenterHostPanel();
            statusStrip = CreateStatusStrip(out statusLabel);
            historyStore = new ClipboardHistoryStore();
            historyPanel = new ClipboardHistoryPanel();
            historyPanel.AttachStore(historyStore);
            historyPanel.ItemActivated += OnHistoryItemActivated;
            historyStore.ItemUpdated += OnStoreItemUpdated;

            InitializeComponent();

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
            toolbar.Items.Add(dirtyIndicatorLabel);

            presenterHostPanel.Dock = DockStyle.Fill;
            presenterHostPanel.BackColor = SystemColors.ControlDarkDark;

            historyPanel.Dock = DockStyle.Right;

            statusStrip.Dock = DockStyle.Bottom;
            statusStrip.Items.Add(statusLabel);

            Controls.Add(presenterHostPanel);
            Controls.Add(historyPanel);
            Controls.Add(statusStrip);
            Controls.Add(toolbar);

            KeyPreview = true;
            DoubleBuffered = true;
            MinimumSize = new Size(900, 600);
            ClientSize = new Size(1100, 700);
            Text = "Screenzap Clipboard Editor";

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
                        pair.Value.Enabled = activeItem?.IsDirty == true;
                        break;
                    case EditorCommandId.Duplicate:
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

            // Push the current content to the Windows clipboard, then mark the item clean.
            try
            {
                if (item.Kind == ClipboardItemKind.Image && activePresenter?.GetCurrentContent() is Bitmap bmp)
                {
                    using (bmp)
                    {
                        Clipboard.SetImage(bmp);
                    }
                }
                else if (item.Kind == ClipboardItemKind.Text && activePresenter?.GetCurrentContent() is string text)
                {
                    Clipboard.SetText(text ?? string.Empty);
                }
            }
            catch (Exception ex)
            {
                screenzap.lib.Logger.Log($"CommitActiveItemEdits clipboard write failed: {ex.Message}");
            }

            // Persist current presenter state back into the item BEFORE marking clean so MarkClean baselines the right content.
            activePresenter?.StashHistoryItemState(item);
            historyStore.MarkClean(item);
            // Restore the stashed undo snapshot so the user can still undo edits they just committed.
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
            if (item == null || !item.IsDirty) return false;
            historyStore.Revert(item);
            activePresenter?.LoadHistoryItem(item);
            UpdateCommandStates();
            UpdateStatusText("Reverted to original.");
            return true;
        }

        /// <summary>
        /// Called by <see cref="EditorHostServices.NotifyContentEdited"/> when the active presenter dirties its content.
        /// We lazily push the current content into the store's active item to keep thumbnails + dirty flag accurate.
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
                    historyStore.NotifyImageEdited(item, bmp);
                }
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
        }

        private void OnHistoryItemActivated(object? sender, ClipboardHistoryItem item)
        {
            ActivateHistoryItem(item);
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
        }
    }
}
