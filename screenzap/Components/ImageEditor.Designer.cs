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
            this.saveToolStripButton = new System.Windows.Forms.ToolStripButton();
            this.saveAsToolStripButton = new System.Windows.Forms.ToolStripButton();
            this.cropToolStripButton = new System.Windows.Forms.ToolStripButton();
            this.replaceToolStripButton = new System.Windows.Forms.ToolStripButton();
            this.pictureBox1 = new screenzap.ScalingPictureBox();
            this.mainToolStrip.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // mainToolStrip
            // 
            this.mainToolStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.mainToolStrip.ImageScalingSize = new System.Drawing.Size(20, 20);
            this.mainToolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.saveToolStripButton,
            this.saveAsToolStripButton,
            this.cropToolStripButton,
            this.replaceToolStripButton});
            this.mainToolStrip.Location = new System.Drawing.Point(0, 0);
            this.mainToolStrip.Name = "mainToolStrip";
            this.mainToolStrip.Padding = new System.Windows.Forms.Padding(4, 2, 0, 2);
            this.mainToolStrip.Size = new System.Drawing.Size(413, 27);
            this.mainToolStrip.TabIndex = 0;
            this.mainToolStrip.Text = "toolStrip1";
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
            // pictureBox1
            // 
            this.pictureBox1.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Default;
            this.pictureBox1.Location = new System.Drawing.Point(0, 27);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(207, 139);
            this.pictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureBox1.TabIndex = 1;
            this.pictureBox1.TabStop = false;
            this.pictureBox1.Paint += new System.Windows.Forms.PaintEventHandler(this.pictureBox1_Paint);
            this.pictureBox1.MouseDown += new System.Windows.Forms.MouseEventHandler(this.pictureBox1_MouseDown);
            this.pictureBox1.MouseMove += new System.Windows.Forms.MouseEventHandler(this.pictureBox1_MouseMove);
            this.pictureBox1.MouseUp += new System.Windows.Forms.MouseEventHandler(this.pictureBox1_MouseUp);
            // 
            // ImageEditor
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Desktop;
            this.ClientSize = new System.Drawing.Size(413, 303);
            this.Controls.Add(this.pictureBox1);
            this.Controls.Add(this.mainToolStrip);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MinimumSize = new System.Drawing.Size(320, 200);
            this.Name = "ImageEditor";
            this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
            this.Text = "Screenzap Image Editor";
            this.KeyPreview = true;
            this.ResizeEnd += new System.EventHandler(this.ImageEditor_ResizeEnd);
            this.Paint += new System.Windows.Forms.PaintEventHandler(this.ImageEditor_Paint);
            this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.ImageEditor_KeyDown);
            this.KeyUp += new System.Windows.Forms.KeyEventHandler(this.ImageEditor_KeyUp);
            this.mainToolStrip.ResumeLayout(false);
            this.mainToolStrip.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ToolStrip mainToolStrip;
        private System.Windows.Forms.ToolStripButton saveToolStripButton;
        private System.Windows.Forms.ToolStripButton saveAsToolStripButton;
    private ScalingPictureBox pictureBox1;
    private System.Windows.Forms.ToolStripButton cropToolStripButton;
    private System.Windows.Forms.ToolStripButton replaceToolStripButton;
    }
}