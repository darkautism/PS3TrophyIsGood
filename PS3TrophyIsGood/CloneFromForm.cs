using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace PS3TrophyIsGood
{
    public partial class CloneFromForm : Form
    {
        public CloneFromForm()
        {
            InitializeComponent();
        }


        public IEnumerable<long> cloneFrom(string url)
        {
            Regex regex = new Regex("<td class=\"date_earned\">\\s+<span class=\"sort\">\\d+</span>");
            using (WebClient client = new WebClient()) // WebClient class inherits IDisposable
            {
                client.Headers.Add("User-Agent: Other");

                var x = regex.Matches(client.DownloadString(url));
                foreach (Match match in x)
                    yield return long.Parse(Regex.Match(match.Value, "\\d+").ToString());
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(textBox1.Text)) MessageBox.Show("Link can't be empty");
            else this.DialogResult = DialogResult.OK;
        }
    }
}
