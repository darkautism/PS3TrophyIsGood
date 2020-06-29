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
            this.textBox1.Location = new System.Drawing.Point(12, 28);
            this.textBox1.Name = "textBox1";
            this.textBox1.Size = new System.Drawing.Size(314, 20);
            this.textBox1.TabIndex = 0;
            // 
            // button1
            // 
            this.button1.Location = new System.Drawing.Point(13, 55);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(75, 23);
            this.button1.TabIndex = 1;
            this.button1.Text = "Accept";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // button2
            // 
            this.button2.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.button2.Location = new System.Drawing.Point(251, 55);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(75, 23);
            this.button2.TabIndex = 2;
            this.button2.Text = "Cancel";
            this.button2.UseVisualStyleBackColor = true;
            // 
            // checkBox1
            // 
            this.checkBox1.AutoSize = true;
            this.checkBox1.Location = new System.Drawing.Point(94, 59);
            this.checkBox1.Name = "checkBox1";
            this.checkBox1.Size = new System.Drawing.Size(109, 17);
            this.checkBox1.TabIndex = 3;
            this.checkBox1.Text = "Apply Smart Copy";
            this.checkBox1.UseVisualStyleBackColor = true;
            this.checkBox1.CheckedChanged += new System.EventHandler(this.checkBox1_CheckedChanged);
            // 
            // groupBox1
            // 
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
            this.groupBox1.Location = new System.Drawing.Point(13, 93);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(313, 144);
            this.groupBox1.TabIndex = 4;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Options";
            // 
            // maxMinutes
            // 
            this.maxMinutes.Location = new System.Drawing.Point(6, 113);
            this.maxMinutes.Maximum = new decimal(new int[] {
            99999,
            0,
            0,
            0});
            this.maxMinutes.Name = "maxMinutes";
            this.maxMinutes.Size = new System.Drawing.Size(82, 20);
            this.maxMinutes.TabIndex = 11;
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(3, 97);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(93, 13);
            this.label5.TabIndex = 10;
            this.label5.Text = "Maximum minutes:";
            // 
            // minMinutes
            // 
            this.minMinutes.Location = new System.Drawing.Point(6, 74);
            this.minMinutes.Maximum = new decimal(new int[] {
            99999,
            0,
            0,
            0});
            this.minMinutes.Name = "minMinutes";
            this.minMinutes.Size = new System.Drawing.Size(82, 20);
            this.minMinutes.TabIndex = 9;
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(3, 58);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(90, 13);
            this.label4.TabIndex = 8;
            this.label4.Text = "Minimum minutes:";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(99, 19);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(34, 13);
            this.label3.TabIndex = 7;
            this.label3.Text = "Years";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(54, 19);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(37, 13);
            this.label2.TabIndex = 6;
            this.label2.Text = "Month";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(6, 19);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(31, 13);
            this.label1.TabIndex = 5;
            this.label1.Text = "Days";
            // 
            // yearsNumeric
            // 
            this.yearsNumeric.Location = new System.Drawing.Point(102, 35);
            this.yearsNumeric.Name = "yearsNumeric";
            this.yearsNumeric.Size = new System.Drawing.Size(42, 20);
            this.yearsNumeric.TabIndex = 2;
            // 
            // monthNumeric
            // 
            this.monthNumeric.Location = new System.Drawing.Point(54, 35);
            this.monthNumeric.Maximum = new decimal(new int[] {
            11,
            0,
            0,
            0});
            this.monthNumeric.Name = "monthNumeric";
            this.monthNumeric.Size = new System.Drawing.Size(42, 20);
            this.monthNumeric.TabIndex = 1;
            // 
            // daysNumeric
            // 
            this.daysNumeric.Location = new System.Drawing.Point(6, 35);
            this.daysNumeric.Maximum = new decimal(new int[] {
            30,
            0,
            0,
            0});
            this.daysNumeric.Name = "daysNumeric";
            this.daysNumeric.Size = new System.Drawing.Size(42, 20);
            this.daysNumeric.TabIndex = 0;
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(13, 9);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(60, 13);
            this.label6.TabIndex = 5;
            this.label6.Text = "Copy From:";
            // 
            // CopyFrom
            // 
            this.AcceptButton = this.button1;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSize = true;
            this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.CancelButton = this.button2;
            this.ClientSize = new System.Drawing.Size(336, 248);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.checkBox1);
            this.Controls.Add(this.button2);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.textBox1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            this.MaximizeBox = false;
            this.Name = "CopyFrom";
            this.Text = "CopyFrom";
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