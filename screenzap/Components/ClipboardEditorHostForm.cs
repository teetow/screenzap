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
        private readonly Panel presenterHostPanel;
        private readonly StatusStrip statusStrip;
        private readonly ToolStripStatusLabel statusLabel;
        private readonly EditorHostServices hostServices;
        private IClipboardDocumentPresenter? activePresenter;
        private bool hasPendingReloadIndicator;
        internal bool SuppressActivation { get; set; }

        public ClipboardEditorHostForm(IEnumerable<IClipboardDocumentPresenter>? presentersToHost)
        {
            toolbar = CreateToolbar();
            reloadIndicatorLabel = CreateReloadIndicatorLabel();
            presenterHostPanel = CreatePresenterHostPanel();
            statusStrip = CreateStatusStrip(out statusLabel);

            InitializeComponent();

            hostServices = new EditorHostServices
            {
                SetReloadIndicator = UpdateReloadIndicator,
                RequestClipboardReload = () => ExecuteCommand(EditorCommandId.Reload),
                UpdateStatusText = UpdateStatusText,
                FocusHost = FocusHostWindow,
                ActivatePresenter = ActivatePresenter
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

            presenterHostPanel.Dock = DockStyle.Fill;
            presenterHostPanel.BackColor = SystemColors.ControlDarkDark;

            statusStrip.Dock = DockStyle.Bottom;
            statusStrip.Items.Add(statusLabel);

            Controls.Add(presenterHostPanel);
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
            return activePresenter?.TryExecute(commandId) == true;
        }

        private void UpdateCommandStates()
        {
            foreach (var pair in commandButtons)
            {
                pair.Value.Enabled = activePresenter?.CanExecute(pair.Key) == true;
            }
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
