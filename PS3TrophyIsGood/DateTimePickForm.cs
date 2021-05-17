using System;
using System.Windows.Forms;

namespace PS3TrophyIsGood {
    public partial class DateTimePickForm : Form {
        public DateTimePickForm(DateTime lastSyncTime) {
            InitializeComponent();
            ps3Time = lastSyncTime;
            dateTimePicker1.CustomFormat = Properties.strings.DateFormatString;
        }

        private Random rand = new Random((int)DateTime.Now.Ticks);
        private DateTime ps3Time = new DateTime(2008, 1, 1);
        private void button3_Click(object sender, EventArgs e) {
            dateTimePicker1.Value = new DateTime(Utility.LongRandom(ps3Time.Ticks, DateTime.Now.Ticks, rand));
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (DateTime.Compare(ps3Time, dateTimePicker1.Value) > 0)
            {
                MessageBox.Show(string.Format(Properties.strings.PsnSyncTime, ps3Time));
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }
}
