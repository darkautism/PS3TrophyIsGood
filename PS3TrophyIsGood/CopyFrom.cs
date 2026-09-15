using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PS3TrophyIsGood
{
    public partial class CopyFrom : Form
    {
        private static readonly Regex DateCellRegex = new Regex(
            @"<td\b(?=[^>]*\bclass\s*=\s*[""'][^""']*\bdate_earned\b[^""']*[""'])[^>]*>(?<body>.*?)</td>|<div\b(?=[^>]*\bclass\s*=\s*[""'][^""']*\bdate_earned\b[^""']*[""'])[^>]*>(?<body>.*?)</div>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled
        );

        private static readonly Regex SortValueRegex = new Regex(
            @"<span\b(?=[^>]*\bclass\s*=\s*[""'][^""']*\bsort\b[^""']*[""'])[^>]*>\s*(?<value>\d+)\s*</span>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled
        );

        private static readonly Regex SortAttributeRegex = new Regex(
            @"\b(?:data-sort|data-order)\s*=\s*[""'](?<value>\d+)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        private readonly List<Pair> loadedTrophies = new List<Pair>();
        private FlareSolverrManager helper;
        private Button startButton;
        private Label statusLabel;
        private ProgressBar helperProgress;
        private bool helperReady;
        private bool preparing;

        public int ExpectedTrophyCount { get; set; }

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
            BuildHelperUi();
            groupBox1.Visible = false;
            Shown += CopyFrom_Shown;
            VisibleChanged += CopyFrom_VisibleChanged;
        }

        private void BuildHelperUi()
        {
            AutoSize = false;
            ClientSize = new Size(430, 344);

            label6.Location = new Point(13, 10);
            textBox1.Location = new Point(13, 30);
            textBox1.Size = new Size(404, 22);

            statusLabel = new Label
            {
                AutoEllipsis = true,
                Location = new Point(13, 65),
                Size = new Size(404, 34),
                Text = "Helper not started. Nothing will be downloaded until you press Start."
            };

            helperProgress = new ProgressBar
            {
                Location = new Point(13, 102),
                Size = new Size(404, 14),
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Style = ProgressBarStyle.Continuous
            };

            startButton = new Button
            {
                Location = new Point(13, 124),
                Size = new Size(75, 23),
                Text = "Start",
                UseVisualStyleBackColor = true
            };
            startButton.Click += startButton_Click;

            accept.Location = new Point(96, 124);
            accept.Size = new Size(104, 23);
            accept.Text = "Copy trophies";

            checkBox1.Location = new Point(210, 127);
            button2.Location = new Point(342, 124);
            button2.Size = new Size(75, 23);

            groupBox1.Location = new Point(13, 157);
            groupBox1.Size = new Size(404, 133);

            Controls.Add(statusLabel);
            Controls.Add(helperProgress);
            Controls.Add(startButton);

            label6.Text = "PSN Trophy Leaders URL:";
        }

        private void CopyFrom_Shown(object sender, EventArgs e)
        {
            ResetDialogState();
        }

        private void CopyFrom_VisibleChanged(object sender, EventArgs e)
        {
            if (!Visible)
                ReleaseHelper();
        }

        private void ResetDialogState()
        {
            ReleaseHelper();
            loadedTrophies.Clear();
            helperReady = false;
            preparing = false;
            DialogResult = DialogResult.None;

            textBox1.Enabled = false;
            accept.Enabled = false;
            checkBox1.Enabled = false;
            startButton.Enabled = true;
            button2.Enabled = true;
            helperProgress.Style = ProgressBarStyle.Continuous;
            helperProgress.Value = 0;
            statusLabel.Text = "Helper not started. Nothing will be downloaded until you press Start.";
        }

        private async void startButton_Click(object sender, EventArgs e)
        {
            if (preparing)
                return;

            preparing = true;
            startButton.Enabled = false;
            textBox1.Enabled = false;
            accept.Enabled = false;
            checkBox1.Enabled = false;
            helperProgress.Style = ProgressBarStyle.Continuous;
            helperProgress.Value = 0;

            ReleaseHelper();
            helper = new FlareSolverrManager();
            helper.StatusChanged += Helper_StatusChanged;
            helper.ProgressChanged += Helper_ProgressChanged;

            try
            {
                await helper.EnsureReadyAsync();
                if (!Visible)
                    return;

                helperReady = true;
                textBox1.Enabled = true;
                accept.Enabled = true;
                checkBox1.Enabled = true;
                startButton.Enabled = false;
                textBox1.Focus();
            }
            catch (Exception ex)
            {
                if (!Visible)
                    return;

                helperReady = false;
                statusLabel.Text = "FlareSolverr unavailable: " + GetUsefulMessage(ex);
                helperProgress.Style = ProgressBarStyle.Continuous;
                helperProgress.Value = 0;
                startButton.Enabled = true;
                MessageBox.Show(this, statusLabel.Text, "Copy From", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ReleaseHelper();
            }
            finally
            {
                preparing = false;
            }
        }

        private void Helper_StatusChanged(string status)
        {
            if (IsDisposed || Disposing)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(Helper_StatusChanged), status);
                return;
            }

            statusLabel.Text = status;
        }

        private void Helper_ProgressChanged(int progress)
        {
            if (IsDisposed || Disposing)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action<int>(Helper_ProgressChanged), progress);
                return;
            }

            helperProgress.Style = ProgressBarStyle.Continuous;
            helperProgress.Value = Math.Max(helperProgress.Minimum, Math.Min(helperProgress.Maximum, progress));
        }

        public IEnumerable<long> smartCopy()
        {
            List<Pair> trophies = loadedTrophies
                .Select(t => new Pair(t.Id, t.Date))
                .ToList();

            if (trophies.Count == 0)
                return Enumerable.Empty<long>();

            trophies.Sort((a, b) => a.Date.CompareTo(b.Date));
            Random rand = new Random();
            TimeSpan time =
                TimeSpan.FromDays(
                    (long)(yearsNumeric.Value * 365 + monthNumeric.Value * 30 + daysNumeric.Value)
                ) + TimeSpan.FromSeconds(rand.Next((int)minMinutes.Value, (int)maxMinutes.Value));
            long delta = Convert.ToInt64(time.TotalSeconds);

            for (int i = 0; i < trophies.Count - 1; ++i)
            {
                if (trophies[i].Date == 0)
                    continue;

                trophies[i].Date += delta;
                if (trophies[i + 1].Date - trophies[i].Date > 60)
                    delta += rand.Next((int)minMinutes.Value, (int)maxMinutes.Value);
            }

            if (trophies[trophies.Count - 1].Date != 0)
                trophies[trophies.Count - 1].Date += delta;

            trophies.Sort((a, b) => a.Id.CompareTo(b.Id));
            return trophies.Select(d => d.Date).ToList();
        }

        public IEnumerable<long> copyFrom()
        {
            return loadedTrophies
                .OrderBy(p => p.Id)
                .Select(p => p.Date)
                .ToList();
        }

        private async Task<List<Pair>> FetchTrophiesAsync(string targetUrl)
        {
            string jsonPayload = JsonSerializer.Serialize(new
            {
                cmd = "request.get",
                url = targetUrl,
                maxTimeout = 60000
            });

            string response;
            using (WebClient client = new WebClient())
            {
                client.Headers.Add(HttpRequestHeader.ContentType, "application/json");
                response = await client.UploadStringTaskAsync(
                    new Uri("http://127.0.0.1:8191/v1"),
                    "POST",
                    jsonPayload
                );
            }

            string html;
            int httpStatus = 0;
            using (JsonDocument json = JsonDocument.Parse(response))
            {
                JsonElement root = json.RootElement;
                JsonElement statusElement;
                if (root.TryGetProperty("status", out statusElement) &&
                    statusElement.ValueKind == JsonValueKind.String &&
                    !string.Equals(statusElement.GetString(), "ok", StringComparison.OrdinalIgnoreCase))
                {
                    JsonElement messageElement;
                    string message = root.TryGetProperty("message", out messageElement) && messageElement.ValueKind == JsonValueKind.String
                        ? messageElement.GetString()
                        : "unknown FlareSolverr error";
                    throw new InvalidOperationException("FlareSolverr request failed: " + message);
                }

                JsonElement solution;
                JsonElement htmlElement;
                if (!root.TryGetProperty("solution", out solution) ||
                    !solution.TryGetProperty("response", out htmlElement) ||
                    htmlElement.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidOperationException("FlareSolverr returned no page HTML.");
                }

                html = htmlElement.GetString();
                JsonElement statusCodeElement;
                if (solution.TryGetProperty("status", out statusCodeElement) && statusCodeElement.ValueKind == JsonValueKind.Number)
                    statusCodeElement.TryGetInt32(out httpStatus);
            }

            if (LooksLikeCloudflareChallenge(html))
            {
                throw new InvalidOperationException(
                    "PSN Trophy Leaders returned a Cloudflare human-verification page instead of the trophy page."
                );
            }

            List<Pair> trophies = ParseTrophyDates(html);
            if (trophies.Count == 0)
            {
                string statusSuffix = httpStatus > 0 ? " (HTTP " + httpStatus + ")" : string.Empty;
                throw new InvalidOperationException(
                    "PSN Trophy Leaders returned a page, but no trophy timestamps could be parsed" + statusSuffix + ". The site format may have changed."
                );
            }

            return trophies;
        }

        private static List<Pair> ParseTrophyDates(string html)
        {
            List<Pair> trophies = new List<Pair>();
            MatchCollection cells = DateCellRegex.Matches(html ?? string.Empty);

            for (int i = 0; i < cells.Count; i++)
            {
                Match cell = cells[i];
                Match timestamp = SortValueRegex.Match(cell.Groups["body"].Value);
                if (!timestamp.Success)
                    timestamp = SortAttributeRegex.Match(cell.Value);

                long value;
                if (!timestamp.Success || !long.TryParse(timestamp.Groups["value"].Value, out value))
                {
                    throw new InvalidOperationException(
                        "PSN Trophy Leaders trophy row " + (i + 1) + " no longer contains the expected earned timestamp. The site format changed."
                    );
                }

                trophies.Add(new Pair(i, value));
            }

            return trophies;
        }

        private static bool LooksLikeCloudflareChallenge(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return false;

            string lower = html.ToLowerInvariant();
            return lower.Contains("cf-chl-") ||
                   lower.Contains("challenge-platform") ||
                   lower.Contains("verify you are human") ||
                   lower.Contains("checking your browser") ||
                   (lower.Contains("just a moment") && lower.Contains("cloudflare"));
        }

        private static string GetUsefulMessage(Exception ex)
        {
            Exception current = ex;
            while (current.InnerException != null)
                current = current.InnerException;
            return current.Message;
        }

        private void ReleaseHelper()
        {
            if (helper == null)
                return;

            helper.StatusChanged -= Helper_StatusChanged;
            helper.ProgressChanged -= Helper_ProgressChanged;
            helper.Dispose();
            helper = null;
            helperReady = false;
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

        private async void accept_Click(object sender, EventArgs e)
        {
            if (!helperReady)
            {
                MessageBox.Show(this, "Press Start and wait until FlareSolverr is ready.", "Copy From");
                return;
            }

            if (minMinutes.Value > maxMinutes.Value)
            {
                MessageBox.Show(Properties.strings.MinCantBeGreaterThanMax);
                return;
            }

            if (!Regex.IsMatch(textBox1.Text, @"^https://psntrophyleaders\.com/user/view/[^/\s]+/[^\s]+$", RegexOptions.IgnoreCase))
            {
                MessageBox.Show(Properties.strings.CantFindGame);
                return;
            }

            accept.Enabled = false;
            startButton.Enabled = false;
            textBox1.Enabled = false;
            checkBox1.Enabled = false;
            button2.Enabled = false;
            helperProgress.Style = ProgressBarStyle.Marquee;
            statusLabel.Text = "Loading trophy page from PSN Trophy Leaders...";

            try
            {
                List<Pair> trophies = await FetchTrophiesAsync(textBox1.Text);
                if (!Visible)
                    return;

                if (ExpectedTrophyCount > 0 && trophies.Count != ExpectedTrophyCount)
                {
                    throw new InvalidOperationException(
                        "PSN Trophy Leaders returned " + trophies.Count +
                        " trophy timestamps, but the local trophy set contains " + ExpectedTrophyCount +
                        ". The page format or trophy mapping changed. No trophies were modified."
                    );
                }

                loadedTrophies.Clear();
                loadedTrophies.AddRange(trophies);
                helperProgress.Style = ProgressBarStyle.Continuous;
                helperProgress.Value = 100;
                statusLabel.Text = "Loaded " + trophies.Count + " trophy timestamps.";
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                if (!Visible)
                    return;

                helperProgress.Style = ProgressBarStyle.Continuous;
                helperProgress.Value = 0;
                statusLabel.Text = GetUsefulMessage(ex);
                MessageBox.Show(this, statusLabel.Text, "Copy From", MessageBoxButtons.OK, MessageBoxIcon.Error);
                accept.Enabled = true;
                textBox1.Enabled = true;
                checkBox1.Enabled = true;
                button2.Enabled = true;
            }
        }
    }
}
