namespace PS3TrophyIsGood
{
    partial class CopyFrom
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(CopyFrom));
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.button1 = new System.Windows.Forms.Button();
            this.button2 = new System.Windows.Forms.Button();
            this.checkBox1 = new System.Windows.Forms.CheckBox();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.maxMinutes = new System.Windows.Forms.NumericUpDown();
            this.label5 = new System.Windows.Forms.Label();
            this.minMinutes = new System.Windows.Forms.NumericUpDown();
            this.label4 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.yearsNumeric = new System.Windows.Forms.NumericUpDown();
            this.monthNumeric = new System.Windows.Forms.NumericUpDown();
            this.daysNumeric = new System.Windows.Forms.NumericUpDown();
            this.label6 = new System.Windows.Forms.Label();
            this.groupBox1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.maxMinutes)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.minMinutes)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.yearsNumeric)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.monthNumeric)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.daysNumeric)).BeginInit();
            this.SuspendLayout();
            // 
            // textBox1
            // 
            resources.ApplyResources(this.textBox1, "textBox1");
            this.textBox1.Name = "textBox1";
            // 
            // button1
            // 
            resources.ApplyResources(this.button1, "button1");
            this.button1.Name = "button1";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // button2
            // 
            resources.ApplyResources(this.button2, "button2");
            this.button2.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.button2.Name = "button2";
            this.button2.UseVisualStyleBackColor = true;
            // 
            // checkBox1
            // 
            resources.ApplyResources(this.checkBox1, "checkBox1");
            this.checkBox1.Name = "checkBox1";
            this.checkBox1.UseVisualStyleBackColor = true;
            this.checkBox1.CheckedChanged += new System.EventHandler(this.checkBox1_CheckedChanged);
            // 
            // groupBox1
            // 
            resources.ApplyResources(this.groupBox1, "groupBox1");
            this.groupBox1.Controls.Add(this.maxMinutes);
            this.groupBox1.Controls.Add(this.label5);
            this.groupBox1.Controls.Add(this.minMinutes);
            this.groupBox1.Controls.Add(this.label4);
            this.groupBox1.Controls.Add(this.label3);
            this.groupBox1.Controls.Add(this.label2);
            this.groupBox1.Controls.Add(this.label1);
            this.groupBox1.Controls.Add(this.yearsNumeric);
            this.groupBox1.Controls.Add(this.monthNumeric);
            this.groupBox1.Controls.Add(this.daysNumeric);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.TabStop = false;
            // 
            // maxMinutes
            // 
            resources.ApplyResources(this.maxMinutes, "maxMinutes");
            this.maxMinutes.Maximum = new decimal(new int[] {
            99999,
            0,
            0,
            0});
            this.maxMinutes.Name = "maxMinutes";
            // 
            // label5
            // 
            resources.ApplyResources(this.label5, "label5");
            this.label5.Name = "label5";
            // 
            // minMinutes
            // 
            resources.ApplyResources(this.minMinutes, "minMinutes");
            this.minMinutes.Maximum = new decimal(new int[] {
            99999,
            0,
            0,
            0});
            this.minMinutes.Name = "minMinutes";
            // 
            // label4
            // 
            resources.ApplyResources(this.label4, "label4");
            this.label4.Name = "label4";
            // 
            // label3
            // 
            resources.ApplyResources(this.label3, "label3");
            this.label3.Name = "label3";
            // 
            // label2
            // 
            resources.ApplyResources(this.label2, "label2");
            this.label2.Name = "label2";
            // 
            // label1
            // 
            resources.ApplyResources(this.label1, "label1");
            this.label1.Name = "label1";
            // 
            // yearsNumeric
            // 
            resources.ApplyResources(this.yearsNumeric, "yearsNumeric");
            this.yearsNumeric.Name = "yearsNumeric";
            // 
            // monthNumeric
            // 
            resources.ApplyResources(this.monthNumeric, "monthNumeric");
            this.monthNumeric.Maximum = new decimal(new int[] {
            11,
            0,
            0,
            0});
            this.monthNumeric.Name = "monthNumeric";
            // 
            // daysNumeric
            // 
            resources.ApplyResources(this.daysNumeric, "daysNumeric");
            this.daysNumeric.Maximum = new decimal(new int[] {
            30,
            0,
            0,
            0});
            this.daysNumeric.Name = "daysNumeric";
            // 
            // label6
            // 
            resources.ApplyResources(this.label6, "label6");
            this.label6.Name = "label6";
            // 
            // CopyFrom
            // 
            this.AcceptButton = this.button1;
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.button2;
            this.Controls.Add(this.label6);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.checkBox1);
            this.Controls.Add(this.button2);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.textBox1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            this.MaximizeBox = false;
            this.Name = "CopyFrom";
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.maxMinutes)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.minMinutes)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.yearsNumeric)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.monthNumeric)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.daysNumeric)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox textBox1;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.Button button2;
        public System.Windows.Forms.CheckBox checkBox1;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.NumericUpDown maxMinutes;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.NumericUpDown minMinutes;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.NumericUpDown yearsNumeric;
        private System.Windows.Forms.NumericUpDown monthNumeric;
        private System.Windows.Forms.NumericUpDown daysNumeric;
        private System.Windows.Forms.Label label6;
    }
}