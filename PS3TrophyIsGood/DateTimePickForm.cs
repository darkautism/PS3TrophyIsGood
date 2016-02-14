using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace PS3TrophyIsGood {
    public partial class DateTimePickForm : Form {
        public DateTimePickForm() {
            InitializeComponent();
        }

        Random rand = new Random((int)DateTime.Now.Ticks);
        DateTime ps3Time = new DateTime(2008, 1, 1);
        private void button3_Click(object sender, EventArgs e) {
            dateTimePicker1.Value = new DateTime(Utility.LongRandom(ps3Time.Ticks, DateTime.Now.Ticks, rand));
        }
    }
}
