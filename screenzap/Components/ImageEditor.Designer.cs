namespace screenzap
{
    partial class ImageEditor
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                undoStack?.Dispose();
                ReleaseCensorPreviewBuffer();
                components?.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ImageEditor));
            this.mainToolStrip = new System.Windows.Forms.ToolStrip();
            this.saveToolStripButton = new FontAwesome.Sharp.IconToolStripButton();
            this.saveAsToolStripButton = new FontAwesome.Sharp.IconToolStripButton();
            this.reloadToolStripButton = new FontAwesome.Sharp.IconToolStripButton();
            this.reloadNotificationLabel = new System.Windows.Forms.ToolStripLabel();
            this.cropToolStripButton = new FontAwesome.Sharp.IconToolStripButton();
            this.replaceToolStripButton = new FontAwesome.Sharp.IconToolStripButton();
            this.censorToolStripButton = new FontAwesome.Sharp.IconToolStripButton();
            this.copyClipboardToolStripButton = new FontAwesome.Sharp.IconToolStripButton();
            this.arrowToolStripButton = new FontAwesome.Sharp.IconToolStripButton();
            this.rectangleToolStripButton = new FontAwesome.Sharp.IconToolStripButton();
            this.textToolStripButton = new FontAwesome.Sharp.IconToolStripButton();
            this.textToolSeparator = new System.Windows.Forms.ToolStripSeparator();
            this.fontComboBox = new System.Windows.Forms.ToolStripComboBox();
            this.fontVariantComboBox = new System.Windows.Forms.ToolStripComboBox();
            this.fontSizeComboBox = new System.Windows.Forms.ToolStripComboBox();
            this.boldButton = new System.Windows.Forms.ToolStripButton();
            this.italicButton = new System.Windows.Forms.ToolStripButton();
            this.underlineButton = new System.Windows.Forms.ToolStripButton();
            this.textColorButton = new System.Windows.Forms.ToolStripButton();
            this.traceToolStripDropDown = new System.Windows.Forms.ToolStripDropDownButton();
            this.tracePosterMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.tracePhotoMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.traceBwMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.censorToolStrip = new System.Windows.Forms.ToolStrip();
            this.selectAllToolStripButton = new FontAwesome.Sharp.IconToolStripButton();
            this.selectNoneToolStripButton = new FontAwesome.Sharp.IconToolStripButton();
            this.censorToolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.confidenceToolStripLabel = new System.Windows.Forms.ToolStripLabel();
            this.confidenceTrackBar = new System.Windows.Forms.TrackBar();
            this.confidenceToolStripHost = new System.Windows.Forms.ToolStripControlHost(this.confidenceTrackBar);
            this.confidenceValueLabel = new System.Windows.Forms.ToolStripLabel();
            this.censorProgressBar = new System.Windows.Forms.ToolStripProgressBar();
            this.censorToolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.applyCensorToolStripButton = new FontAwesome.Sharp.IconToolStripButton();
            this.cancelCensorToolStripButton = new FontAwesome.Sharp.IconToolStripButton();
            this.canvasPanel = new System.Windows.Forms.Panel();
            this.pictureBox1 = new screenzap.Components.Shared.ImageViewportControl();
            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.resolutionStatusLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.zoomStatusLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.selectionStatusLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.mainToolStrip.SuspendLayout();
            this.censorToolStrip.SuspendLayout();
            this.canvasPanel.SuspendLayout();
            this.statusStrip.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.confidenceTrackBar)).BeginInit();
            this.SuspendLayout();
            // 
            // mainToolStrip
            // 
            this.mainToolStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.mainToolStrip.ImageScalingSize = new System.Drawing.Size(20, 20);
            this.mainToolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.saveToolStripButton,
            this.saveAsToolStripButton,
            this.copyClipboardToolStripButton,
            this.reloadToolStripButton,
            this.reloadNotificationLabel,
            this.cropToolStripButton,
            this.replaceToolStripButton,
            this.arrowToolStripButton,
            this.rectangleToolStripButton,
            this.textToolStripButton,
            this.textToolSeparator,
            this.fontComboBox,
            this.fontVariantComboBox,
            this.fontSizeComboBox,
            this.boldButton,
            this.italicButton,
            this.underlineButton,
            this.textColorButton,
            this.traceToolStripDropDown,
            this.censorToolStripButton});
            this.mainToolStrip.Location = new System.Drawing.Point(0, 0);
            this.mainToolStrip.Name = "mainToolStrip";
            this.mainToolStrip.Padding = new System.Windows.Forms.Padding(4, 2, 0, 2);
            this.mainToolStrip.Size = new System.Drawing.Size(413, 27);
            this.mainToolStrip.TabIndex = 0;
            this.mainToolStrip.Text = "toolStrip1";
            // 
            // textToolSeparator
            // 
            this.textToolSeparator.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.textToolSeparator.Name = "textToolSeparator";
            this.textToolSeparator.Size = new System.Drawing.Size(6, 23);
            this.textToolSeparator.Visible = false;
            // 
            // fontComboBox
            // 
            this.fontComboBox.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.fontComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.fontComboBox.Name = "fontComboBox";
            this.fontComboBox.Size = new System.Drawing.Size(160, 23);
            this.fontComboBox.ToolTipText = "Font Family";
            this.fontComboBox.Visible = false;
            this.fontComboBox.SelectedIndexChanged += new System.EventHandler(this.fontComboBox_SelectedIndexChanged);
            // 
            // fontVariantComboBox
            // 
            this.fontVariantComboBox.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.fontVariantComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.fontVariantComboBox.Name = "fontVariantComboBox";
            this.fontVariantComboBox.Size = new System.Drawing.Size(100, 23);
            this.fontVariantComboBox.ToolTipText = "Font Weight/Style Variant";
            this.fontVariantComboBox.Visible = false;
            this.fontVariantComboBox.SelectedIndexChanged += new System.EventHandler(this.fontVariantComboBox_SelectedIndexChanged);
            // 
            // fontSizeComboBox
            // 
            this.fontSizeComboBox.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.fontSizeComboBox.Name = "fontSizeComboBox";
            this.fontSizeComboBox.Size = new System.Drawing.Size(60, 23);
            this.fontSizeComboBox.ToolTipText = "Font Size";
            this.fontSizeComboBox.Visible = false;
            this.fontSizeComboBox.TextChanged += new System.EventHandler(this.fontSizeComboBox_TextChanged);
            // 
            // boldButton
            // 
            this.boldButton.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.boldButton.CheckOnClick = true;
            this.boldButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.boldButton.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.boldButton.Name = "boldButton";
            this.boldButton.Size = new System.Drawing.Size(23, 20);
            this.boldButton.Text = "B";
            this.boldButton.ToolTipText = "Bold";
            this.boldButton.Visible = false;
            this.boldButton.CheckedChanged += new System.EventHandler(this.boldButton_CheckedChanged);
            // 
            // italicButton
            // 
            this.italicButton.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.italicButton.CheckOnClick = true;
            this.italicButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.italicButton.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic);
            this.italicButton.Name = "italicButton";
            this.italicButton.Size = new System.Drawing.Size(23, 20);
            this.italicButton.Text = "I";
            this.italicButton.ToolTipText = "Italic";
            this.italicButton.Visible = false;
            this.italicButton.CheckedChanged += new System.EventHandler(this.italicButton_CheckedChanged);
            // 
            // underlineButton
            // 
            this.underlineButton.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.underlineButton.CheckOnClick = true;
            this.underlineButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.underlineButton.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Underline);
            this.underlineButton.Name = "underlineButton";
            this.underlineButton.Size = new System.Drawing.Size(23, 20);
            this.underlineButton.Text = "U";
            this.underlineButton.ToolTipText = "Underline";
            this.underlineButton.Visible = false;
            this.underlineButton.CheckedChanged += new System.EventHandler(this.underlineButton_CheckedChanged);
            // 
            // textColorButton
            // 
            this.textColorButton.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.textColorButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.textColorButton.Name = "textColorButton";
            this.textColorButton.Size = new System.Drawing.Size(43, 20);
            this.textColorButton.Text = "Color";
            this.textColorButton.ToolTipText = "Text Color";
            this.textColorButton.Visible = false;
            this.textColorButton.Click += new System.EventHandler(this.textColorButton_Click);
            // 
            // censorToolStrip
            // 
            this.censorToolStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.censorToolStrip.ImageScalingSize = new System.Drawing.Size(20, 20);
            this.censorToolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.selectAllToolStripButton,
            this.selectNoneToolStripButton,
            this.censorToolStripSeparator1,
            this.confidenceToolStripLabel,
            this.confidenceToolStripHost,
            this.confidenceValueLabel,
            this.censorProgressBar,
            this.censorToolStripSeparator2,
            this.applyCensorToolStripButton,
            this.cancelCensorToolStripButton});
            this.censorToolStrip.Location = new System.Drawing.Point(0, 27);
            this.censorToolStrip.Name = "censorToolStrip";
            this.censorToolStrip.Padding = new System.Windows.Forms.Padding(4, 2, 0, 2);
            this.censorToolStrip.Size = new System.Drawing.Size(413, 27);
            this.censorToolStrip.TabIndex = 1;
            this.censorToolStrip.Text = "censorToolStrip";
            this.censorToolStrip.Visible = false;
            // 
            // selectAllToolStripButton
            // 
            this.selectAllToolStripButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.selectAllToolStripButton.Name = "selectAllToolStripButton";
            this.selectAllToolStripButton.Size = new System.Drawing.Size(69, 23);
            this.selectAllToolStripButton.Text = "Select All";
            this.selectAllToolStripButton.Click += new System.EventHandler(this.selectAllToolStripButton_Click);
            // 
            // selectNoneToolStripButton
            // 
            this.selectNoneToolStripButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.selectNoneToolStripButton.Name = "selectNoneToolStripButton";
            this.selectNoneToolStripButton.Size = new System.Drawing.Size(87, 23);
            this.selectNoneToolStripButton.Text = "Select None";
            this.selectNoneToolStripButton.Click += new System.EventHandler(this.selectNoneToolStripButton_Click);
            // 
            // censorToolStripSeparator1
            // 
            this.censorToolStripSeparator1.Name = "censorToolStripSeparator1";
            this.censorToolStripSeparator1.Size = new System.Drawing.Size(6, 23);
            // 
            // confidenceToolStripLabel
            // 
            this.confidenceToolStripLabel.Name = "confidenceToolStripLabel";
            this.confidenceToolStripLabel.Size = new System.Drawing.Size(84, 23);
            this.confidenceToolStripLabel.Text = "Min Confidence";
            // 
            // confidenceTrackBar
            // 
            this.confidenceTrackBar.AutoSize = false;
            this.confidenceTrackBar.LargeChange = 10;
            this.confidenceTrackBar.Location = new System.Drawing.Point(0, 0);
            this.confidenceTrackBar.Maximum = 100;
            this.confidenceTrackBar.Name = "confidenceTrackBar";
            this.confidenceTrackBar.Size = new System.Drawing.Size(140, 24);
            this.confidenceTrackBar.TabIndex = 0;
            this.confidenceTrackBar.TickFrequency = 10;
            this.confidenceTrackBar.ValueChanged += new System.EventHandler(this.confidenceTrackBar_ValueChanged);
            // 
            // confidenceToolStripHost
            // 
            this.confidenceToolStripHost.AutoSize = false;
            this.confidenceToolStripHost.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.confidenceToolStripHost.Name = "confidenceToolStripHost";
            this.confidenceToolStripHost.Size = new System.Drawing.Size(146, 23);
            this.confidenceToolStripHost.Text = "confidenceToolStripHost";
            // 
            // confidenceValueLabel
            // 
            this.confidenceValueLabel.Name = "confidenceValueLabel";
            this.confidenceValueLabel.Size = new System.Drawing.Size(29, 23);
            this.confidenceValueLabel.Text = "0%";
            // 
            // censorProgressBar
            // 
            this.censorProgressBar.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.censorProgressBar.AutoSize = false;
            this.censorProgressBar.MarqueeAnimationSpeed = 30;
            this.censorProgressBar.Name = "censorProgressBar";
            this.censorProgressBar.Size = new System.Drawing.Size(120, 19);
            this.censorProgressBar.Style = System.Windows.Forms.ProgressBarStyle.Marquee;
            this.censorProgressBar.Visible = false;
            // 
            // censorToolStripSeparator2
            // 
            this.censorToolStripSeparator2.Name = "censorToolStripSeparator2";
            this.censorToolStripSeparator2.Size = new System.Drawing.Size(6, 23);
            // 
            // applyCensorToolStripButton
            // 
            this.applyCensorToolStripButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.applyCensorToolStripButton.Enabled = false;
            this.applyCensorToolStripButton.Name = "applyCensorToolStripButton";
            this.applyCensorToolStripButton.Size = new System.Drawing.Size(44, 23);
            this.applyCensorToolStripButton.Text = "Apply";
            this.applyCensorToolStripButton.Click += new System.EventHandler(this.applyCensorToolStripButton_Click);
            // 
            // cancelCensorToolStripButton
            // 
            this.cancelCensorToolStripButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.cancelCensorToolStripButton.Name = "cancelCensorToolStripButton";
            this.cancelCensorToolStripButton.Size = new System.Drawing.Size(53, 23);
            this.cancelCensorToolStripButton.Text = "Cancel";
            this.cancelCensorToolStripButton.Click += new System.EventHandler(this.cancelCensorToolStripButton_Click);
            // 
            // saveToolStripButton
            // 
            this.saveToolStripButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.saveToolStripButton.Enabled = false;
            this.saveToolStripButton.Name = "saveToolStripButton";
            this.saveToolStripButton.Size = new System.Drawing.Size(43, 23);
            this.saveToolStripButton.Text = "Save";
            this.saveToolStripButton.ToolTipText = "Save (Ctrl+S)";
            this.saveToolStripButton.Click += new System.EventHandler(this.saveToolStripButton_Click);
            // 
            // saveAsToolStripButton
            // 
            this.saveAsToolStripButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.saveAsToolStripButton.Enabled = false;
            this.saveAsToolStripButton.Name = "saveAsToolStripButton";
            this.saveAsToolStripButton.Size = new System.Drawing.Size(69, 23);
            this.saveAsToolStripButton.Text = "Save As...";
            this.saveAsToolStripButton.ToolTipText = "Save As (Ctrl+Shift+S)";
            this.saveAsToolStripButton.Click += new System.EventHandler(this.saveAsToolStripButton_Click);
            // 
            // cropToolStripButton
            // 
            this.cropToolStripButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.cropToolStripButton.Enabled = false;
            this.cropToolStripButton.Name = "cropToolStripButton";
            this.cropToolStripButton.Size = new System.Drawing.Size(43, 23);
            this.cropToolStripButton.Text = "Crop";
            this.cropToolStripButton.ToolTipText = "Crop Selection (Ctrl+T)";
            this.cropToolStripButton.Click += new System.EventHandler(this.cropToolStripButton_Click);
            // 
            // replaceToolStripButton
            // 
            this.replaceToolStripButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.replaceToolStripButton.Enabled = false;
            this.replaceToolStripButton.Name = "replaceToolStripButton";
            this.replaceToolStripButton.Size = new System.Drawing.Size(82, 23);
            this.replaceToolStripButton.Text = "Replace BG";
            this.replaceToolStripButton.ToolTipText = "Replace with Background (Ctrl+B or Backspace)";
            this.replaceToolStripButton.Click += new System.EventHandler(this.replaceToolStripButton_Click);
            // 
            // arrowToolStripButton
            // 
            this.arrowToolStripButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.arrowToolStripButton.Enabled = false;
            this.arrowToolStripButton.Name = "arrowToolStripButton";
            this.arrowToolStripButton.Size = new System.Drawing.Size(49, 23);
            this.arrowToolStripButton.Text = "Arrow";
            this.arrowToolStripButton.ToolTipText = "Draw an arrow annotation";
            this.arrowToolStripButton.Click += new System.EventHandler(this.arrowToolStripButton_Click);
            // 
            // rectangleToolStripButton
            // 
            this.rectangleToolStripButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.rectangleToolStripButton.Enabled = false;
            this.rectangleToolStripButton.Name = "rectangleToolStripButton";
            this.rectangleToolStripButton.Size = new System.Drawing.Size(73, 23);
            this.rectangleToolStripButton.Text = "Rectangle";
            this.rectangleToolStripButton.ToolTipText = "Draw a rectangle annotation";
            this.rectangleToolStripButton.Click += new System.EventHandler(this.rectangleToolStripButton_Click);
            // 
            // textToolStripButton
            // 
            this.textToolStripButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.textToolStripButton.Enabled = false;
            this.textToolStripButton.Name = "textToolStripButton";
            this.textToolStripButton.Size = new System.Drawing.Size(36, 23);
            this.textToolStripButton.Text = "Text";
            this.textToolStripButton.ToolTipText = "Add text annotation";
            this.textToolStripButton.Click += new System.EventHandler(this.textToolStripButton_Click);
            // 
            // traceToolStripDropDown
            // 
            this.traceToolStripDropDown.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.traceToolStripDropDown.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tracePosterMenuItem,
            this.tracePhotoMenuItem,
            this.traceBwMenuItem});
            this.traceToolStripDropDown.Enabled = false;
            this.traceToolStripDropDown.Name = "traceToolStripDropDown";
            this.traceToolStripDropDown.Size = new System.Drawing.Size(73, 23);
            this.traceToolStripDropDown.Text = "Trace";
            this.traceToolStripDropDown.ToolTipText = "Trace bitmap to SVG and copy to clipboard";
            // 
            // tracePosterMenuItem
            // 
            this.tracePosterMenuItem.Name = "tracePosterMenuItem";
            this.tracePosterMenuItem.Size = new System.Drawing.Size(180, 22);
            this.tracePosterMenuItem.Text = "Poster (logos, icons)";
            this.tracePosterMenuItem.Click += new System.EventHandler(this.tracePosterMenuItem_Click);
            // 
            // tracePhotoMenuItem
            // 
            this.tracePhotoMenuItem.Name = "tracePhotoMenuItem";
            this.tracePhotoMenuItem.Size = new System.Drawing.Size(180, 22);
            this.tracePhotoMenuItem.Text = "Photo (complex images)";
            this.tracePhotoMenuItem.Click += new System.EventHandler(this.tracePhotoMenuItem_Click);
            // 
            // traceBwMenuItem
            // 
            this.traceBwMenuItem.Name = "traceBwMenuItem";
            this.traceBwMenuItem.Size = new System.Drawing.Size(180, 22);
            this.traceBwMenuItem.Text = "B&&W (line art, sketches)";
            this.traceBwMenuItem.Click += new System.EventHandler(this.traceBwMenuItem_Click);
            // 
            // censorToolStripButton
            // 
            this.censorToolStripButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.censorToolStripButton.Enabled = false;
            this.censorToolStripButton.Name = "censorToolStripButton";
            this.censorToolStripButton.Size = new System.Drawing.Size(92, 23);
            this.censorToolStripButton.Text = "Censor Tool";
            this.censorToolStripButton.ToolTipText = "Detect text and censor selections (Ctrl+E)";
            this.censorToolStripButton.Click += new System.EventHandler(this.censorToolStripButton_Click);
            // 
            // copyClipboardToolStripButton
            // 
            this.copyClipboardToolStripButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.copyClipboardToolStripButton.Enabled = false;
            this.copyClipboardToolStripButton.Name = "copyClipboardToolStripButton";
            this.copyClipboardToolStripButton.Size = new System.Drawing.Size(97, 23);
            this.copyClipboardToolStripButton.Text = "Save to Clipboard";
            this.copyClipboardToolStripButton.ToolTipText = "Save the current image to the clipboard";
            this.copyClipboardToolStripButton.Click += new System.EventHandler(this.copyClipboardToolStripButton_Click);
            // 
            // reloadToolStripButton
            // 
            this.reloadToolStripButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.reloadToolStripButton.Name = "reloadToolStripButton";
            this.reloadToolStripButton.Size = new System.Drawing.Size(49, 23);
            this.reloadToolStripButton.Text = "Reload";
            this.reloadToolStripButton.ToolTipText = "Reload from clipboard (Ctrl+R)";
            this.reloadToolStripButton.Click += new System.EventHandler(this.reloadToolStripButton_Click);
            // 
            // reloadNotificationLabel
            // 
            this.reloadNotificationLabel.AutoSize = false;
            this.reloadNotificationLabel.Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.reloadNotificationLabel.ForeColor = System.Drawing.Color.OrangeRed;
            this.reloadNotificationLabel.Margin = new System.Windows.Forms.Padding(-12, 0, 4, 0);
            this.reloadNotificationLabel.Name = "reloadNotificationLabel";
            this.reloadNotificationLabel.Size = new System.Drawing.Size(14, 23);
            this.reloadNotificationLabel.Text = "●";
            this.reloadNotificationLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.reloadNotificationLabel.Visible = false;
            // 
            // canvasPanel
            // 
            this.canvasPanel.BackColor = System.Drawing.SystemColors.ControlDarkDark;
            this.canvasPanel.Controls.Add(this.pictureBox1);
            this.canvasPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.canvasPanel.Location = new System.Drawing.Point(0, 27);
            this.canvasPanel.Name = "canvasPanel";
            this.canvasPanel.Size = new System.Drawing.Size(413, 276);
            this.canvasPanel.TabIndex = 2;
            this.canvasPanel.SizeChanged += new System.EventHandler(this.canvasPanel_SizeChanged);
            // 
            // pictureBox1
            // 
            this.pictureBox1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pictureBox1.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            this.pictureBox1.Location = new System.Drawing.Point(0, 0);
            this.pictureBox1.Margin = new System.Windows.Forms.Padding(0);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(413, 276);
            this.pictureBox1.TabIndex = 0;
            this.pictureBox1.TabStop = false;
            this.pictureBox1.Paint += new System.Windows.Forms.PaintEventHandler(this.pictureBox1_Paint);
            this.pictureBox1.MouseDown += new System.Windows.Forms.MouseEventHandler(this.pictureBox1_MouseDown);
            this.pictureBox1.MouseMove += new System.Windows.Forms.MouseEventHandler(this.pictureBox1_MouseMove);
            this.pictureBox1.MouseUp += new System.Windows.Forms.MouseEventHandler(this.pictureBox1_MouseUp);
            this.pictureBox1.SizeChanged += new System.EventHandler(this.pictureBox1_SizeChanged);
            // 
            // statusStrip
            // 
            this.statusStrip.BackColor = System.Drawing.SystemColors.Control;
            this.statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.resolutionStatusLabel,
            this.zoomStatusLabel,
            this.selectionStatusLabel});
            this.statusStrip.Location = new System.Drawing.Point(0, 281);
            this.statusStrip.Name = "statusStrip";
            this.statusStrip.Size = new System.Drawing.Size(413, 22);
            this.statusStrip.SizingGrip = false;
            this.statusStrip.TabIndex = 4;
            // 
            // resolutionStatusLabel
            // 
            this.resolutionStatusLabel.Name = "resolutionStatusLabel";
            this.resolutionStatusLabel.Size = new System.Drawing.Size(0, 17);
            // 
            // zoomStatusLabel
            // 
            this.zoomStatusLabel.Name = "zoomStatusLabel";
            this.zoomStatusLabel.Size = new System.Drawing.Size(0, 17);
            // 
            // selectionStatusLabel
            // 
            this.selectionStatusLabel.Name = "selectionStatusLabel";
            this.selectionStatusLabel.Size = new System.Drawing.Size(0, 17);
            // 
            // ImageEditor
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Desktop;
            this.ClientSize = new System.Drawing.Size(413, 303);
            this.Controls.Add(this.canvasPanel);
            this.Controls.Add(this.statusStrip);
            this.Controls.Add(this.censorToolStrip);
            this.Controls.Add(this.mainToolStrip);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MinimumSize = new System.Drawing.Size(320, 200);
            this.Name = "ImageEditor";
            this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
            this.Text = "Screenzap Image Editor";
            this.KeyPreview = true;
            this.ClientSizeChanged += new System.EventHandler(this.ImageEditor_ClientSizeChanged);
            this.ResizeEnd += new System.EventHandler(this.ImageEditor_ResizeEnd);
            this.Paint += new System.Windows.Forms.PaintEventHandler(this.ImageEditor_Paint);
            this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.ImageEditor_KeyDown);
            this.KeyUp += new System.Windows.Forms.KeyEventHandler(this.ImageEditor_KeyUp);
            this.canvasPanel.ResumeLayout(false);
            this.statusStrip.ResumeLayout(false);
            this.statusStrip.PerformLayout();
            this.censorToolStrip.ResumeLayout(false);
            this.censorToolStrip.PerformLayout();
            this.mainToolStrip.ResumeLayout(false);
            this.mainToolStrip.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.confidenceTrackBar)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ToolStrip mainToolStrip;
        private FontAwesome.Sharp.IconToolStripButton saveToolStripButton;
        private FontAwesome.Sharp.IconToolStripButton saveAsToolStripButton;
        private System.Windows.Forms.Panel canvasPanel;
        private Components.Shared.ImageViewportControl pictureBox1;
        private FontAwesome.Sharp.IconToolStripButton cropToolStripButton;
    private FontAwesome.Sharp.IconToolStripButton replaceToolStripButton;
    private FontAwesome.Sharp.IconToolStripButton censorToolStripButton;
    private FontAwesome.Sharp.IconToolStripButton copyClipboardToolStripButton;
    private FontAwesome.Sharp.IconToolStripButton reloadToolStripButton;
    private System.Windows.Forms.ToolStripLabel reloadNotificationLabel;
    private FontAwesome.Sharp.IconToolStripButton arrowToolStripButton;
    private FontAwesome.Sharp.IconToolStripButton rectangleToolStripButton;
    private FontAwesome.Sharp.IconToolStripButton textToolStripButton;
    private System.Windows.Forms.ToolStripDropDownButton traceToolStripDropDown;
    private System.Windows.Forms.ToolStripMenuItem tracePosterMenuItem;
    private System.Windows.Forms.ToolStripMenuItem tracePhotoMenuItem;
    private System.Windows.Forms.ToolStripMenuItem traceBwMenuItem;
        private System.Windows.Forms.ToolStripSeparator textToolSeparator;
        private System.Windows.Forms.ToolStrip censorToolStrip;
        private FontAwesome.Sharp.IconToolStripButton selectAllToolStripButton;
        private FontAwesome.Sharp.IconToolStripButton selectNoneToolStripButton;
        private System.Windows.Forms.ToolStripSeparator censorToolStripSeparator1;
        private System.Windows.Forms.ToolStripLabel confidenceToolStripLabel;
        private System.Windows.Forms.TrackBar confidenceTrackBar;
        private System.Windows.Forms.ToolStripControlHost confidenceToolStripHost;
        private System.Windows.Forms.ToolStripLabel confidenceValueLabel;
        private System.Windows.Forms.ToolStripProgressBar censorProgressBar;
        private System.Windows.Forms.ToolStripSeparator censorToolStripSeparator2;
        private FontAwesome.Sharp.IconToolStripButton applyCensorToolStripButton;
        private FontAwesome.Sharp.IconToolStripButton cancelCensorToolStripButton;
        private System.Windows.Forms.ToolStripComboBox fontComboBox;
        private System.Windows.Forms.ToolStripComboBox fontVariantComboBox;
        private System.Windows.Forms.ToolStripComboBox fontSizeComboBox;
        private System.Windows.Forms.ToolStripButton boldButton;
        private System.Windows.Forms.ToolStripButton italicButton;
        private System.Windows.Forms.ToolStripButton underlineButton;
        private System.Windows.Forms.ToolStripButton textColorButton;
        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel resolutionStatusLabel;
        private System.Windows.Forms.ToolStripStatusLabel zoomStatusLabel;
        private System.Windows.Forms.ToolStripStatusLabel selectionStatusLabel;
    }
}