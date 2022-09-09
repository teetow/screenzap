namespace screenzap
{
    partial class Screenzap
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
            if (disposing && (components != null))
            {
                components.Dispose();
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
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Screenzap));
            this.label1 = new System.Windows.Forms.Label();
            this.notifyIcon1 = new System.Windows.Forms.NotifyIcon(this.components);
            this.contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.startWhenLoggedInToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.showBalloonMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.setKeyboardShortcutToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.quitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.setfolderToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.contextMenuStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(17, 27);
            this.label1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(44, 16);
            this.label1.TabIndex = 0;
            this.label1.Text = "label1";
            // 
            // notifyIcon1
            // 
            this.notifyIcon1.ContextMenuStrip = this.contextMenuStrip1;
            this.notifyIcon1.Icon = ((System.Drawing.Icon)(resources.GetObject("notifyIcon1.Icon")));
            this.notifyIcon1.Text = "Screenzap";
            this.notifyIcon1.Visible = true;
            // 
            // contextMenuStrip1
            // 
            this.contextMenuStrip1.ImageScalingSize = new System.Drawing.Size(20, 20);
            this.contextMenuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.startWhenLoggedInToolStripMenuItem,
            this.showBalloonMenuItem,
            this.toolStripSeparator2,
            this.setKeyboardShortcutToolStripMenuItem,
            this.setfolderToolStripMenuItem,
            this.toolStripSeparator1,
            this.quitToolStripMenuItem});
            this.contextMenuStrip1.Name = "contextMenuStrip1";
            this.contextMenuStrip1.Size = new System.Drawing.Size(266, 164);
            // 
            // startWhenLoggedInToolStripMenuItem
            // 
            this.startWhenLoggedInToolStripMenuItem.CheckOnClick = true;
            this.startWhenLoggedInToolStripMenuItem.Name = "startWhenLoggedInToolStripMenuItem";
            this.startWhenLoggedInToolStripMenuItem.Size = new System.Drawing.Size(265, 24);
            this.startWhenLoggedInToolStripMenuItem.Text = "Start when &logged in";
            this.startWhenLoggedInToolStripMenuItem.CheckStateChanged += new System.EventHandler(this.startWhenLoggedInToolStripMenuItem_CheckStateChanged);
            // 
            // showBalloonMenuItem
            // 
            this.showBalloonMenuItem.Name = "showBalloonMenuItem";
            this.showBalloonMenuItem.Size = new System.Drawing.Size(265, 24);
            this.showBalloonMenuItem.Text = "Show &notification on startup";
            this.showBalloonMenuItem.Click += new System.EventHandler(this.showBalloonMenuItem_Click);
            // 
            // setKeyboardShortcutToolStripMenuItem
            // 
            this.setKeyboardShortcutToolStripMenuItem.Name = "setKeyboardShortcutToolStripMenuItem";
            this.setKeyboardShortcutToolStripMenuItem.Size = new System.Drawing.Size(265, 24);
            this.setKeyboardShortcutToolStripMenuItem.Text = "Set &keyboard shortcut...";
            this.setKeyboardShortcutToolStripMenuItem.Click += new System.EventHandler(this.setKeyboardShortcutToolStripMenuItem_Click);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(262, 6);
            // 
            // quitToolStripMenuItem
            // 
            this.quitToolStripMenuItem.Name = "quitToolStripMenuItem";
            this.quitToolStripMenuItem.Size = new System.Drawing.Size(265, 24);
            this.quitToolStripMenuItem.Text = "&Quit";
            this.quitToolStripMenuItem.Click += new System.EventHandler(this.quitToolStripMenuItem_Click);
            // 
            // setfolderToolStripMenuItem
            // 
            this.setfolderToolStripMenuItem.Name = "setfolderToolStripMenuItem";
            this.setfolderToolStripMenuItem.Size = new System.Drawing.Size(265, 24);
            this.setfolderToolStripMenuItem.Text = "Set &folder...";
            this.setfolderToolStripMenuItem.Click += new System.EventHandler(this.setfolderToolStripMenuItem_Click);
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(262, 6);
            // 
            // Screenzap
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(120F, 120F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.ClientSize = new System.Drawing.Size(437, 282);
            this.Controls.Add(this.label1);
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "Screenzap";
            this.Text = "Form1";
            this.contextMenuStrip1.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.NotifyIcon notifyIcon1;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip1;
        private System.Windows.Forms.ToolStripMenuItem startWhenLoggedInToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem quitToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem setKeyboardShortcutToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripMenuItem showBalloonMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        private System.Windows.Forms.ToolStripMenuItem setfolderToolStripMenuItem;
    }
}

