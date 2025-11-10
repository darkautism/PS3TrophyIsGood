using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace PS3TrophyIsGood
{
    public partial class CopyFrom : Form
    {
        /// <summary>
        /// Class for havian a pair for use it later (you could do it generic as c++ Pair but to lazy
        /// </summary>
        public class Pair
        {
            public int Id { get; set; }
            public long Date { get; set; }

            public Pair(int id, long date)
            {
                Id = id;
                Date = date;
            }
        }

        public CopyFrom()
        {
            InitializeComponent();
            groupBox1.Visible = false;
        }

        /// <summary>
        /// This get the timestamp from a profile(asuming is a legit one) then modify them to looks like they are legit but not a comple copy
        /// </summary>
        /// <returns></returns>
        public IEnumerable<long> smartCopy()
        {
            var trophies = copyFrom(textBox1.Text).ToList();
            trophies.Sort((a, b) => a.Date.CompareTo(b.Date));
            var rand = new Random();
            var time =
                TimeSpan.FromDays(
                    (long)(yearsNumeric.Value * 365 + monthNumeric.Value * 30 + daysNumeric.Value)
                ) + TimeSpan.FromSeconds(rand.Next((int)minMinutes.Value, (int)maxMinutes.Value));
            var delta = Convert.ToInt64(time.TotalSeconds);
            for (int i = 0; i < trophies.Count - 1; ++i)
            {
                if (trophies[i].Date == 0)
                    continue;
                trophies[i].Date += delta;
                if (trophies[i + 1].Date - trophies[i].Date > 60)
                    delta += rand.Next((int)minMinutes.Value, (int)maxMinutes.Value);
            }
            trophies[trophies.Count - 1].Date += delta;
            trophies.Sort((a, b) => a.Id.CompareTo(b.Id));
            return trophies.Select(d => d.Date);
        }

        public IEnumerable<long> copyFrom() => copyFrom(textBox1.Text).Select(p => p.Date);

        /// <summary>
        /// Just parse and get the timestamps from a profile from https://psntrophyleaders.com
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        private IEnumerable<Pair> copyFrom(string targetUrl)
        {
            // 等待直到 "Serving on" 出現
            Console.WriteLine("等待服務啟動中...");
            Utility.servingReady.WaitOne();

            Console.WriteLine("服務已啟動，繼續執行後續邏輯...");
            // 這裡可以繼續執行其他操作，例如發送 HTTP 請求等


            int i = 0;
            Regex regex = new Regex(
                "<td class=\"date_earned\">\\s+<span class=\"sort\">\\d+</span>"
            );

            // 建立 FlareSolverr 的請求 JSON
            string jsonPayload =
                $@"
            {{
                ""cmd"": ""request.get"",
                ""url"": ""{targetUrl}"",
                ""maxTimeout"": 60000
            }}";

            using (WebClient client = new WebClient())
            {
                client.Headers.Add("Content-Type", "application/json");
                string response = client.UploadString("http://localhost:8191/v1", jsonPayload);

                // 解析 FlareSolverr 回傳的 HTML
                var json = System.Text.Json.JsonDocument.Parse(response);
                string html = json
                    .RootElement.GetProperty("solution")
                    .GetProperty("response")
                    .GetString();

                var matches = regex.Matches(html);
                foreach (Match match in matches)
                {
                    yield return new Pair(
                        i++,
                        long.Parse(Regex.Match(match.Value, "\\d+").ToString())
                    );
                }
            }

            MessageBox.Show(Properties.strings.CopiedSuccessfully);
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox1.Checked)
                groupBox1.Visible = true;
            else
            {
                groupBox1.Visible = false;
                daysNumeric.Value = 0;
                monthNumeric.Value = 0;
                yearsNumeric.Value = 0;
                minMinutes.Value = 0;
                maxMinutes.Value = 0;
            }
        }

        private void accept_Click(object sender, EventArgs e)
        {
            if (minMinutes.Value > maxMinutes.Value)
                MessageBox.Show(Properties.strings.MinCantBeGreaterThanMax);
            else if (
                Regex.IsMatch(
                    textBox1.Text,
                    "https://psntrophyleaders.com/user/view/" + "\\S+/\\S+"
                )
            )
                DialogResult = DialogResult.OK;
            else
                MessageBox.Show(Properties.strings.CantFindGame);
        }
    }
}
