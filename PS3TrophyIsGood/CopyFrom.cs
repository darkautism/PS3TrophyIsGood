using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PS3TrophyIsGood
{
    public partial class CopyFrom : Form
    {
        private const int CompactHeight = 172;
        private const int SmartCopyHeight = 312;
        private const int VerificationHeight = 288;
        private const int MaxChallengeReloads = 5;

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
        private Button verificationRetryButton;
        private Label statusLabel;
        private ProgressBar helperProgress;
        private WebView2 verificationWebView;
        private Timer verificationPollTimer;
        private string verificationTargetUrl;
        private bool helperReady;
        private bool preparing;
        private bool verificationActive;
        private bool verificationCheckingCookies;
        private bool verificationClearanceDetected;
        private bool verificationFinishing;
        private bool verificationPaused;
        private bool verificationPostClearanceNavigateIssued;
        private int verificationTargetNavigations;
        private DateTime verificationNavigationWindowStart;
        private DateTime? verificationClearanceDetectedAt;

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
            VisibleChanged += CopyFrom_VisibleChanged;
        }

        private void BuildHelperUi()
        {
            AutoSize = false;
            ClientSize = new Size(430, CompactHeight);

            label6.Location = new Point(13, 8);
            textBox1.Location = new Point(13, 27);
            textBox1.Size = new Size(404, 22);

            statusLabel = new Label
            {
                AutoEllipsis = true,
                Location = new Point(13, 58),
                Size = new Size(404, 30),
                Text = "Helper not started. Nothing will be downloaded until you press Start."
            };

            helperProgress = new ProgressBar
            {
                Location = new Point(13, 91),
                Size = new Size(404, 10),
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Style = ProgressBarStyle.Continuous
            };

            startButton = new Button
            {
                Location = new Point(13, 109),
                Size = new Size(75, 23),
                Text = "Start",
                UseVisualStyleBackColor = true
            };
            startButton.Click += startButton_Click;

            verificationRetryButton = new Button
            {
                Location = new Point(235, 251),
                Size = new Size(99, 23),
                Text = "Retry",
                UseVisualStyleBackColor = true,
                Visible = false
            };
            verificationRetryButton.Click += verificationRetryButton_Click;

            accept.Location = new Point(96, 109);
            accept.Size = new Size(110, 23);
            accept.Text = "Copy trophies";

            button2.Location = new Point(342, 109);
            button2.Size = new Size(75, 23);

            checkBox1.Location = new Point(13, 141);
            groupBox1.Location = new Point(13, 166);
            groupBox1.Size = new Size(404, 133);

            verificationPollTimer = new Timer { Interval = 1000 };
            verificationPollTimer.Tick += verificationPollTimer_Tick;

            Controls.Add(statusLabel);
            Controls.Add(helperProgress);
            Controls.Add(startButton);
            Controls.Add(verificationRetryButton);

            label6.Text = "PSN Trophy Leaders URL:";
        }

        private void CopyFrom_VisibleChanged(object sender, EventArgs e)
        {
            if (Visible)
                ResetDialogState();
            else
            {
                StopEmbeddedVerification(true);
                ReleaseHelper();
            }
        }

        private void ResetDialogState()
        {
            StopEmbeddedVerification(true);
            ReleaseHelper();
            loadedTrophies.Clear();
            helperReady = false;
            preparing = false;
            DialogResult = DialogResult.None;

            ExitVerificationLayout();
            textBox1.Text = string.Empty;
            textBox1.Enabled = false;
            accept.Enabled = false;
            checkBox1.Checked = false;
            checkBox1.Enabled = false;
            groupBox1.Visible = false;
            startButton.Enabled = true;
            button2.Enabled = true;
            helperProgress.Style = ProgressBarStyle.Continuous;
            helperProgress.Value = 0;
            statusLabel.Text = "Helper not started. Nothing will be downloaded until you press Start.";
            UpdateDialogHeight();
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
            helperProgress.Visible = true;
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
            List<Pair> trophies = loadedTrophies.Select(t => new Pair(t.Id, t.Date)).ToList();
            if (trophies.Count == 0)
                return Enumerable.Empty<long>();

            trophies.Sort((a, b) => a.Date.CompareTo(b.Date));
            Random rand = new Random();
            TimeSpan time = TimeSpan.FromDays(
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
            return loadedTrophies.OrderBy(p => p.Id).Select(p => p.Date).ToList();
        }

        private async Task<List<Pair>> FetchTrophiesAsync(string targetUrl)
        {
            FlareSolverrManager.PageResult page = await helper.RequestPageAsync(targetUrl);
            return ParseTrophyPage(page);
        }

        private static List<Pair> ParseTrophyPage(FlareSolverrManager.PageResult page)
        {
            if (LooksLikeCloudflareChallenge(page.Html))
                throw new CloudflareChallengeException();

            List<Pair> trophies = ParseTrophyDates(page.Html);
            if (trophies.Count == 0)
            {
                string statusSuffix = page.HttpStatus > 0 ? " (HTTP " + page.HttpStatus + ")" : string.Empty;
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
                   lower.Contains("cf-turnstile") ||
                   (lower.Contains("just a moment") && lower.Contains("cloudflare"));
        }

        private void ValidateTrophyCount(List<Pair> trophies)
        {
            if (ExpectedTrophyCount > 0 && trophies.Count != ExpectedTrophyCount)
            {
                throw new InvalidOperationException(
                    "PSN Trophy Leaders returned " + trophies.Count +
                    " trophy timestamps, but the local trophy set contains " + ExpectedTrophyCount +
                    ". The page format or trophy mapping changed. No trophies were modified."
                );
            }
        }

        private void CompleteCopy(List<Pair> trophies)
        {
            StopEmbeddedVerification(false);
            loadedTrophies.Clear();
            loadedTrophies.AddRange(trophies);
            helperProgress.Style = ProgressBarStyle.Continuous;
            helperProgress.Value = 100;
            statusLabel.Text = "Loaded " + trophies.Count + " trophy timestamps.";
            DialogResult = DialogResult.OK;
        }

        private async Task ShowEmbeddedVerificationAsync(string targetUrl)
        {
            verificationTargetUrl = targetUrl;
            verificationActive = true;
            verificationPaused = false;
            verificationCheckingCookies = false;
            verificationClearanceDetected = false;
            verificationFinishing = false;
            verificationPostClearanceNavigateIssued = false;
            verificationClearanceDetectedAt = null;
            verificationTargetNavigations = 0;
            verificationNavigationWindowStart = DateTime.UtcNow;

            EnterVerificationLayout();
            statusLabel.Text = "Complete the Cloudflare checkbox below. It will close automatically.";

            try
            {
                await EnsureVerificationWebViewAsync();
                if (!Visible || !verificationActive)
                    return;

                verificationWebView.Visible = true;
                verificationWebView.ZoomFactor = 0.90;
                verificationWebView.BringToFront();
                verificationRetryButton.BringToFront();
                button2.BringToFront();
                verificationPollTimer.Start();
                verificationWebView.CoreWebView2.Navigate(targetUrl);
            }
            catch (Exception ex)
            {
                HandleVerificationFailure(new InvalidOperationException(
                    "Embedded verification could not start. Microsoft Edge WebView2 Runtime is required.", ex));
            }
        }

        private async Task EnsureVerificationWebViewAsync()
        {
            if (verificationWebView != null && verificationWebView.CoreWebView2 != null)
                return;

            if (verificationWebView == null)
            {
                verificationWebView = new WebView2
                {
                    Location = new Point(13, 45),
                    Size = new Size(404, 198),
                    Visible = false,
                    TabStop = true
                };
                verificationWebView.NavigationCompleted += verificationWebView_NavigationCompleted;
                Controls.Add(verificationWebView);
            }

            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PS3TrophyIsGood",
                "WebView2"
            );
            Directory.CreateDirectory(userDataFolder);

            CoreWebView2Environment.GetAvailableBrowserVersionString();
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await verificationWebView.EnsureCoreWebView2Async(environment);

            CoreWebView2Settings settings = verificationWebView.CoreWebView2.Settings;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;

            verificationWebView.CoreWebView2.NavigationStarting += verificationWebView_NavigationStarting;
            verificationWebView.CoreWebView2.NewWindowRequested += verificationWebView_NewWindowRequested;
            verificationWebView.CoreWebView2.ProcessFailed += verificationWebView_ProcessFailed;
        }

        private void verificationWebView_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (!IsAllowedVerificationUri(e.Uri))
            {
                e.Cancel = true;
                return;
            }

            if (!verificationActive || verificationClearanceDetected || verificationPaused || !IsPsntrophyLeadersUri(e.Uri))
                return;

            DateTime now = DateTime.UtcNow;
            if (now - verificationNavigationWindowStart > TimeSpan.FromSeconds(30))
            {
                verificationNavigationWindowStart = now;
                verificationTargetNavigations = 0;
            }

            verificationTargetNavigations++;
            if (verificationTargetNavigations <= MaxChallengeReloads)
                return;

            e.Cancel = true;
            PauseVerificationLoop();
        }

        private void verificationWebView_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
        }

        private void verificationWebView_ProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
        {
            if (!verificationActive || IsDisposed || Disposing)
                return;

            BeginInvoke(new Action(delegate
            {
                HandleVerificationFailure(new InvalidOperationException("The embedded verification browser stopped unexpectedly."));
            }));
        }

        private async void verificationWebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!verificationActive || verificationPaused || !e.IsSuccess)
                return;

            if (verificationClearanceDetected)
                await ContinueAfterClearanceAsync(true);
            else
                await CheckForClearanceAsync();
        }

        private async void verificationPollTimer_Tick(object sender, EventArgs e)
        {
            if (verificationClearanceDetected)
                await ContinueAfterClearanceAsync(false);
            else
                await CheckForClearanceAsync();
        }

        private async Task CheckForClearanceAsync()
        {
            if (!verificationActive || verificationPaused || verificationClearanceDetected || verificationCheckingCookies ||
                verificationWebView == null || verificationWebView.CoreWebView2 == null)
                return;

            verificationCheckingCookies = true;
            try
            {
                IReadOnlyList<CoreWebView2Cookie> cookies = await verificationWebView.CoreWebView2.CookieManager
                    .GetCookiesAsync("https://psntrophyleaders.com/");

                if (!verificationActive || verificationPaused)
                    return;

                bool hasClearance = cookies.Any(cookie =>
                    string.Equals(cookie.Name, "cf_clearance", StringComparison.OrdinalIgnoreCase));

                if (!hasClearance)
                {
                    statusLabel.Text = "Complete the Cloudflare checkbox below. Waiting for verification...";
                    return;
                }

                verificationClearanceDetected = true;
                verificationClearanceDetectedAt = DateTime.UtcNow;
                verificationPostClearanceNavigateIssued = false;
                verificationTargetNavigations = 0;
                statusLabel.Text = "Verification passed. Waiting for Cloudflare to finish...";
            }
            catch (Exception ex)
            {
                if (verificationActive)
                    HandleVerificationFailure(ex);
            }
            finally
            {
                verificationCheckingCookies = false;
            }
        }

        private async Task ContinueAfterClearanceAsync(bool navigationCompleted)
        {
            if (!verificationActive || verificationPaused || verificationFinishing || !verificationClearanceDetected ||
                verificationWebView == null || verificationWebView.CoreWebView2 == null)
                return;

            DateTime detectedAt = verificationClearanceDetectedAt ?? DateTime.UtcNow;
            TimeSpan elapsed = DateTime.UtcNow - detectedAt;
            if (elapsed < TimeSpan.FromMilliseconds(1200))
                return;

            string currentUrl = verificationWebView.CoreWebView2.Source;
            if (!IsPsntrophyLeadersUri(currentUrl))
                return;

            verificationFinishing = true;
            try
            {
                if (navigationCompleted)
                {
                    await Task.Delay(500);
                    if (!verificationActive || verificationPaused)
                        return;
                    elapsed = DateTime.UtcNow - detectedAt;
                }

                if (!verificationPostClearanceNavigateIssued && elapsed >= TimeSpan.FromSeconds(3))
                {
                    verificationPostClearanceNavigateIssued = true;
                    statusLabel.Text = "Verification passed. Opening the trophy page...";
                    verificationWebView.CoreWebView2.Navigate(verificationTargetUrl);
                    return;
                }

                string htmlJson = await verificationWebView.CoreWebView2.ExecuteScriptAsync(
                    "document.documentElement ? document.documentElement.outerHTML : ''"
                );
                string html = JsonSerializer.Deserialize<string>(htmlJson) ?? string.Empty;

                if (LooksLikeCloudflareChallenge(html))
                {
                    elapsed = DateTime.UtcNow - detectedAt;
                    if (elapsed < TimeSpan.FromSeconds(15))
                    {
                        statusLabel.Text = "Verification passed. Cloudflare is finishing the redirect...";
                        return;
                    }

                    PauseVerificationLoop(
                        "Verification succeeded, but Cloudflare did not leave the challenge page. Click Retry to continue."
                    );
                    return;
                }

                List<Pair> trophies = ParseTrophyDates(html);
                if (trophies.Count == 0)
                {
                    elapsed = DateTime.UtcNow - detectedAt;
                    if (elapsed < TimeSpan.FromSeconds(15))
                    {
                        statusLabel.Text = "Verification passed. Waiting for the trophy page contents...";
                        return;
                    }

                    throw new InvalidOperationException(
                        "Cloudflare verification passed, but no trophy timestamps were found. The site format may have changed."
                    );
                }

                ValidateTrophyCount(trophies);
                CompleteCopy(trophies);
            }
            catch (Exception ex)
            {
                if (verificationActive)
                    HandleVerificationFailure(ex);
            }
            finally
            {
                verificationFinishing = false;
            }
        }

        private void PauseVerificationLoop(string message = null)
        {
            if (!verificationActive || verificationPaused)
                return;

            verificationPaused = true;
            verificationPollTimer.Stop();
            try
            {
                if (verificationWebView != null && verificationWebView.CoreWebView2 != null)
                    verificationWebView.CoreWebView2.Stop();
            }
            catch
            {
            }

            statusLabel.Text = message ?? "Cloudflare rejected the embedded browser repeatedly. Automatic reloads were stopped.";
            verificationRetryButton.Visible = true;
            verificationRetryButton.Enabled = true;
            verificationRetryButton.BringToFront();
            button2.BringToFront();
        }

        private void verificationRetryButton_Click(object sender, EventArgs e)
        {
            if (!verificationActive || string.IsNullOrEmpty(verificationTargetUrl) ||
                verificationWebView == null || verificationWebView.CoreWebView2 == null)
                return;

            verificationPaused = false;
            verificationClearanceDetected = false;
            verificationClearanceDetectedAt = null;
            verificationPostClearanceNavigateIssued = false;
            verificationFinishing = false;
            verificationTargetNavigations = 0;
            verificationNavigationWindowStart = DateTime.UtcNow;
            verificationRetryButton.Visible = false;
            statusLabel.Text = "Retrying Cloudflare verification...";
            verificationPollTimer.Start();
            verificationWebView.CoreWebView2.Navigate(verificationTargetUrl);
        }

        private static bool IsAllowedVerificationUri(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "about:blank")
                return true;

            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri))
                return false;

            string host = uri.Host ?? string.Empty;
            return host.Equals("psntrophyleaders.com", StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith(".psntrophyleaders.com", StringComparison.OrdinalIgnoreCase) ||
                   host.Equals("challenges.cloudflare.com", StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith(".cloudflare.com", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPsntrophyLeadersUri(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri))
                return false;

            string host = uri.Host ?? string.Empty;
            return host.Equals("psntrophyleaders.com", StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith(".psntrophyleaders.com", StringComparison.OrdinalIgnoreCase);
        }

        private void EnterVerificationLayout()
        {
            label6.Visible = false;
            textBox1.Visible = false;
            helperProgress.Visible = false;
            startButton.Visible = false;
            accept.Visible = false;
            checkBox1.Visible = false;
            groupBox1.Visible = false;
            verificationRetryButton.Visible = false;

            statusLabel.Location = new Point(13, 8);
            statusLabel.Size = new Size(404, 30);
            button2.Location = new Point(342, 251);
            button2.Visible = true;
            button2.Enabled = true;
            ClientSize = new Size(430, VerificationHeight);
        }

        private void ExitVerificationLayout()
        {
            label6.Visible = true;
            textBox1.Visible = true;
            helperProgress.Visible = true;
            startButton.Visible = true;
            accept.Visible = true;
            checkBox1.Visible = true;
            verificationRetryButton.Visible = false;

            statusLabel.Location = new Point(13, 58);
            statusLabel.Size = new Size(404, 30);
            button2.Location = new Point(342, 109);
            button2.Visible = true;
            groupBox1.Visible = checkBox1.Checked;

            if (verificationWebView != null)
                verificationWebView.Visible = false;

            UpdateDialogHeight();
        }

        private void StopEmbeddedVerification(bool disposeBrowser)
        {
            verificationActive = false;
            verificationPaused = false;
            verificationCheckingCookies = false;
            verificationClearanceDetected = false;
            verificationClearanceDetectedAt = null;
            verificationPostClearanceNavigateIssued = false;
            verificationFinishing = false;
            verificationTargetNavigations = 0;
            verificationTargetUrl = null;
            if (verificationPollTimer != null)
                verificationPollTimer.Stop();

            verificationRetryButton.Visible = false;

            if (verificationWebView != null)
            {
                verificationWebView.Visible = false;
                if (disposeBrowser)
                {
                    Controls.Remove(verificationWebView);
                    verificationWebView.Dispose();
                    verificationWebView = null;
                }
            }
        }

        private void HandleVerificationFailure(Exception ex)
        {
            StopEmbeddedVerification(false);
            ExitVerificationLayout();
            RestoreReadyControls();
            statusLabel.Text = GetUsefulMessage(ex);
            MessageBox.Show(this, statusLabel.Text, "Copy From", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void SetBusy(string status)
        {
            statusLabel.Text = status;
            textBox1.Enabled = false;
            accept.Enabled = false;
            checkBox1.Enabled = false;
            startButton.Enabled = false;
            button2.Enabled = true;
            helperProgress.Visible = true;
            helperProgress.Style = ProgressBarStyle.Marquee;
        }

        private void RestoreReadyControls()
        {
            helperProgress.Visible = true;
            helperProgress.Style = ProgressBarStyle.Continuous;
            helperProgress.Value = 0;
            textBox1.Enabled = helperReady;
            accept.Enabled = helperReady;
            checkBox1.Enabled = helperReady;
            startButton.Enabled = !helperReady;
            button2.Enabled = true;
            UpdateDialogHeight();
        }

        private void UpdateDialogHeight()
        {
            if (verificationActive)
                return;

            ClientSize = new Size(430, checkBox1.Checked ? SmartCopyHeight : CompactHeight);
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

            UpdateDialogHeight();
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

            string targetUrl = textBox1.Text;
            SetBusy("Loading trophy page from PSN Trophy Leaders...");

            try
            {
                List<Pair> trophies = await FetchTrophiesAsync(targetUrl);
                if (!Visible)
                    return;

                ValidateTrophyCount(trophies);
                CompleteCopy(trophies);
            }
            catch (CloudflareChallengeException)
            {
                if (!Visible)
                    return;

                await ShowEmbeddedVerificationAsync(targetUrl);
            }
            catch (Exception ex)
            {
                if (!Visible)
                    return;

                RestoreReadyControls();
                statusLabel.Text = GetUsefulMessage(ex);
                MessageBox.Show(this, statusLabel.Text, "Copy From", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private sealed class CloudflareChallengeException : Exception
        {
        }
    }
}
