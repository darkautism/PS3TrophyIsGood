using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace PS3TrophyIsGood
{
    public partial class CopyFrom
    {
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
                    throw new InvalidOperationException(
                        "PSNProfiles returned a Cloudflare verification page. Retry later or use PSN Trophy Leaders for this copy."
                    );
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

            // Anonymous PSNProfiles trophy pages render timestamps in GMT/UTC.
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
    }
}
