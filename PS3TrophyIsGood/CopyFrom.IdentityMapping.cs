using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PS3TrophyIsGood
{
    public partial class CopyFrom
    {
        private enum IdentitySource
        {
            PsnTrophyLeaders,
            PsnProfiles
        }

        public sealed class LocalTrophyIdentity
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Detail { get; set; }
            public string Type { get; set; }
            public int GroupId { get; set; }
        }

        private sealed class RemoteTrophyIdentity
        {
            public int RemoteId { get; set; }
            public string Name { get; set; }
            public string Detail { get; set; }
            public string Type { get; set; }
            public long Date { get; set; }
        }

        private static readonly Regex PsntlTitleRegex = new Regex(
            @"<a\b(?=[^>]*\bid\s*=\s*[\"\"']trophytitle\d+[\"\"'])[^>]*>(?<value>.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled
        );

        private static readonly Regex PsntlDetailRegex = new Regex(
            @"<div\b(?=[^>]*\bid\s*=\s*[\"\"']trophydescription\d+[\"\"'])[^>]*>(?<value>.*?)</div>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled
        );

        private static readonly Regex PsntlTypeRegex = new Regex(
            @"<td\b(?=[^>]*\bclass\s*=\s*[\"\"'][^\"\"']*\btrophytype\b[^\"\"']*[\"\"'])[^>]*>\s*(?<value>\d+)\s*</td>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled
        );

        private static readonly Regex PsntlRemoteIdRegex = new Regex(
            @"\bid\s*=\s*[\"\"']trophytitle(?<value>\d+)[\"\"']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        private static readonly Regex ProfilesIdentityTitleRegex = new Regex(
            @"<a\b(?=[^>]*\bclass\s*=\s*[\"\"'][^\"\"']*\btitle\b[^\"\"']*[\"\"'])[^>]*>(?<value>.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled
        );

        private static readonly Regex ProfilesIdentityDetailRegex = new Regex(
            @"<a\b(?=[^>]*\bclass\s*=\s*[\"\"'][^\"\"']*\btitle\b[^\"\"']*[\"\"'])[^>]*>.*?</a>\s*<br\s*/?>(?<value>.*?)</td>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled
        );

        private static readonly Regex ProfilesIdentityTypeRegex = new Regex(
            @"<img\b[^>]*\btitle\s*=\s*[\"\"'](?<value>Platinum|Gold|Silver|Bronze)[\"\"'][^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        private const int IdentityMaxChallengeReloads = 5;
        private readonly List<LocalTrophyIdentity> localTrophyIdentities = new List<LocalTrophyIdentity>();
        private bool identityHookInstalled;
        private bool identityVisibleHookInstalled;
        private WebView2 identityVerificationWebView;
        private Timer identityVerificationTimer;
        private Button identityRetryButton;
        private bool identityVerificationActive;
        private bool identityVerificationPaused;
        private bool identityCheckingCookies;
        private bool identityClearanceDetected;
        private bool identityPostClearanceNavigateIssued;
        private bool identityFinishing;
        private int identityTargetNavigations;
        private DateTime identityNavigationWindowStart;
        private DateTime? identityClearanceDetectedAt;
        private string identityVerificationTargetUrl;
        private IdentitySource identityVerificationSource;

        public int LastRemoteTrophyCount { get; private set; }
        public int LastMatchedTrophyCount { get; private set; }
        public string LastSourceName { get; private set; }

        public void SetLocalTrophies(IEnumerable<LocalTrophyIdentity> trophies)
        {
            localTrophyIdentities.Clear();
            if (trophies != null)
                localTrophyIdentities.AddRange(trophies);

            ExpectedTrophyCount = localTrophyIdentities.Count;
        }

        public IEnumerable<Pair> copyFromPairs()
        {
            return loadedTrophies
                .OrderBy(t => t.Id)
                .Select(t => new Pair(t.Id, t.Date))
                .ToList();
        }

        public IEnumerable<Pair> smartCopyPairs()
        {
            List<Pair> trophies = loadedTrophies.Select(t => new Pair(t.Id, t.Date)).ToList();
            if (trophies.Count == 0)
                return trophies;

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

            return trophies.OrderBy(t => t.Id).ToList();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            if (!identityHookInstalled)
            {
                identityHookInstalled = true;
                accept.Click -= accept_Click;
                accept.Click -= accept_SourceAware_Click;
                accept.Click += accept_Identity_Click;
            }

            if (!identityVisibleHookInstalled)
            {
                identityVisibleHookInstalled = true;
                VisibleChanged += IdentityCopyFrom_VisibleChanged;
            }
        }

        private void IdentityCopyFrom_VisibleChanged(object sender, EventArgs e)
        {
            if (!Visible)
                StopIdentityVerification(true);
        }

        private async void accept_Identity_Click(object sender, EventArgs e)
        {
            string targetUrl = (textBox1.Text ?? string.Empty).Trim();
            IdentitySource source;
            if (IsPsnProfilesTrophyUrl(targetUrl))
            {
                source = IdentitySource.PsnProfiles;
            }
            else if (Regex.IsMatch(
                targetUrl,
                @"^https://psntrophyleaders\.com/user/view/[^/\s]+/[^\s]+$",
                RegexOptions.IgnoreCase))
            {
                source = IdentitySource.PsnTrophyLeaders;
            }
            else
            {
                MessageBox.Show(Properties.strings.CantFindGame);
                return;
            }

            if (!helperReady)
            {
                MessageBox.Show(this, "Press Start and wait until FlareSolverr is ready.", "Copy From");
                return;
            }

            if (localTrophyIdentities.Count == 0)
            {
                MessageBox.Show(this, "No local trophy identities are available for safe matching.", "Copy From");
                return;
            }

            if (minMinutes.Value > maxMinutes.Value)
            {
                MessageBox.Show(Properties.strings.MinCantBeGreaterThanMax);
                return;
            }

            LastSourceName = GetIdentitySourceName(source);
            SetBusy("Loading trophy page from " + LastSourceName + "...");

            try
            {
                FlareSolverrManager.PageResult page = await helper.RequestPageAsync(targetUrl);
                if (!Visible)
                    return;

                if (LooksLikeCloudflareChallenge(page.Html))
                {
                    await ShowIdentityVerificationAsync(targetUrl, source);
                    return;
                }

                CompleteIdentityPage(page.Html, page.HttpStatus, source);
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

        private void CompleteIdentityPage(string html, int httpStatus, IdentitySource source)
        {
            List<RemoteTrophyIdentity> remote = source == IdentitySource.PsnProfiles
                ? ParseProfilesIdentities(html, httpStatus)
                : ParsePsntlIdentities(html, httpStatus);

            List<Pair> mapped = MapRemoteToLocal(remote, source);
            LastRemoteTrophyCount = remote.Count;
            LastMatchedTrophyCount = mapped.Count;
            LastSourceName = GetIdentitySourceName(source);

            StopIdentityVerification(false);
            loadedTrophies.Clear();
            loadedTrophies.AddRange(mapped);
            helperProgress.Style = ProgressBarStyle.Continuous;
            helperProgress.Value = 100;

            int unmatchedLocal = Math.Max(0, localTrophyIdentities.Count - mapped.Count);
            int ignoredRemote = Math.Max(0, remote.Count - mapped.Count);
            statusLabel.Text = "Matched " + mapped.Count + " trophies" +
                (unmatchedLocal > 0 ? "; " + unmatchedLocal + " local left unchanged" : string.Empty) +
                (ignoredRemote > 0 ? "; " + ignoredRemote + " source-only ignored" : string.Empty) + ".";
            DialogResult = DialogResult.OK;
        }

        private List<Pair> MapRemoteToLocal(List<RemoteTrophyIdentity> remote, IdentitySource source)
        {
            Dictionary<int, Pair> mapped = new Dictionary<int, Pair>();
            HashSet<int> usedRemoteIds = new HashSet<int>();

            foreach (RemoteTrophyIdentity item in remote)
            {
                string remoteName = NormalizeIdentityText(item.Name);
                if (string.IsNullOrEmpty(remoteName))
                    continue;

                List<LocalTrophyIdentity> candidates = localTrophyIdentities
                    .Where(local => NormalizeIdentityText(local.Name) == remoteName)
                    .ToList();

                string remoteType = NormalizeTrophyType(item.Type);
                if (!string.IsNullOrEmpty(remoteType))
                {
                    List<LocalTrophyIdentity> sameType = candidates
                        .Where(local => NormalizeTrophyType(local.Type) == remoteType)
                        .ToList();
                    if (sameType.Count > 0)
                        candidates = sameType;
                }

                if (candidates.Count > 1)
                {
                    string remoteDetail = NormalizeIdentityDetail(item.Detail);
                    List<LocalTrophyIdentity> sameDetail = candidates
                        .Where(local => NormalizeIdentityDetail(local.Detail) == remoteDetail)
                        .ToList();
                    if (sameDetail.Count > 0)
                        candidates = sameDetail;
                }

                if (candidates.Count == 0)
                    continue;

                if (candidates.Count != 1)
                {
                    throw new InvalidOperationException(
                        GetIdentitySourceName(source) + " trophy '" + item.Name +
                        "' matches more than one local trophy. No trophies were modified."
                    );
                }

                LocalTrophyIdentity localTrophy = candidates[0];
                if (mapped.ContainsKey(localTrophy.Id))
                {
                    throw new InvalidOperationException(
                        GetIdentitySourceName(source) + " mapped more than one source trophy to local trophy '" +
                        localTrophy.Name + "'. No trophies were modified."
                    );
                }

                mapped.Add(localTrophy.Id, new Pair(localTrophy.Id, item.Date));
                usedRemoteIds.Add(item.RemoteId);
            }

            int expectedIntersection = Math.Min(remote.Count, localTrophyIdentities.Count);
            if (mapped.Count != expectedIntersection)
            {
                throw new InvalidOperationException(
                    GetIdentitySourceName(source) + " has " + remote.Count + " trophy rows and the local set has " +
                    localTrophyIdentities.Count + ", but only " + mapped.Count +
                    " trophies could be matched 1:1 by name/type. No trophies were modified."
                );
            }

            return mapped.Values.OrderBy(pair => pair.Id).ToList();
        }

        private static List<RemoteTrophyIdentity> ParsePsntlIdentities(string html, int httpStatus)
        {
            List<RemoteTrophyIdentity> trophies = new List<RemoteTrophyIdentity>();
            MatchCollection rows = TrophyRowRegex.Matches(html ?? string.Empty);

            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                string body = rows[rowIndex].Groups["body"].Value;
                Match title = PsntlTitleRegex.Match(body);
                Match detail = PsntlDetailRegex.Match(body);
                Match type = PsntlTypeRegex.Match(body);
                Match remoteId = PsntlRemoteIdRegex.Match(body);
                Match dateCell = DateCellRegex.Match(body);

                if (!title.Success || !dateCell.Success)
                    throw new InvalidOperationException("PSN Trophy Leaders returned a trophy row without identity/date data. No trophies were modified.");

                Match timestamp = SortValueRegex.Match(dateCell.Groups["body"].Value);
                if (!timestamp.Success)
                    timestamp = SortAttributeRegex.Match(dateCell.Value);

                if (!timestamp.Success || !long.TryParse(timestamp.Groups["value"].Value, out long date))
                    throw new InvalidOperationException("PSN Trophy Leaders returned an invalid trophy timestamp. No trophies were modified.");

                int parsedRemoteId = rowIndex + 1;
                if (remoteId.Success)
                    int.TryParse(remoteId.Groups["value"].Value, out parsedRemoteId);

                trophies.Add(new RemoteTrophyIdentity
                {
                    RemoteId = parsedRemoteId,
                    Name = CleanIdentityHtml(title.Groups["value"].Value),
                    Detail = detail.Success ? CleanIdentityHtml(detail.Groups["value"].Value) : string.Empty,
                    Type = type.Success ? PsntlNumericTypeToCode(type.Groups["value"].Value) : string.Empty,
                    Date = date
                });
            }

            if (trophies.Count == 0)
            {
                string statusSuffix = httpStatus > 0 ? " (HTTP " + httpStatus + ")" : string.Empty;
                throw new InvalidOperationException("PSN Trophy Leaders returned no parseable trophy rows" + statusSuffix + ".");
            }

            return trophies;
        }

        private static List<RemoteTrophyIdentity> ParseProfilesIdentities(string html, int httpStatus)
        {
            List<RemoteTrophyIdentity> trophies = new List<RemoteTrophyIdentity>();
            MatchCollection rows = ProfilesIdentityRowRegex.Matches(html ?? string.Empty);

            foreach (Match row in rows)
            {
                string body = row.Groups["body"].Value;
                Match trophyLink = ProfilesIdentityLinkRegex.Match(body);
                if (!trophyLink.Success)
                    continue;

                if (!int.TryParse(trophyLink.Groups["id"].Value, out int remoteId) || remoteId <= 0)
                    throw new InvalidOperationException("PSNProfiles returned an invalid trophy number. No trophies were modified.");

                Match title = ProfilesIdentityTitleRegex.Match(body);
                Match detail = ProfilesIdentityDetailRegex.Match(body);
                Match type = ProfilesIdentityTypeRegex.Match(body);
                if (!title.Success)
                    throw new InvalidOperationException("PSNProfiles returned a trophy without a readable title. No trophies were modified.");

                string attrs = row.Groups["attrs"].Value;
                bool earned = Regex.IsMatch(attrs, @"\bclass\s*=\s*[\"\"'][^\"\"']*\bcompleted\b", RegexOptions.IgnoreCase) ||
                              ProfilesIdentityEarnedRegex.IsMatch(body);

                long timestamp = 0;
                if (earned)
                {
                    Match date = ProfilesIdentityDateRegex.Match(body);
                    Match time = ProfilesIdentityTimeRegex.Match(body);
                    if (!date.Success || !time.Success)
                    {
                        throw new InvalidOperationException(
                            "PSNProfiles trophy " + remoteId + " is marked earned but has no readable earned date. No trophies were modified."
                        );
                    }

                    timestamp = ParsePsnProfilesTimestamp(
                        date.Groups["value"].Value,
                        time.Groups["value"].Value,
                        remoteId
                    );
                }

                trophies.Add(new RemoteTrophyIdentity
                {
                    RemoteId = remoteId,
                    Name = CleanIdentityHtml(title.Groups["value"].Value),
                    Detail = detail.Success ? CleanIdentityHtml(detail.Groups["value"].Value) : string.Empty,
                    Type = type.Success ? NormalizeTrophyType(type.Groups["value"].Value) : string.Empty,
                    Date = timestamp
                });
            }

            if (trophies.Count == 0)
            {
                string statusSuffix = httpStatus > 0 ? " (HTTP " + httpStatus + ")" : string.Empty;
                throw new InvalidOperationException("PSNProfiles returned no parseable trophy rows" + statusSuffix + ".");
            }

            return trophies;
        }

        private static readonly Regex ProfilesIdentityRowRegex = new Regex(
            @"<tr\b(?<attrs>[^>]*)>(?<body>.*?)</tr>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled
        );

        private static readonly Regex ProfilesIdentityLinkRegex = new Regex(
            @"href\s*=\s*[\"\"']/trophy/\d+-[^/\"\"']+/(?<id>\d+)-[^\"\"']+[\"\"']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        private static readonly Regex ProfilesIdentityEarnedRegex = new Regex(
            @"<picture\b(?=[^>]*\bclass\s*=\s*[\"\"'][^\"\"']*\btrophy\b[^\"\"']*\bearned\b[^\"\"']*[\"\"'])",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled
        );

        private static readonly Regex ProfilesIdentityDateRegex = new Regex(
            @"<span\b(?=[^>]*\bclass\s*=\s*[\"\"'][^\"\"']*\btypo-top-date\b[^\"\"']*[\"\"'])[^>]*>\s*<nobr>(?<value>.*?)</nobr>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled
        );

        private static readonly Regex ProfilesIdentityTimeRegex = new Regex(
            @"<span\b(?=[^>]*\bclass\s*=\s*[\"\"'][^\"\"']*\btypo-bottom-date\b[^\"\"']*[\"\"'])[^>]*>\s*<nobr>(?<value>.*?)</nobr>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled
        );

        private static string CleanIdentityHtml(string value)
        {
            string withoutTags = Regex.Replace(value ?? string.Empty, @"<[^>]+>", string.Empty);
            return WebUtility.HtmlDecode(withoutTags ?? string.Empty).Trim();
        }

        private static string NormalizeIdentityText(string value)
        {
            string decoded = CleanIdentityHtml(value).Normalize(NormalizationForm.FormKC);
            decoded = decoded.Replace('’', '\'').Replace('‘', '\'').Replace('“', '"').Replace('”', '"');
            return Regex.Replace(decoded, @"\s+", " ").Trim().ToLowerInvariant();
        }

        private static string NormalizeIdentityDetail(string value)
        {
            string normalized = NormalizeIdentityText(value);
            normalized = Regex.Replace(normalized, @"\s*\([^()]*\)\s*$", string.Empty).Trim();
            return normalized.TrimEnd('.', ' ', '\t', '\r', '\n');
        }

        private static string NormalizeTrophyType(string value)
        {
            string normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
            switch (normalized)
            {
                case "PLATINUM": return "P";
                case "GOLD": return "G";
                case "SILVER": return "S";
                case "BRONZE": return "B";
                case "P":
                case "G":
                case "S":
                case "B":
                    return normalized;
                default:
                    return string.Empty;
            }
        }

        private static string PsntlNumericTypeToCode(string value)
        {
            switch ((value ?? string.Empty).Trim())
            {
                case "0": return "B";
                case "1": return "S";
                case "2": return "G";
                case "3": return "P";
                default: return string.Empty;
            }
        }

        private static string GetIdentitySourceName(IdentitySource source)
        {
            return source == IdentitySource.PsnProfiles ? "PSNProfiles" : "PSN Trophy Leaders";
        }

        private async Task ShowIdentityVerificationAsync(string targetUrl, IdentitySource source)
        {
            identityVerificationTargetUrl = targetUrl;
            identityVerificationSource = source;
            identityVerificationActive = true;
            identityVerificationPaused = false;
            identityCheckingCookies = false;
            identityClearanceDetected = false;
            identityPostClearanceNavigateIssued = false;
            identityFinishing = false;
            identityTargetNavigations = 0;
            identityNavigationWindowStart = DateTime.UtcNow;
            identityClearanceDetectedAt = null;

            EnterVerificationLayout();
            EnsureIdentityRetryButton();
            statusLabel.Text = "Complete the " + GetIdentitySourceName(source) + " Cloudflare checkbox below.";

            try
            {
                await EnsureIdentityVerificationWebViewAsync();
                if (!Visible || !identityVerificationActive)
                    return;

                identityVerificationWebView.Visible = true;
                identityVerificationWebView.ZoomFactor = 0.90;
                identityVerificationWebView.BringToFront();
                identityRetryButton.BringToFront();
                button2.BringToFront();
                identityVerificationTimer.Start();
                identityVerificationWebView.CoreWebView2.Navigate(targetUrl);
            }
            catch (Exception ex)
            {
                HandleIdentityVerificationFailure(new InvalidOperationException(
                    "Embedded verification could not start. Microsoft Edge WebView2 Runtime is required.", ex));
            }
        }

        private void EnsureIdentityRetryButton()
        {
            if (identityRetryButton != null)
            {
                identityRetryButton.Visible = false;
                return;
            }

            identityRetryButton = new Button
            {
                Location = new Point(235, 251),
                Size = new Size(99, 23),
                Text = "Retry",
                UseVisualStyleBackColor = true,
                Visible = false
            };
            identityRetryButton.Click += identityRetryButton_Click;
            Controls.Add(identityRetryButton);
        }

        private async Task EnsureIdentityVerificationWebViewAsync()
        {
            if (identityVerificationWebView != null && identityVerificationWebView.CoreWebView2 != null)
                return;

            if (identityVerificationTimer == null)
            {
                identityVerificationTimer = new Timer { Interval = 1000 };
                identityVerificationTimer.Tick += identityVerificationTimer_Tick;
            }

            if (identityVerificationWebView == null)
            {
                identityVerificationWebView = new WebView2
                {
                    Location = new Point(13, 45),
                    Size = new Size(404, 198),
                    Visible = false,
                    TabStop = true
                };
                identityVerificationWebView.NavigationCompleted += identityVerificationWebView_NavigationCompleted;
                Controls.Add(identityVerificationWebView);
            }

            string userDataFolder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PS3TrophyIsGood",
                "WebView2"
            );
            System.IO.Directory.CreateDirectory(userDataFolder);

            CoreWebView2Environment.GetAvailableBrowserVersionString();
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await identityVerificationWebView.EnsureCoreWebView2Async(environment);

            CoreWebView2Settings settings = identityVerificationWebView.CoreWebView2.Settings;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;

            identityVerificationWebView.CoreWebView2.NavigationStarting += identityVerificationWebView_NavigationStarting;
            identityVerificationWebView.CoreWebView2.NewWindowRequested += identityVerificationWebView_NewWindowRequested;
            identityVerificationWebView.CoreWebView2.ProcessFailed += identityVerificationWebView_ProcessFailed;
        }

        private void identityVerificationWebView_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (!IsAllowedIdentityVerificationUri(e.Uri, identityVerificationSource))
            {
                e.Cancel = true;
                return;
            }

            if (!identityVerificationActive || identityClearanceDetected || identityVerificationPaused ||
                !IsIdentitySourceHostUri(e.Uri, identityVerificationSource))
                return;

            DateTime now = DateTime.UtcNow;
            if (now - identityNavigationWindowStart > TimeSpan.FromSeconds(30))
            {
                identityNavigationWindowStart = now;
                identityTargetNavigations = 0;
            }

            identityTargetNavigations++;
            if (identityTargetNavigations <= IdentityMaxChallengeReloads)
                return;

            e.Cancel = true;
            PauseIdentityVerificationLoop();
        }

        private void identityVerificationWebView_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
        }

        private void identityVerificationWebView_ProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
        {
            if (!identityVerificationActive || IsDisposed || Disposing)
                return;

            BeginInvoke(new Action(delegate
            {
                HandleIdentityVerificationFailure(new InvalidOperationException("The embedded verification browser stopped unexpectedly."));
            }));
        }

        private async void identityVerificationWebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!identityVerificationActive || identityVerificationPaused || !e.IsSuccess)
                return;

            if (identityClearanceDetected)
                await ContinueAfterIdentityClearanceAsync(true);
            else
                await CheckForIdentityClearanceAsync();
        }

        private async void identityVerificationTimer_Tick(object sender, EventArgs e)
        {
            if (identityClearanceDetected)
                await ContinueAfterIdentityClearanceAsync(false);
            else
                await CheckForIdentityClearanceAsync();
        }

        private async Task CheckForIdentityClearanceAsync()
        {
            if (!identityVerificationActive || identityVerificationPaused || identityClearanceDetected ||
                identityCheckingCookies || identityVerificationWebView == null || identityVerificationWebView.CoreWebView2 == null)
                return;

            identityCheckingCookies = true;
            try
            {
                string cookieUrl = identityVerificationSource == IdentitySource.PsnProfiles
                    ? "https://psnprofiles.com/"
                    : "https://psntrophyleaders.com/";

                IReadOnlyList<CoreWebView2Cookie> cookies = await identityVerificationWebView.CoreWebView2.CookieManager
                    .GetCookiesAsync(cookieUrl);

                if (!identityVerificationActive || identityVerificationPaused)
                    return;

                bool hasClearance = cookies.Any(cookie =>
                    string.Equals(cookie.Name, "cf_clearance", StringComparison.OrdinalIgnoreCase));

                if (!hasClearance)
                {
                    statusLabel.Text = "Complete the Cloudflare checkbox below. Waiting for verification...";
                    return;
                }

                identityClearanceDetected = true;
                identityClearanceDetectedAt = DateTime.UtcNow;
                identityPostClearanceNavigateIssued = false;
                identityTargetNavigations = 0;
                statusLabel.Text = "Verification passed. Waiting for Cloudflare to finish...";
            }
            catch (Exception ex)
            {
                if (identityVerificationActive)
                    HandleIdentityVerificationFailure(ex);
            }
            finally
            {
                identityCheckingCookies = false;
            }
        }

        private async Task ContinueAfterIdentityClearanceAsync(bool navigationCompleted)
        {
            if (!identityVerificationActive || identityVerificationPaused || identityFinishing || !identityClearanceDetected ||
                identityVerificationWebView == null || identityVerificationWebView.CoreWebView2 == null)
                return;

            DateTime detectedAt = identityClearanceDetectedAt ?? DateTime.UtcNow;
            TimeSpan elapsed = DateTime.UtcNow - detectedAt;
            if (elapsed < TimeSpan.FromMilliseconds(1200))
                return;

            if (!IsIdentitySourceHostUri(identityVerificationWebView.CoreWebView2.Source, identityVerificationSource))
                return;

            identityFinishing = true;
            try
            {
                if (navigationCompleted)
                {
                    await Task.Delay(500);
                    if (!identityVerificationActive || identityVerificationPaused)
                        return;
                    elapsed = DateTime.UtcNow - detectedAt;
                }

                if (!identityPostClearanceNavigateIssued && elapsed >= TimeSpan.FromSeconds(3))
                {
                    identityPostClearanceNavigateIssued = true;
                    statusLabel.Text = "Verification passed. Opening the trophy page...";
                    identityVerificationWebView.CoreWebView2.Navigate(identityVerificationTargetUrl);
                    return;
                }

                string htmlJson = await identityVerificationWebView.CoreWebView2.ExecuteScriptAsync(
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

                    PauseIdentityVerificationLoop(
                        "Verification succeeded, but Cloudflare did not leave the challenge page. Click Retry to continue."
                    );
                    return;
                }

                CompleteIdentityPage(html, 200, identityVerificationSource);
            }
            catch (Exception ex)
            {
                if (identityVerificationActive)
                    HandleIdentityVerificationFailure(ex);
            }
            finally
            {
                identityFinishing = false;
            }
        }

        private void PauseIdentityVerificationLoop(string message = null)
        {
            if (!identityVerificationActive || identityVerificationPaused)
                return;

            identityVerificationPaused = true;
            identityVerificationTimer.Stop();
            try
            {
                if (identityVerificationWebView != null && identityVerificationWebView.CoreWebView2 != null)
                    identityVerificationWebView.CoreWebView2.Stop();
            }
            catch
            {
            }

            statusLabel.Text = message ?? "Cloudflare rejected the embedded browser repeatedly. Automatic reloads were stopped.";
            identityRetryButton.Visible = true;
            identityRetryButton.Enabled = true;
            identityRetryButton.BringToFront();
            button2.BringToFront();
        }

        private void identityRetryButton_Click(object sender, EventArgs e)
        {
            if (!identityVerificationActive || string.IsNullOrEmpty(identityVerificationTargetUrl) ||
                identityVerificationWebView == null || identityVerificationWebView.CoreWebView2 == null)
                return;

            identityVerificationPaused = false;
            identityClearanceDetected = false;
            identityClearanceDetectedAt = null;
            identityPostClearanceNavigateIssued = false;
            identityFinishing = false;
            identityTargetNavigations = 0;
            identityNavigationWindowStart = DateTime.UtcNow;
            identityRetryButton.Visible = false;
            statusLabel.Text = "Retrying Cloudflare verification...";
            identityVerificationTimer.Start();
            identityVerificationWebView.CoreWebView2.Navigate(identityVerificationTargetUrl);
        }

        private void StopIdentityVerification(bool disposeBrowser)
        {
            identityVerificationActive = false;
            identityVerificationPaused = false;
            identityCheckingCookies = false;
            identityClearanceDetected = false;
            identityClearanceDetectedAt = null;
            identityPostClearanceNavigateIssued = false;
            identityFinishing = false;
            identityTargetNavigations = 0;
            identityVerificationTargetUrl = null;
            if (identityVerificationTimer != null)
                identityVerificationTimer.Stop();

            if (identityRetryButton != null)
                identityRetryButton.Visible = false;

            if (identityVerificationWebView != null)
            {
                identityVerificationWebView.Visible = false;
                if (disposeBrowser)
                {
                    Controls.Remove(identityVerificationWebView);
                    identityVerificationWebView.Dispose();
                    identityVerificationWebView = null;
                }
            }
        }

        private void HandleIdentityVerificationFailure(Exception ex)
        {
            StopIdentityVerification(false);
            ExitVerificationLayout();
            RestoreReadyControls();
            statusLabel.Text = GetUsefulMessage(ex);
            MessageBox.Show(this, statusLabel.Text, "Copy From", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static bool IsAllowedIdentityVerificationUri(string value, IdentitySource source)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "about:blank")
                return true;

            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri))
                return false;

            string host = uri.Host ?? string.Empty;
            if (host.Equals("challenges.cloudflare.com", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".cloudflare.com", StringComparison.OrdinalIgnoreCase))
                return true;

            return IsIdentitySourceHost(host, source);
        }

        private static bool IsIdentitySourceHostUri(string value, IdentitySource source)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri))
                return false;
            return IsIdentitySourceHost(uri.Host ?? string.Empty, source);
        }

        private static bool IsIdentitySourceHost(string host, IdentitySource source)
        {
            if (source == IdentitySource.PsnProfiles)
            {
                return host.Equals("psnprofiles.com", StringComparison.OrdinalIgnoreCase) ||
                       host.EndsWith(".psnprofiles.com", StringComparison.OrdinalIgnoreCase);
            }

            return host.Equals("psntrophyleaders.com", StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith(".psntrophyleaders.com", StringComparison.OrdinalIgnoreCase);
        }
    }
}
