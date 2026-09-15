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
            @"href\s*=\s*[\"\"']/trophy/\d+-[^/\"\"']+/(?<id>\d+)-[^\"\"']+[\"\"']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        private static readonly Regex PsnProfilesEarnedRegex = new Regex(
            @"<picture\b(?=[^>]*\bclass\s*=\s*[\"\"'][^\"\"']*\btrophy\b[^\"\"']*\bearned\b[^\"\"']*[\"\"'])",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled
        );

        private static readonly Regex PsnProfilesDateRegex = new Regex(
            @"<span\b(?=[^>]*\bclass\s*=\s*[\"\"'][^\"\"']*\btypo-top-date\b[^\"\"']*[\"\"'])[^>]*>\s*<nobr>(?<value>.*?)</nobr>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled
        );

        private static readonly Regex PsnProfilesTimeRegex = new Regex(
            @"<span\b(?=[^>]*\bclass\s*=\s*[\"\"'][^\"\"']*\btypo-bottom-date\b[^\"\"']*[\"\"'])[^>]*>\s*<nobr>(?<value>.*?)</nobr>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled
        );

        private WebView2 psnProfilesVerificationWebView;
        private Timer psnProfilesVerificationTimer;
        private bool sourceAwareHookInstalled;
        private bool psnProfilesVerificationActive;
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
                bool earned = Regex.IsMatch(attrs, @"\bclass\s*=\s*[\"\"'][^\"\"']*\bcompleted\b", RegexOptions.IgnoreCase) ||
                              PsnProfilesEarnedRegex.IsMatch(body);

                long timestamp = 0;
                if (earned)
                {
                    Match date = PsnProfilesDateRegex.Match(body);
                    Match time = PsnProfilesTimeRegex.Match(body);
                    if (!date.Success || !time.Success)
                    {
                        throw new InvalidOperationException(
                            "PSNProfiles trophy " + siteTrophyId + " is marked earned but has no readable earned date. No trophies were modified."
                        );
                    }

                    timestamp = ParsePsnProfilesTimestamp(date.Groups["value"].Value, time.Groups["value"].Value, siteTrophyId);
                }

                trophies.Add(new Pair(localTrophyId, timestamp));
            }

            if (trophies.Count == 0)
            {
                string statusSuffix = httpStatus > 0 ? " (HTTP " + httpStatus + ")" : string.Empty;
                throw new InvalidOperationException(
                    "PSNProfiles returned a page, but no trophy rows could be parsed" + statusSuffix + ". The site format may have changed."
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
                    "PSNProfiles trophy " + siteTrophyId + " has an unsupported earned-date format: " + value + ". No trophies were modified."
                );
            }

            // The app-owned PSNProfiles session is anonymous; PSNProfiles renders anonymous trophy times in GMT.
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
                    "PSNProfiles returned " + trophies.Count + " trophy rows, but the local trophy set contains " +
                    ExpectedTrophyCount + ". No trophies were modified."
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
            psnProfilesCheckingCookies = false;
            psnProfilesClearanceDetected = false;
            psnProfilesPostClearanceNavigateIssued = false;
            psnProfilesFinishing = false;
            psnProfilesClearanceDetectedAt = null;
            psnProfilesTargetNavigations = 0;
            psnProfilesNavigationWindowStart = DateTime.UtcNow;

            EnterVerificationLayout();
            statusLabel.Text = "Complete the PSNProfiles Cloudflare checkbox below. It will close automatically.";

            try
            {
                await EnsurePsnProfilesVerificationWebViewAsync();
                if (!Visible || !psnProfilesVerificationActive)
                    return;

                psnProfilesVerificationWebView.Visible = true;
                psnProfilesVerificationWebView.ZoomFactor = 0.90;
                psnProfilesVerificationWebView.BringToFront();
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

        private async Task EnsurePsnProfilesVerificationWebViewAsync()
        {
            if (psnProfilesVerificationWebView != null && psnProfilesVerificationWebView.CoreWebView2 != null)
                return;

            if (psnProfilesVerificationWebView == null)
            {
                psnProfilesVerificationWebView = new WebView2
                {
                    Location = new System.Drawing.Point(13, 45),
                    Size = new System.Drawing.Size(404, 198),
                    Visible = false,
                    TabStop = true
                };
                psnProfilesVerificationWebView.NavigationCompleted += PsnProfilesVerificationWebView_NavigationCompleted;
                Controls.Add(psnProfilesVerificationWebView);
            }

            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PS3TrophyIsGood",
                "WebView2",
                "PSNProfiles"
            );
            Directory.CreateDirectory(userDataFolder);

            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await psnProfilesVerificationWebView.EnsureCoreWebView2Async(environment);

            CoreWebView2Settings settings = psnProfilesVerificationWebView.CoreWebView2.Settings;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;

            psnProfilesVerificationWebView.CoreWebView2.NavigationStarting += PsnProfilesVerificationWebView_NavigationStarting;
            psnProfilesVerificationWebView.CoreWebView2.NewWindowRequested += PsnProfilesVerificationWebView_NewWindowRequested;
            psnProfilesVerificationWebView.CoreWebView2.ProcessFailed += PsnProfilesVerificationWebView_ProcessFailed;

            if (psnProfilesVerificationTimer == null)
            {
                psnProfilesVerificationTimer = new Timer { Interval = 1000 };
                psnProfilesVerificationTimer.Tick += PsnProfilesVerificationTimer_Tick;
            }
        }

        private void PsnProfilesVerificationWebView_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (!IsAllowedPsnProfilesVerificationUri(e.Uri))
            {
                e.Cancel = true;
                return;
            }

            if (!psnProfilesVerificationActive || psnProfilesClearanceDetected || !IsPsnProfilesUri(e.Uri))
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
            HandlePsnProfilesVerificationFailure(
                new InvalidOperationException("PSNProfiles Cloudflare verification reloaded repeatedly. Try Copy trophies again.")
            );
        }

        private void PsnProfilesVerificationWebView_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
        }

        private void PsnProfilesVerificationWebView_ProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
        {
            if (!psnProfilesVerificationActive || IsDisposed || Disposing)
                return;

            BeginInvoke(new Action(delegate
            {
                HandlePsnProfilesVerificationFailure(new InvalidOperationException("The embedded PSNProfiles verification browser stopped unexpectedly."));
            }));
        }

        private async void PsnProfilesVerificationWebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!psnProfilesVerificationActive || !e.IsSuccess)
                return;

            if (psnProfilesClearanceDetected)
                await TryFinishPsnProfilesVerificationAsync();
            else
                await CheckPsnProfilesClearanceAsync();
        }

        private async void PsnProfilesVerificationTimer_Tick(object sender, EventArgs e)
        {
            if (!psnProfilesVerificationActive)
                return;

            if (!psnProfilesClearanceDetected)
            {
                await CheckPsnProfilesClearanceAsync();
                return;
            }

            DateTime detectedAt = psnProfilesClearanceDetectedAt ?? DateTime.UtcNow;
            TimeSpan elapsed = DateTime.UtcNow - detectedAt;
            if (!psnProfilesPostClearanceNavigateIssued && elapsed >= TimeSpan.FromSeconds(3))
            {
                psnProfilesPostClearanceNavigateIssued = true;
                statusLabel.Text = "Verification passed. Opening the PSNProfiles trophy page...";
                psnProfilesVerificationWebView.CoreWebView2.Navigate(psnProfilesVerificationTargetUrl);
                return;
            }

            if (elapsed >= TimeSpan.FromSeconds(15))
            {
                HandlePsnProfilesVerificationFailure(
                    new InvalidOperationException("PSNProfiles verification succeeded, but the trophy page did not finish loading. Try Copy trophies again.")
                );
            }
        }

        private async Task CheckPsnProfilesClearanceAsync()
        {
            if (!psnProfilesVerificationActive || psnProfilesCheckingCookies || psnProfilesClearanceDetected ||
                psnProfilesVerificationWebView == null || psnProfilesVerificationWebView.CoreWebView2 == null)
                return;

            psnProfilesCheckingCookies = true;
            try
            {
                IReadOnlyList<CoreWebView2Cookie> cookies = await psnProfilesVerificationWebView.CoreWebView2.CookieManager
                    .GetCookiesAsync("https://psnprofiles.com/");

                if (!psnProfilesVerificationActive)
                    return;

                if (!cookies.Any(cookie => string.Equals(cookie.Name, "cf_clearance", StringComparison.OrdinalIgnoreCase)))
                {
                    statusLabel.Text = "Complete the PSNProfiles Cloudflare checkbox below. Waiting for verification...";
                    return;
                }

                psnProfilesClearanceDetected = true;
                psnProfilesClearanceDetectedAt = DateTime.UtcNow;
                psnProfilesTargetNavigations = 0;
                statusLabel.Text = "Verification passed. Waiting for Cloudflare to finish...";
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

        private async Task TryFinishPsnProfilesVerificationAsync()
        {
            if (!psnProfilesVerificationActive || psnProfilesFinishing || !psnProfilesClearanceDetected ||
                psnProfilesVerificationWebView == null || psnProfilesVerificationWebView.CoreWebView2 == null)
                return;

            DateTime detectedAt = psnProfilesClearanceDetectedAt ?? DateTime.UtcNow;
            if (DateTime.UtcNow - detectedAt < TimeSpan.FromMilliseconds(1200))
                return;

            if (!IsPsnProfilesUri(psnProfilesVerificationWebView.CoreWebView2.Source))
                return;

            psnProfilesFinishing = true;
            try
            {
                await Task.Delay(500);
                if (!psnProfilesVerificationActive)
                    return;

                string htmlJson = await psnProfilesVerificationWebView.CoreWebView2.ExecuteScriptAsync(
                    "document.documentElement ? document.documentElement.outerHTML : ''"
                );
                string html = JsonSerializer.Deserialize<string>(htmlJson) ?? string.Empty;

                if (LooksLikeCloudflareChallenge(html))
                {
                    statusLabel.Text = "Verification passed. Cloudflare is finishing the redirect...";
                    return;
                }

                List<Pair> trophies = ParsePsnProfilesPage(html, 200);
                ValidatePsnProfilesMapping(trophies);
                StopPsnProfilesVerification(false);
                ExitVerificationLayout();
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

        private static bool IsAllowedPsnProfilesVerificationUri(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "about:blank")
                return true;

            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri))
                return false;

            string host = uri.Host ?? string.Empty;
            return IsPsnProfilesHost(host) ||
                   host.Equals("challenges.cloudflare.com", StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith(".cloudflare.com", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPsnProfilesUri(string value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out Uri uri) && IsPsnProfilesHost(uri.Host ?? string.Empty);
        }

        private static bool IsPsnProfilesHost(string host)
        {
            return host.Equals("psnprofiles.com", StringComparison.OrdinalIgnoreCase) ||
                   host.Equals("www.psnprofiles.com", StringComparison.OrdinalIgnoreCase);
        }

        private void StopPsnProfilesVerification(bool disposeBrowser)
        {
            psnProfilesVerificationActive = false;
            psnProfilesCheckingCookies = false;
            psnProfilesClearanceDetected = false;
            psnProfilesPostClearanceNavigateIssued = false;
            psnProfilesFinishing = false;
            psnProfilesClearanceDetectedAt = null;
            psnProfilesTargetNavigations = 0;
            psnProfilesVerificationTargetUrl = null;

            if (psnProfilesVerificationTimer != null)
                psnProfilesVerificationTimer.Stop();

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
