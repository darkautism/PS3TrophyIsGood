namespace PS3TrophyIsGood {
    partial class Form1 {
        /// <summary>
        /// 設計工具所需的變數。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清除任何使用中的資源。
        /// </summary>
        /// <param name="disposing">如果應該處置 Managed 資源則為 true，否則為 false。</param>
        protected override void Dispose(bool disposing) {
            if (disposing && (components != null)) {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form 設計工具產生的程式碼

        /// <summary>
        /// 此為設計工具支援所需的方法 - 請勿使用程式碼編輯器
        /// 修改這個方法的內容。
        /// </summary>
        private void InitializeComponent() {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            this.linkLabel1 = new System.Windows.Forms.LinkLabel();
            this.imageList1 = new System.Windows.Forms.ImageList(this.components);
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.檔案ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.開啟ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.存檔ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.關閉檔案CToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.關閉ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.進階ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.瞬間白金ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.清除獎杯ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripMenuItem1 = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.setRandomStartTimeToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.setRandomEndTimeToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.重新整理ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripComboBox2 = new System.Windows.Forms.ToolStripComboBox();
            this.toolStripComboBox1 = new System.Windows.Forms.ToolStripComboBox();
            this.folderBrowserDialog1 = new System.Windows.Forms.FolderBrowserDialog();
            this.dateTimePicker1 = new System.Windows.Forms.DateTimePicker();
            this.label1 = new System.Windows.Forms.Label();
            this.progressBar1 = new System.Windows.Forms.ProgressBar();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.listViewEx1 = new ListViewEx.ListViewEx();
            this.columnHeader1 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeader2 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeader3 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeader4 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeader5 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeader7 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeader6 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeader8 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.menuStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // linkLabel1
            // 
            resources.ApplyResources(this.linkLabel1, "linkLabel1");
            this.linkLabel1.Name = "linkLabel1";
            this.linkLabel1.TabStop = true;
            this.linkLabel1.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.linkLabel1_LinkClicked);
            // 
            // imageList1
            // 
            this.imageList1.ColorDepth = System.Windows.Forms.ColorDepth.Depth8Bit;
            resources.ApplyResources(this.imageList1, "imageList1");
            this.imageList1.TransparentColor = System.Drawing.Color.Transparent;
            // 
            // menuStrip1
            // 
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.檔案ToolStripMenuItem,
            this.進階ToolStripMenuItem,
            this.重新整理ToolStripMenuItem,
            this.toolStripComboBox2,
            this.toolStripComboBox1});
            resources.ApplyResources(this.menuStrip1, "menuStrip1");
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Click += new System.EventHandler(this.menuStrip1_Click);
            // 
            // 檔案ToolStripMenuItem
            // 
            this.檔案ToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.開啟ToolStripMenuItem,
            this.存檔ToolStripMenuItem,
            this.關閉檔案CToolStripMenuItem,
            this.toolStripSeparator1,
            this.關閉ToolStripMenuItem});
            this.檔案ToolStripMenuItem.Name = "檔案ToolStripMenuItem";
            resources.ApplyResources(this.檔案ToolStripMenuItem, "檔案ToolStripMenuItem");
            // 
            // 開啟ToolStripMenuItem
            // 
            this.開啟ToolStripMenuItem.Name = "開啟ToolStripMenuItem";
            resources.ApplyResources(this.開啟ToolStripMenuItem, "開啟ToolStripMenuItem");
            this.開啟ToolStripMenuItem.Click += new System.EventHandler(this.開啟ToolStripMenuItem_Click);
            // 
            // 存檔ToolStripMenuItem
            // 
            this.存檔ToolStripMenuItem.Name = "存檔ToolStripMenuItem";
            resources.ApplyResources(this.存檔ToolStripMenuItem, "存檔ToolStripMenuItem");
            this.存檔ToolStripMenuItem.Click += new System.EventHandler(this.存檔ToolStripMenuItem_Click);
            // 
            // 關閉檔案CToolStripMenuItem
            // 
            this.關閉檔案CToolStripMenuItem.Name = "關閉檔案CToolStripMenuItem";
            resources.ApplyResources(this.關閉檔案CToolStripMenuItem, "關閉檔案CToolStripMenuItem");
            this.關閉檔案CToolStripMenuItem.Click += new System.EventHandler(this.關閉檔案CToolStripMenuItem_Click);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            resources.ApplyResources(this.toolStripSeparator1, "toolStripSeparator1");
            // 
            // 關閉ToolStripMenuItem
            // 
            this.關閉ToolStripMenuItem.Name = "關閉ToolStripMenuItem";
            resources.ApplyResources(this.關閉ToolStripMenuItem, "關閉ToolStripMenuItem");
            this.關閉ToolStripMenuItem.Click += new System.EventHandler(this.關閉ToolStripMenuItem_Click);
            // 
            // 進階ToolStripMenuItem
            // 
            this.進階ToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.瞬間白金ToolStripMenuItem,
            this.清除獎杯ToolStripMenuItem,
            this.toolStripMenuItem1,
            this.toolStripSeparator2,
            this.setRandomStartTimeToolStripMenuItem,
            this.setRandomEndTimeToolStripMenuItem});
            resources.ApplyResources(this.進階ToolStripMenuItem, "進階ToolStripMenuItem");
            this.進階ToolStripMenuItem.Name = "進階ToolStripMenuItem";
            // 
            // 瞬間白金ToolStripMenuItem
            // 
            this.瞬間白金ToolStripMenuItem.Name = "瞬間白金ToolStripMenuItem";
            resources.ApplyResources(this.瞬間白金ToolStripMenuItem, "瞬間白金ToolStripMenuItem");
            this.瞬間白金ToolStripMenuItem.Click += new System.EventHandler(this.瞬間白金ToolStripMenuItem_Click);
            // 
            // 清除獎杯ToolStripMenuItem
            // 
            this.清除獎杯ToolStripMenuItem.Name = "清除獎杯ToolStripMenuItem";
            resources.ApplyResources(this.清除獎杯ToolStripMenuItem, "清除獎杯ToolStripMenuItem");
            this.清除獎杯ToolStripMenuItem.Click += new System.EventHandler(this.清除獎杯ToolStripMenuItem_Click);
            // 
            // toolStripMenuItem1
            // 
            this.toolStripMenuItem1.Name = "toolStripMenuItem1";
            resources.ApplyResources(this.toolStripMenuItem1, "toolStripMenuItem1");
            this.toolStripMenuItem1.Click += new System.EventHandler(this.toolStripMenuItem1_Click);
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            resources.ApplyResources(this.toolStripSeparator2, "toolStripSeparator2");
            // 
            // setRandomStartTimeToolStripMenuItem
            // 
            this.setRandomStartTimeToolStripMenuItem.Name = "setRandomStartTimeToolStripMenuItem";
            resources.ApplyResources(this.setRandomStartTimeToolStripMenuItem, "setRandomStartTimeToolStripMenuItem");
            this.setRandomStartTimeToolStripMenuItem.Click += new System.EventHandler(this.setRandomStartTimeToolStripMenuItem_Click);
            // 
            // setRandomEndTimeToolStripMenuItem
            // 
            this.setRandomEndTimeToolStripMenuItem.Name = "setRandomEndTimeToolStripMenuItem";
            resources.ApplyResources(this.setRandomEndTimeToolStripMenuItem, "setRandomEndTimeToolStripMenuItem");
            this.setRandomEndTimeToolStripMenuItem.Click += new System.EventHandler(this.setRandomEndTimeToolStripMenuItem_Click);
            // 
            // 重新整理ToolStripMenuItem
            // 
            resources.ApplyResources(this.重新整理ToolStripMenuItem, "重新整理ToolStripMenuItem");
            this.重新整理ToolStripMenuItem.Name = "重新整理ToolStripMenuItem";
            this.重新整理ToolStripMenuItem.Click += new System.EventHandler(this.重新整理ToolStripMenuItem_Click);
            // 
            // toolStripComboBox2
            // 
            this.toolStripComboBox2.Name = "toolStripComboBox2";
            resources.ApplyResources(this.toolStripComboBox2, "toolStripComboBox2");
            // 
            // toolStripComboBox1
            // 
            this.toolStripComboBox1.Items.AddRange(new object[] {
            resources.GetString("toolStripComboBox1.Items"),
            resources.GetString("toolStripComboBox1.Items1"),
            resources.GetString("toolStripComboBox1.Items2")});
            this.toolStripComboBox1.Name = "toolStripComboBox1";
            resources.ApplyResources(this.toolStripComboBox1, "toolStripComboBox1");
            this.toolStripComboBox1.SelectedIndexChanged += new System.EventHandler(this.toolStripComboBox1_SelectedIndexChanged);
            // 
            // dateTimePicker1
            // 
            resources.ApplyResources(this.dateTimePicker1, "dateTimePicker1");
            this.dateTimePicker1.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.dateTimePicker1.Name = "dateTimePicker1";
            this.dateTimePicker1.TabStop = false;
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // progressBar1
            // 
            resources.ApplyResources(this.progressBar1, "progressBar1");
            this.progressBar1.Name = "progressBar1";
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // label3
            // 
            resources.ApplyResources(this.label3, "label3");
            this.label3.Name = "label3";
            // 
            // label4
            // 
            resources.ApplyResources(this.label4, "label4");
            this.label4.Name = "label4";
            // 
            // listViewEx1
            // 
            this.listViewEx1.AllowColumnReorder = true;
            this.listViewEx1.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnHeader1,
            this.columnHeader2,
            this.columnHeader3,
            this.columnHeader4,
            this.columnHeader5,
            this.columnHeader7,
            this.columnHeader6,
            this.columnHeader8});
            resources.ApplyResources(this.listViewEx1, "listViewEx1");
            this.listViewEx1.DoubleClickActivation = false;
            this.listViewEx1.FullRowSelect = true;
            this.listViewEx1.HideSelection = false;
            this.listViewEx1.LargeImageList = this.imageList1;
            this.listViewEx1.Name = "listViewEx1";
            this.listViewEx1.SmallImageList = this.imageList1;
            this.listViewEx1.UseCompatibleStateImageBehavior = false;
            this.listViewEx1.View = System.Windows.Forms.View.Details;
            this.listViewEx1.SubItemClicked += new ListViewEx.SubItemEventHandler(this.listViewEx1_SubItemClicked);
            this.listViewEx1.SubItemEndEditing += new ListViewEx.SubItemEndEditingEventHandler(this.listViewEx1_SubItemEndEditing);
            this.listViewEx1.DoubleClick += new System.EventHandler(this.listViewEx1_DoubleClick);
            // 
            // columnHeader1
            // 
            resources.ApplyResources(this.columnHeader1, "columnHeader1");
            // 
            // columnHeader2
            // 
            resources.ApplyResources(this.columnHeader2, "columnHeader2");
            // 
            // columnHeader3
            // 
            resources.ApplyResources(this.columnHeader3, "columnHeader3");
            // 
            // columnHeader4
            // 
            resources.ApplyResources(this.columnHeader4, "columnHeader4");
            // 
            // columnHeader5
            // 
            resources.ApplyResources(this.columnHeader5, "columnHeader5");
            // 
            // columnHeader7
            // 
            this.columnHeader7.Tag = "Sincronizado";
            resources.ApplyResources(this.columnHeader7, "columnHeader7");
            // 
            // columnHeader6
            // 
            resources.ApplyResources(this.columnHeader6, "columnHeader6");
            // 
            // columnHeader8
            // 
            resources.ApplyResources(this.columnHeader8, "columnHeader8");
            // 
            // Form1
            // 
            this.AllowDrop = true;
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.dateTimePicker1);
            this.Controls.Add(this.listViewEx1);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.progressBar1);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.linkLabel1);
            this.Controls.Add(this.menuStrip1);
            this.MainMenuStrip = this.menuStrip1;
            this.Name = "Form1";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.Form1_FormClosing);
            this.DragDrop += new System.Windows.Forms.DragEventHandler(this.Form1_DragDrop);
            this.DragEnter += new System.Windows.Forms.DragEventHandler(this.Form1_DragEnter);
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.LinkLabel linkLabel1;
        private ListViewEx.ListViewEx listViewEx1;
        private System.Windows.Forms.MenuStrip menuStrip1;
        private System.Windows.Forms.ToolStripMenuItem 檔案ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 開啟ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 存檔ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 關閉ToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog1;
        private System.Windows.Forms.ImageList imageList1;
        private System.Windows.Forms.ColumnHeader columnHeader1;
        private System.Windows.Forms.ColumnHeader columnHeader2;
        private System.Windows.Forms.ColumnHeader columnHeader3;
        private System.Windows.Forms.ColumnHeader columnHeader4;
        private System.Windows.Forms.ColumnHeader columnHeader5;
        private System.Windows.Forms.ColumnHeader columnHeader6;
        private System.Windows.Forms.DateTimePicker dateTimePicker1;
        private System.Windows.Forms.ToolStripMenuItem 進階ToolStripMenuItem;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.ProgressBar progressBar1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.ToolStripMenuItem 重新整理ToolStripMenuItem;
        private System.Windows.Forms.ColumnHeader columnHeader7;
        private System.Windows.Forms.ToolStripMenuItem 關閉檔案CToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 瞬間白金ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 清除獎杯ToolStripMenuItem;
        private System.Windows.Forms.ToolStripComboBox toolStripComboBox1;
        private System.Windows.Forms.ToolStripMenuItem setRandomStartTimeToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        private System.Windows.Forms.ToolStripMenuItem setRandomEndTimeToolStripMenuItem;
        private System.Windows.Forms.ColumnHeader columnHeader8;
        private System.Windows.Forms.ToolStripMenuItem toolStripMenuItem1;
        private System.Windows.Forms.ToolStripComboBox toolStripComboBox2;
    }
}

