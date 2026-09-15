using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PS3TrophyIsGood
{
    public partial class CopyFrom
    {
        private const int PsnProfilesMaxChallengeReloads = 5;

        private static readonly Regex PsnProfilesRowRegex = new Regex(
            @"<tr\b(?<attrs>[^>]*)>(?<body>.*?)</tr>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled
        );

        private static readonly Regex PsnProfilesTrophyLinkRegex = new Regex(
            @"href\s*=\s*[""']/trophy/\d+-[^/""']+/(?<id>\d+)-[^""']+[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        private static readonly Regex PsnProfilesEarnedRegex = new Regex(
            @"<picture\b(?=[^>]*\bclass\s*=\s*[""'][^""']*\btrophy\b[^""']*\bearned\b[^""']*[""'])",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled
        );

        private static readonly Regex PsnProfilesDateRegex = new Regex(
            @"<span\b(?=[^>]*\bclass\s*=\s*[""'][^""']*\btypo-top-date\b[^""']*[""'])[^>]*>\s*<nobr>(?<value>.*?)</nobr>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled
        );

        private static readonly Regex PsnProfilesTimeRegex = new Regex(
            @"<span\b(?=[^>]*\bclass\s*=\s*[""'][^""']*\btypo-bottom-date\b[^""']*[""'])[^>]*>\s*<nobr>(?<value>.*?)</nobr>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled
        );

        private bool sourceAwareHookInstalled;
        private WebView2 psnProfilesVerificationWebView;
        private Timer psnProfilesVerificationTimer;
        private Button psnProfilesRetryButton;
        private bool psnProfilesVerificationActive;
        private bool psnProfilesVerificationPaused;
        private bool psnProfilesCheckingCookies;
        private bool psnProfilesClearanceDetected;
        private bool psnProfilesPostClearanceNavigateIssued;
        private bool psnProfilesFinishing;
        private int psnProfilesTargetNavigations;
        private DateTime psnProfilesNavigationWindowStart;
        private DateTime? psnProfilesClearanceDetectedAt;
        private string psnProfilesVerificationTargetUrl;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            if (sourceAwareHookInstalled)
                return;

            sourceAwareHookInstalled = true;
            accept.Click -= accept_Click;
            accept.Click += accept_SourceAware_Click;
            label6.Text = "Trophy profile URL (PSN Trophy Leaders or PSNProfiles):";
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible)
                StopPsnProfilesVerification(true);
        }

        private async void accept_SourceAware_Click(object sender, EventArgs e)
        {
            string targetUrl = (textBox1.Text ?? string.Empty).Trim();
            if (!IsPsnProfilesTrophyUrl(targetUrl))
            {
                accept_Click(sender, e);
                return;
            }

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

            SetBusy("Loading trophy page from PSNProfiles...");

            try
            {
                FlareSolverrManager.PageResult page = await helper.RequestPageAsync(targetUrl);
                if (!Visible)
                    return;

                if (LooksLikeCloudflareChallenge(page.Html))
                {
                    await ShowPsnProfilesVerificationAsync(targetUrl);
                    return;
                }

                List<Pair> trophies = ParsePsnProfilesPage(page.Html, page.HttpStatus);
                ValidatePsnProfilesMapping(trophies);
                CompleteCopy(trophies);
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

        private static bool IsPsnProfilesTrophyUrl(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) || uri.Scheme != Uri.UriSchemeHttps)
                return false;

            string host = uri.Host ?? string.Empty;
            if (!host.Equals("psnprofiles.com", StringComparison.OrdinalIgnoreCase) &&
                !host.Equals("www.psnprofiles.com", StringComparison.OrdinalIgnoreCase))
                return false;

            string[] parts = uri.AbsolutePath.Trim('/').Split('/');
            if (parts.Length != 3 || !parts[0].Equals("trophies", StringComparison.OrdinalIgnoreCase))
                return false;

            return Regex.IsMatch(parts[1], @"^\d+-.+$") && !string.IsNullOrWhiteSpace(parts[2]);
        }

        private static List<Pair> ParsePsnProfilesPage(string html, int httpStatus)
        {
            List<Pair> trophies = new List<Pair>();
            MatchCollection rows = PsnProfilesRowRegex.Matches(html ?? string.Empty);

            foreach (Match row in rows)
            {
                string body = row.Groups["body"].Value;
                Match trophyLink = PsnProfilesTrophyLinkRegex.Match(body);
                if (!trophyLink.Success)
                    continue;

                if (!int.TryParse(trophyLink.Groups["id"].Value, out int siteTrophyId) || siteTrophyId <= 0)
                    throw new InvalidOperationException("PSNProfiles returned an invalid trophy number. No trophies were modified.");

                int localTrophyId = siteTrophyId - 1;
                string attrs = row.Groups["attrs"].Value;
                bool earned = Regex.IsMatch(
                                  attrs,
                                  @"\bclass\s*=\s*[""'][^""']*\bcompleted\b",
                                  RegexOptions.IgnoreCase
                              ) || PsnProfilesEarnedRegex.IsMatch(body);

                long timestamp = 0;
                if (earned)
                {
                    Match date = PsnProfilesDateRegex.Match(body);
                    Match time = PsnProfilesTimeRegex.Match(body);
                    if (!date.Success || !time.Success)
                    {
                        throw new InvalidOperationException(
                            "PSNProfiles trophy " + siteTrophyId +
                            " is marked earned but has no readable earned date. No trophies were modified."
                        );
                    }

                    timestamp = ParsePsnProfilesTimestamp(
                        date.Groups["value"].Value,
                        time.Groups["value"].Value,
                        siteTrophyId
                    );
                }

                trophies.Add(new Pair(localTrophyId, timestamp));
            }

            if (trophies.Count == 0)
            {
                string statusSuffix = httpStatus > 0 ? " (HTTP " + httpStatus + ")" : string.Empty;
                throw new InvalidOperationException(
                    "PSNProfiles returned a page, but no trophy rows could be parsed" +
                    statusSuffix + ". The site format may have changed."
                );
            }

            return trophies;
        }

        private static long ParsePsnProfilesTimestamp(string dateHtml, string timeHtml, int siteTrophyId)
        {
            string date = CleanPsnProfilesText(dateHtml);
            string time = CleanPsnProfilesText(timeHtml);
            date = Regex.Replace(date, @"(?<=\d)(st|nd|rd|th)\b", string.Empty, RegexOptions.IgnoreCase);

            string value = date + " " + time;
            string[] formats =
            {
                "d MMM yyyy h:mm:ss tt",
                "dd MMM yyyy h:mm:ss tt"
            };

            if (!DateTime.TryParseExact(
                value,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime parsed))
            {
                throw new InvalidOperationException(
                    "PSNProfiles trophy " + siteTrophyId +
                    " has an unsupported earned-date format: " + value + ". No trophies were modified."
                );
            }

            return new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Utc)).ToUnixTimeSeconds();
        }

        private static string CleanPsnProfilesText(string value)
        {
            string withoutTags = Regex.Replace(value ?? string.Empty, @"<[^>]+>", string.Empty);
            string decoded = WebUtility.HtmlDecode(withoutTags);
            return Regex.Replace(decoded, @"\s+", " ").Trim();
        }

        private void ValidatePsnProfilesMapping(List<Pair> trophies)
        {
            if (ExpectedTrophyCount <= 0)
                return;

            if (trophies.Count != ExpectedTrophyCount)
            {
                throw new InvalidOperationException(
                    "PSNProfiles returned " + trophies.Count +
                    " trophy rows, but the local trophy set contains " + ExpectedTrophyCount +
                    ". No trophies were modified."
                );
            }

            int[] ids = trophies.Select(t => t.Id).OrderBy(id => id).ToArray();
            for (int expectedId = 0; expectedId < ExpectedTrophyCount; expectedId++)
            {
                if (ids[expectedId] != expectedId)
                {
                    throw new InvalidOperationException(
                        "PSNProfiles trophy numbers no longer match the local set (expected IDs 1-" +
                        ExpectedTrophyCount + "). No trophies were modified."
                    );
                }
            }
        }

        private async Task ShowPsnProfilesVerificationAsync(string targetUrl)
        {
            psnProfilesVerificationTargetUrl = targetUrl;
            psnProfilesVerificationActive = true;
            psnProfilesVerificationPaused = false;
            psnProfilesCheckingCookies = false;
            psnProfilesClearanceDetected = false;
            psnProfilesPostClearanceNavigateIssued = false;
            psnProfilesFinishing = false;
            psnProfilesTargetNavigations = 0;
            psnProfilesNavigationWindowStart = DateTime.UtcNow;
            psnProfilesClearanceDetectedAt = null;

            EnterVerificationLayout();
            EnsurePsnProfilesRetryButton();
            statusLabel.Text = "Complete the PSNProfiles Cloudflare checkbox below. It will continue automatically.";

            try
            {
                await EnsurePsnProfilesVerificationWebViewAsync();
                if (!Visible || !psnProfilesVerificationActive)
                    return;

                psnProfilesVerificationWebView.Visible = true;
                psnProfilesVerificationWebView.ZoomFactor = 0.90;
                psnProfilesVerificationWebView.BringToFront();
                psnProfilesRetryButton.BringToFront();
                button2.BringToFront();
                psnProfilesVerificationTimer.Start();
                psnProfilesVerificationWebView.CoreWebView2.Navigate(targetUrl);
            }
            catch (Exception ex)
            {
                HandlePsnProfilesVerificationFailure(new InvalidOperationException(
                    "Embedded PSNProfiles verification could not start. Microsoft Edge WebView2 Runtime is required.", ex));
            }
        }

        private void EnsurePsnProfilesRetryButton()
        {
            if (psnProfilesRetryButton != null)
            {
                psnProfilesRetryButton.Visible = false;
                return;
            }

            psnProfilesRetryButton = new Button
            {
                Location = new System.Drawing.Point(235, 251),
                Size = new System.Drawing.Size(99, 23),
                Text = "Retry",
                UseVisualStyleBackColor = true,
                Visible = false
            };
            psnProfilesRetryButton.Click += psnProfilesRetryButton_Click;
            Controls.Add(psnProfilesRetryButton);
        }

        private async Task EnsurePsnProfilesVerificationWebViewAsync()
        {
            if (psnProfilesVerificationWebView != null && psnProfilesVerificationWebView.CoreWebView2 != null)
                return;

            if (psnProfilesVerificationTimer == null)
            {
                psnProfilesVerificationTimer = new Timer { Interval = 1000 };
                psnProfilesVerificationTimer.Tick += psnProfilesVerificationTimer_Tick;
            }

            if (psnProfilesVerificationWebView == null)
            {
                psnProfilesVerificationWebView = new WebView2
                {
                    Location = new System.Drawing.Point(13, 45),
                    Size = new System.Drawing.Size(404, 198),
                    Visible = false,
                    TabStop = true
                };
                psnProfilesVerificationWebView.NavigationCompleted += psnProfilesVerificationWebView_NavigationCompleted;
                Controls.Add(psnProfilesVerificationWebView);
            }

            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PS3TrophyIsGood",
                "WebView2"
            );
            Directory.CreateDirectory(userDataFolder);

            CoreWebView2Environment.GetAvailableBrowserVersionString();
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await psnProfilesVerificationWebView.EnsureCoreWebView2Async(environment);

            CoreWebView2Settings settings = psnProfilesVerificationWebView.CoreWebView2.Settings;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;

            psnProfilesVerificationWebView.CoreWebView2.NavigationStarting += psnProfilesVerificationWebView_NavigationStarting;
            psnProfilesVerificationWebView.CoreWebView2.NewWindowRequested += psnProfilesVerificationWebView_NewWindowRequested;
            psnProfilesVerificationWebView.CoreWebView2.ProcessFailed += psnProfilesVerificationWebView_ProcessFailed;
        }

        private void psnProfilesVerificationWebView_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (!IsAllowedPsnProfilesVerificationUri(e.Uri))
            {
                e.Cancel = true;
                return;
            }

            if (!psnProfilesVerificationActive || psnProfilesClearanceDetected || psnProfilesVerificationPaused ||
                !IsPsnProfilesHostUri(e.Uri))
                return;

            DateTime now = DateTime.UtcNow;
            if (now - psnProfilesNavigationWindowStart > TimeSpan.FromSeconds(30))
            {
                psnProfilesNavigationWindowStart = now;
                psnProfilesTargetNavigations = 0;
            }

            psnProfilesTargetNavigations++;
            if (psnProfilesTargetNavigations <= PsnProfilesMaxChallengeReloads)
                return;

            e.Cancel = true;
            PausePsnProfilesVerificationLoop();
        }

        private void psnProfilesVerificationWebView_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
        }

        private void psnProfilesVerificationWebView_ProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
        {
            if (!psnProfilesVerificationActive || IsDisposed || Disposing)
                return;

            BeginInvoke(new Action(delegate
            {
                HandlePsnProfilesVerificationFailure(
                    new InvalidOperationException("The embedded PSNProfiles verification browser stopped unexpectedly."));
            }));
        }

        private async void psnProfilesVerificationWebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!psnProfilesVerificationActive || psnProfilesVerificationPaused || !e.IsSuccess)
                return;

            if (psnProfilesClearanceDetected)
                await ContinueAfterPsnProfilesClearanceAsync(true);
            else
                await CheckForPsnProfilesClearanceAsync();
        }

        private async void psnProfilesVerificationTimer_Tick(object sender, EventArgs e)
        {
            if (psnProfilesClearanceDetected)
                await ContinueAfterPsnProfilesClearanceAsync(false);
            else
                await CheckForPsnProfilesClearanceAsync();
        }

        private async Task CheckForPsnProfilesClearanceAsync()
        {
            if (!psnProfilesVerificationActive || psnProfilesVerificationPaused || psnProfilesClearanceDetected ||
                psnProfilesCheckingCookies || psnProfilesVerificationWebView == null ||
                psnProfilesVerificationWebView.CoreWebView2 == null)
                return;

            psnProfilesCheckingCookies = true;
            try
            {
                IReadOnlyList<CoreWebView2Cookie> cookies = await psnProfilesVerificationWebView.CoreWebView2.CookieManager
                    .GetCookiesAsync("https://psnprofiles.com/");

                if (!psnProfilesVerificationActive || psnProfilesVerificationPaused)
                    return;

                bool hasClearance = cookies.Any(cookie =>
                    string.Equals(cookie.Name, "cf_clearance", StringComparison.OrdinalIgnoreCase));

                if (!hasClearance)
                {
                    statusLabel.Text = "Complete the PSNProfiles Cloudflare checkbox below. Waiting for verification...";
                    return;
                }

                psnProfilesClearanceDetected = true;
                psnProfilesClearanceDetectedAt = DateTime.UtcNow;
                psnProfilesPostClearanceNavigateIssued = false;
                psnProfilesTargetNavigations = 0;
                statusLabel.Text = "PSNProfiles verification passed. Waiting for Cloudflare to finish...";
            }
            catch (Exception ex)
            {
                if (psnProfilesVerificationActive)
                    HandlePsnProfilesVerificationFailure(ex);
            }
            finally
            {
                psnProfilesCheckingCookies = false;
            }
        }

        private async Task ContinueAfterPsnProfilesClearanceAsync(bool navigationCompleted)
        {
            if (!psnProfilesVerificationActive || psnProfilesVerificationPaused || psnProfilesFinishing ||
                !psnProfilesClearanceDetected || psnProfilesVerificationWebView == null ||
                psnProfilesVerificationWebView.CoreWebView2 == null)
                return;

            DateTime detectedAt = psnProfilesClearanceDetectedAt ?? DateTime.UtcNow;
            TimeSpan elapsed = DateTime.UtcNow - detectedAt;
            if (elapsed < TimeSpan.FromMilliseconds(1200))
                return;

            string currentUrl = psnProfilesVerificationWebView.CoreWebView2.Source;
            if (!IsPsnProfilesHostUri(currentUrl))
                return;

            psnProfilesFinishing = true;
            try
            {
                if (navigationCompleted)
                {
                    await Task.Delay(500);
                    if (!psnProfilesVerificationActive || psnProfilesVerificationPaused)
                        return;
                    elapsed = DateTime.UtcNow - detectedAt;
                }

                if (!psnProfilesPostClearanceNavigateIssued && elapsed >= TimeSpan.FromSeconds(3))
                {
                    psnProfilesPostClearanceNavigateIssued = true;
                    statusLabel.Text = "PSNProfiles verification passed. Opening the trophy page...";
                    psnProfilesVerificationWebView.CoreWebView2.Navigate(psnProfilesVerificationTargetUrl);
                    return;
                }

                string htmlJson = await psnProfilesVerificationWebView.CoreWebView2.ExecuteScriptAsync(
                    "document.documentElement ? document.documentElement.outerHTML : ''"
                );
                string html = JsonSerializer.Deserialize<string>(htmlJson) ?? string.Empty;

                if (LooksLikeCloudflareChallenge(html))
                {
                    elapsed = DateTime.UtcNow - detectedAt;
                    if (elapsed < TimeSpan.FromSeconds(15))
                    {
                        statusLabel.Text = "PSNProfiles verification passed. Cloudflare is finishing the redirect...";
                        return;
                    }

                    PausePsnProfilesVerificationLoop(
                        "Verification succeeded, but PSNProfiles did not leave the Cloudflare page. Click Retry to continue."
                    );
                    return;
                }

                if (PsnProfilesTrophyLinkRegex.Matches(html).Count == 0)
                {
                    elapsed = DateTime.UtcNow - detectedAt;
                    if (elapsed < TimeSpan.FromSeconds(15))
                    {
                        statusLabel.Text = "PSNProfiles verification passed. Waiting for the trophy page contents...";
                        return;
                    }
                }

                List<Pair> trophies = ParsePsnProfilesPage(html, 0);
                ValidatePsnProfilesMapping(trophies);
                StopPsnProfilesVerification(false);
                CompleteCopy(trophies);
            }
            catch (Exception ex)
            {
                if (psnProfilesVerificationActive)
                    HandlePsnProfilesVerificationFailure(ex);
            }
            finally
            {
                psnProfilesFinishing = false;
            }
        }

        private void PausePsnProfilesVerificationLoop(string message = null)
        {
            if (!psnProfilesVerificationActive || psnProfilesVerificationPaused)
                return;

            psnProfilesVerificationPaused = true;
            if (psnProfilesVerificationTimer != null)
                psnProfilesVerificationTimer.Stop();

            try
            {
                if (psnProfilesVerificationWebView != null && psnProfilesVerificationWebView.CoreWebView2 != null)
                    psnProfilesVerificationWebView.CoreWebView2.Stop();
            }
            catch
            {
            }

            statusLabel.Text = message ??
                "PSNProfiles rejected the embedded browser repeatedly. Automatic reloads were stopped.";
            EnsurePsnProfilesRetryButton();
            psnProfilesRetryButton.Visible = true;
            psnProfilesRetryButton.Enabled = true;
            psnProfilesRetryButton.BringToFront();
            button2.BringToFront();
        }

        private void psnProfilesRetryButton_Click(object sender, EventArgs e)
        {
            if (!psnProfilesVerificationActive || string.IsNullOrEmpty(psnProfilesVerificationTargetUrl) ||
                psnProfilesVerificationWebView == null || psnProfilesVerificationWebView.CoreWebView2 == null)
                return;

            psnProfilesVerificationPaused = false;
            psnProfilesClearanceDetected = false;
            psnProfilesClearanceDetectedAt = null;
            psnProfilesPostClearanceNavigateIssued = false;
            psnProfilesFinishing = false;
            psnProfilesTargetNavigations = 0;
            psnProfilesNavigationWindowStart = DateTime.UtcNow;
            psnProfilesRetryButton.Visible = false;
            statusLabel.Text = "Retrying PSNProfiles Cloudflare verification...";
            psnProfilesVerificationTimer.Start();
            psnProfilesVerificationWebView.CoreWebView2.Navigate(psnProfilesVerificationTargetUrl);
        }

        private static bool IsAllowedPsnProfilesVerificationUri(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "about:blank")
                return true;

            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri))
                return false;

            string host = uri.Host ?? string.Empty;
            return host.Equals("psnprofiles.com", StringComparison.OrdinalIgnoreCase) ||
                   host.Equals("www.psnprofiles.com", StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith(".psnprofiles.com", StringComparison.OrdinalIgnoreCase) ||
                   host.Equals("challenges.cloudflare.com", StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith(".cloudflare.com", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPsnProfilesHostUri(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri))
                return false;

            string host = uri.Host ?? string.Empty;
            return host.Equals("psnprofiles.com", StringComparison.OrdinalIgnoreCase) ||
                   host.Equals("www.psnprofiles.com", StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith(".psnprofiles.com", StringComparison.OrdinalIgnoreCase);
        }

        private void StopPsnProfilesVerification(bool disposeBrowser)
        {
            psnProfilesVerificationActive = false;
            psnProfilesVerificationPaused = false;
            psnProfilesCheckingCookies = false;
            psnProfilesClearanceDetected = false;
            psnProfilesClearanceDetectedAt = null;
            psnProfilesPostClearanceNavigateIssued = false;
            psnProfilesFinishing = false;
            psnProfilesTargetNavigations = 0;
            psnProfilesVerificationTargetUrl = null;

            if (psnProfilesVerificationTimer != null)
                psnProfilesVerificationTimer.Stop();

            if (psnProfilesRetryButton != null)
                psnProfilesRetryButton.Visible = false;

            if (psnProfilesVerificationWebView != null)
            {
                psnProfilesVerificationWebView.Visible = false;
                if (disposeBrowser)
                {
                    Controls.Remove(psnProfilesVerificationWebView);
                    psnProfilesVerificationWebView.Dispose();
                    psnProfilesVerificationWebView = null;
                }
            }
        }

        private void HandlePsnProfilesVerificationFailure(Exception ex)
        {
            StopPsnProfilesVerification(false);
            ExitVerificationLayout();
            RestoreReadyControls();
            statusLabel.Text = GetUsefulMessage(ex);
            MessageBox.Show(this, statusLabel.Text, "Copy From", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
