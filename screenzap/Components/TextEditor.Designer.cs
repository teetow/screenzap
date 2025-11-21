using System.Drawing;
using System.Windows.Forms;
using ScintillaNet.WinForms;

namespace screenzap
{
    partial class TextEditor
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeThemeWatcher();
                DisposeClipboardMonitor();
                components?.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            mainToolStrip = new ToolStrip();
            saveToolStripButton = new FontAwesome.Sharp.IconToolStripButton();
            saveAsToolStripButton = new FontAwesome.Sharp.IconToolStripButton();
            copyToolStripButton = new FontAwesome.Sharp.IconToolStripButton();
            reloadToolStripButton = new FontAwesome.Sharp.IconToolStripButton();
            reloadNotificationLabel = new ToolStripLabel();
            findToolStripButton = new FontAwesome.Sharp.IconToolStripButton();
            searchPanel = new Panel();
            closeSearchPanelButton = new Button();
            replaceAllButton = new Button();
            replaceButton = new Button();
            findPreviousButton = new Button();
            findNextButton = new Button();
            regexCheckBox = new CheckBox();
            wholeWordCheckBox = new CheckBox();
            matchCaseCheckBox = new CheckBox();
            replaceLabel = new Label();
            replaceTextBox = new TextBox();
            findLabel = new Label();
            findTextBox = new TextBox();
            searchMessageLabel = new Label();
            editor = new Scintilla();
            statusStrip = new StatusStrip();
            caretStatusLabel = new ToolStripStatusLabel();
            documentStatusLabel = new ToolStripStatusLabel();
            mainToolStrip.SuspendLayout();
            searchPanel.SuspendLayout();
            statusStrip.SuspendLayout();
            SuspendLayout();
            // 
            // mainToolStrip
            // 
            mainToolStrip.ImageScalingSize = new Size(20, 20);
            mainToolStrip.Items.AddRange(new ToolStripItem[] { saveToolStripButton, saveAsToolStripButton, copyToolStripButton, reloadToolStripButton, reloadNotificationLabel, findToolStripButton });
            mainToolStrip.Location = new Point(0, 0);
            mainToolStrip.Name = "mainToolStrip";
            mainToolStrip.Padding = new Padding(1, 0, 1, 0);
            mainToolStrip.Size = new Size(944, 27);
            mainToolStrip.TabIndex = 0;
            // 
            // saveToolStripButton
            // 
            saveToolStripButton.Name = "saveToolStripButton";
            saveToolStripButton.Size = new Size(23, 24);
            saveToolStripButton.Text = "Save";
            saveToolStripButton.Click += saveToolStripButton_Click;
            // 
            // saveAsToolStripButton
            // 
            saveAsToolStripButton.Name = "saveAsToolStripButton";
            saveAsToolStripButton.Size = new Size(23, 24);
            saveAsToolStripButton.Text = "Save As";
            saveAsToolStripButton.Click += saveAsToolStripButton_Click;
            // 
            // copyToolStripButton
            // 
            copyToolStripButton.Name = "copyToolStripButton";
            copyToolStripButton.Size = new Size(23, 24);
            copyToolStripButton.Text = "Copy to Clipboard";
            copyToolStripButton.Click += copyToolStripButton_Click;
            // 
            // reloadToolStripButton
            // 
            reloadToolStripButton.Name = "reloadToolStripButton";
            reloadToolStripButton.Size = new Size(23, 24);
            reloadToolStripButton.Text = "Reload from Clipboard";
            reloadToolStripButton.Click += reloadToolStripButton_Click;
            // 
            // reloadNotificationLabel
            // 
            reloadNotificationLabel.AutoSize = false;
            reloadNotificationLabel.Font = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point);
            reloadNotificationLabel.ForeColor = Color.OrangeRed;
            reloadNotificationLabel.Margin = new Padding(-12, 0, 4, 0);
            reloadNotificationLabel.Name = "reloadNotificationLabel";
            reloadNotificationLabel.Size = new Size(14, 24);
            reloadNotificationLabel.Text = "●";
            reloadNotificationLabel.TextAlign = ContentAlignment.MiddleCenter;
            reloadNotificationLabel.Visible = false;
            // 
            // findToolStripButton
            // 
            findToolStripButton.Alignment = ToolStripItemAlignment.Right;
            findToolStripButton.Name = "findToolStripButton";
            findToolStripButton.Size = new Size(23, 24);
            findToolStripButton.Text = "Find";
            findToolStripButton.Click += findToolStripButton_Click;
            // 
            // searchPanel
            // 
            searchPanel.Controls.Add(closeSearchPanelButton);
            searchPanel.Controls.Add(replaceAllButton);
            searchPanel.Controls.Add(replaceButton);
            searchPanel.Controls.Add(findPreviousButton);
            searchPanel.Controls.Add(findNextButton);
            searchPanel.Controls.Add(regexCheckBox);
            searchPanel.Controls.Add(wholeWordCheckBox);
            searchPanel.Controls.Add(matchCaseCheckBox);
            searchPanel.Controls.Add(replaceLabel);
            searchPanel.Controls.Add(replaceTextBox);
            searchPanel.Controls.Add(findLabel);
            searchPanel.Controls.Add(findTextBox);
            searchPanel.Controls.Add(searchMessageLabel);
            searchPanel.Dock = DockStyle.Top;
            searchPanel.Location = new Point(0, 27);
            searchPanel.Name = "searchPanel";
            searchPanel.Padding = new Padding(8, 4, 8, 4);
            searchPanel.Size = new Size(944, 86);
            searchPanel.TabIndex = 1;
            searchPanel.Visible = false;
            // 
            // closeSearchPanelButton
            // 
            closeSearchPanelButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            closeSearchPanelButton.Location = new Point(907, 4);
            closeSearchPanelButton.Name = "closeSearchPanelButton";
            closeSearchPanelButton.Size = new Size(29, 27);
            closeSearchPanelButton.TabIndex = 12;
            closeSearchPanelButton.Text = "X";
            closeSearchPanelButton.UseVisualStyleBackColor = true;
            closeSearchPanelButton.Click += closeSearchPanelButton_Click;
            // 
            // replaceAllButton
            // 
            replaceAllButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            replaceAllButton.Location = new Point(781, 45);
            replaceAllButton.Name = "replaceAllButton";
            replaceAllButton.Size = new Size(110, 27);
            replaceAllButton.TabIndex = 11;
            replaceAllButton.Text = "Replace All";
            replaceAllButton.UseVisualStyleBackColor = true;
            replaceAllButton.Click += replaceAllButton_Click;
            // 
            // replaceButton
            // 
            replaceButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            replaceButton.Location = new Point(665, 45);
            replaceButton.Name = "replaceButton";
            replaceButton.Size = new Size(110, 27);
            replaceButton.TabIndex = 10;
            replaceButton.Text = "Replace";
            replaceButton.UseVisualStyleBackColor = true;
            replaceButton.Click += replaceButton_Click;
            // 
            // findPreviousButton
            // 
            findPreviousButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            findPreviousButton.Location = new Point(781, 8);
            findPreviousButton.Name = "findPreviousButton";
            findPreviousButton.Size = new Size(110, 27);
            findPreviousButton.TabIndex = 9;
            findPreviousButton.Text = "Previous";
            findPreviousButton.UseVisualStyleBackColor = true;
            findPreviousButton.Click += findPreviousButton_Click;
            // 
            // findNextButton
            // 
            findNextButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            findNextButton.Location = new Point(665, 8);
            findNextButton.Name = "findNextButton";
            findNextButton.Size = new Size(110, 27);
            findNextButton.TabIndex = 8;
            findNextButton.Text = "Next";
            findNextButton.UseVisualStyleBackColor = true;
            findNextButton.Click += findNextButton_Click;
            // 
            // regexCheckBox
            // 
            regexCheckBox.AutoSize = true;
            regexCheckBox.Location = new Point(285, 52);
            regexCheckBox.Name = "regexCheckBox";
            regexCheckBox.Size = new Size(68, 24);
            regexCheckBox.TabIndex = 7;
            regexCheckBox.Text = "Regex";
            regexCheckBox.UseVisualStyleBackColor = true;
            // 
            // wholeWordCheckBox
            // 
            wholeWordCheckBox.AutoSize = true;
            wholeWordCheckBox.Location = new Point(182, 52);
            wholeWordCheckBox.Name = "wholeWordCheckBox";
            wholeWordCheckBox.Size = new Size(102, 24);
            wholeWordCheckBox.TabIndex = 6;
            wholeWordCheckBox.Text = "Whole word";
            wholeWordCheckBox.UseVisualStyleBackColor = true;
            // 
            // matchCaseCheckBox
            // 
            matchCaseCheckBox.AutoSize = true;
            matchCaseCheckBox.Location = new Point(84, 52);
            matchCaseCheckBox.Name = "matchCaseCheckBox";
            matchCaseCheckBox.Size = new Size(99, 24);
            matchCaseCheckBox.TabIndex = 5;
            matchCaseCheckBox.Text = "Match case";
            matchCaseCheckBox.UseVisualStyleBackColor = true;
            // 
            // replaceLabel
            // 
            replaceLabel.AutoSize = true;
            replaceLabel.Location = new Point(8, 31);
            replaceLabel.Name = "replaceLabel";
            replaceLabel.Size = new Size(62, 20);
            replaceLabel.TabIndex = 3;
            replaceLabel.Text = "Replace";
            // 
            // replaceTextBox
            // 
            replaceTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            replaceTextBox.Location = new Point(84, 28);
            replaceTextBox.Name = "replaceTextBox";
            replaceTextBox.Size = new Size(567, 27);
            replaceTextBox.TabIndex = 4;
            replaceTextBox.KeyDown += replaceTextBox_KeyDown;
            // 
            // findLabel
            // 
            findLabel.AutoSize = true;
            findLabel.Location = new Point(8, 4);
            findLabel.Name = "findLabel";
            findLabel.Size = new Size(38, 20);
            findLabel.TabIndex = 1;
            findLabel.Text = "Find";
            // 
            // findTextBox
            // 
            findTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            findTextBox.Location = new Point(84, 1);
            findTextBox.Name = "findTextBox";
            findTextBox.Size = new Size(567, 27);
            findTextBox.TabIndex = 2;
            findTextBox.KeyDown += findTextBox_KeyDown;
            // 
            // searchMessageLabel
            // 
            searchMessageLabel.AutoSize = true;
            searchMessageLabel.ForeColor = Color.Firebrick;
            searchMessageLabel.Location = new Point(365, 52);
            searchMessageLabel.Name = "searchMessageLabel";
            searchMessageLabel.Size = new Size(0, 20);
            searchMessageLabel.TabIndex = 0;
            // 
            // editor
            // 
            editor.Dock = DockStyle.Fill;
            editor.Location = new Point(0, 113);
            editor.Name = "editor";
            editor.Size = new Size(944, 415);
            editor.TabIndex = 2;
            // 
            // statusStrip
            // 
            statusStrip.ImageScalingSize = new Size(20, 20);
            statusStrip.Items.AddRange(new ToolStripItem[] { caretStatusLabel, documentStatusLabel });
            statusStrip.Location = new Point(0, 528);
            statusStrip.Name = "statusStrip";
            statusStrip.Padding = new Padding(1, 0, 16, 0);
            statusStrip.Size = new Size(944, 22);
            statusStrip.TabIndex = 3;
            // 
            // caretStatusLabel
            // 
            caretStatusLabel.Name = "caretStatusLabel";
            caretStatusLabel.Size = new Size(93, 17);
            caretStatusLabel.Text = "Ln 1, Col 1";
            // 
            // documentStatusLabel
            // 
            documentStatusLabel.Name = "documentStatusLabel";
            documentStatusLabel.Size = new Size(88, 17);
            documentStatusLabel.Text = "Len 0, Sel 0";
            // 
            // TextEditor
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(944, 550);
            Controls.Add(editor);
            Controls.Add(searchPanel);
            Controls.Add(statusStrip);
            Controls.Add(mainToolStrip);
            KeyPreview = true;
            MinimumSize = new Size(640, 400);
            Name = "TextEditor";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Screenzap Text Editor";
            FormClosing += TextEditor_FormClosing;
            KeyDown += TextEditor_KeyDown;
            mainToolStrip.ResumeLayout(false);
            mainToolStrip.PerformLayout();
            searchPanel.ResumeLayout(false);
            searchPanel.PerformLayout();
            statusStrip.ResumeLayout(false);
            statusStrip.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private ToolStrip mainToolStrip;
        private FontAwesome.Sharp.IconToolStripButton saveToolStripButton;
        private FontAwesome.Sharp.IconToolStripButton saveAsToolStripButton;
        private FontAwesome.Sharp.IconToolStripButton copyToolStripButton;
        private FontAwesome.Sharp.IconToolStripButton reloadToolStripButton;
        private ToolStripLabel reloadNotificationLabel;
        private FontAwesome.Sharp.IconToolStripButton findToolStripButton;
        private Panel searchPanel;
        private Button closeSearchPanelButton;
        private Button replaceAllButton;
        private Button replaceButton;
        private Button findPreviousButton;
        private Button findNextButton;
        private CheckBox regexCheckBox;
        private CheckBox wholeWordCheckBox;
        private CheckBox matchCaseCheckBox;
        private Label replaceLabel;
        private TextBox replaceTextBox;
        private Label findLabel;
        private TextBox findTextBox;
        private Label searchMessageLabel;
        private Scintilla editor;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel caretStatusLabel;
        private ToolStripStatusLabel documentStatusLabel;
    }
}