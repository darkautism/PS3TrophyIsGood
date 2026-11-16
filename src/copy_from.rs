//! Copy From parsers for PSN Trophy Leaders and PSNProfiles.
//!
//! Trophy IDs on external sites are NOT interchangeable with local PS3 IDs.
//! Map exact trophy identities, grade, and description; use XMB ID as
//! supporting evidence only. Ambiguous and incomplete maps never modify data.
use anyhow::{Context, Result, bail, ensure};
use chrono::{NaiveDateTime, TimeZone, Utc};
use rand::Rng;
use scraper::{ElementRef, Html, Selector};
use std::collections::{HashMap, HashSet};
use unicode_normalization::UnicodeNormalization;
use crate::{Grade, Trophy};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CopySource {
    PsnTrophyLeaders,
    PsnProfiles,
}

impl CopySource {
    pub fn label(self) -> &'static str {
        match self {
            Self::PsnTrophyLeaders => "PSN Trophy Leaders",
            Self::PsnProfiles => "PSNProfiles",
        }
    }

    pub fn parse(value: &str) -> Result<Self> {
        match value.to_ascii_lowercase().as_str() {
            "psntl" | "psntrophyleaders" | "psntrophyleaders.com" => Ok(Self::PsnTrophyLeaders),
            "psnprofiles" | "psnprofiles.com" => Ok(Self::PsnProfiles),
            _ => bail!("unsupported Copy From source: {value}"),
        }
    }
}

/// Infer the website from a URL, or from the trophy identities inside a
/// previously saved HTML page. Never guess based on local trophy numbering.
pub fn detect_copy_source(reference: &str, saved_html: Option<&str>) -> Result<CopySource> {
    if let Ok(url) = reqwest::Url::parse(reference) {
        ensure!(url.scheme() == "https", "Copy From requires an HTTPS trophy URL");
        let host = url.host_str().unwrap_or("").trim_start_matches("www.");
        return match host.to_ascii_lowercase().as_str() {
            "psnprofiles.com" => Ok(CopySource::PsnProfiles),
            "psntrophyleaders.com" => Ok(CopySource::PsnTrophyLeaders),
            _ => bail!("unsupported Copy From website: {host}"),
        };
    }
    let html = saved_html.context("expected a supported trophy URL or a saved HTML file")?;
    let profile_rows = Html::parse_document(html)
        .select(&select("a[href^='/trophy/']")).count();
    let leaders_rows = Html::parse_document(html)
        .select(&select("tr")).filter(|row| {
            row.value().attr("class").is_some_and(|c|
                c.split_whitespace().any(|x| x.to_ascii_lowercase().starts_with("trophyvalue")))
        }).count();
    match (profile_rows > 0, leaders_rows > 0) {
        (true, false) => Ok(CopySource::PsnProfiles),
        (false, true) => Ok(CopySource::PsnTrophyLeaders),
        (false, false) => bail!("saved HTML does not contain PSNProfiles or PSN Trophy Leaders trophy rows"),
        (true, true) => bail!("saved HTML contains two different trophy formats; source is ambiguous"),
    }
}

/// The GUI's primary workflow: a single URL/HTML field, with no manual
/// source selector. Returns the detected source for an accurate audit log.
pub fn load_copy_page_auto(reference: &str) -> Result<(CopySource, String)> {
    if std::path::Path::new(reference).is_file() {
        let html = std::fs::read_to_string(reference)
            .with_context(|| format!("read saved trophy HTML: {reference}"))?;
        let source = detect_copy_source(reference, Some(&html))?;
        return Ok((source, html));
    }
    let source = detect_copy_source(reference, None)?;
    let html = load_copy_page(source, reference)?;
    Ok((source, html))
}

#[derive(Debug, Clone)]
pub struct RemoteTrophy {
    pub remote_id: u32,
    pub xmb_id: Option<u32>,
    pub name: String,
    pub detail: String,
    pub grade: Option<Grade>,
    pub earned_us: Option<i64>,
}

fn select(css: &str) -> Selector {
    Selector::parse(css).expect("valid fixed CSS selector")
}

fn text_of(element: ElementRef<'_>) -> String {
    element.text().collect::<Vec<_>>().join(" ").split_whitespace()
        .collect::<Vec<_>>().join(" ")
}

fn classes_contain(element: ElementRef<'_>, name: &str) -> bool {
    element.value().attr("class").is_some_and(|value|
        value.split_whitespace().any(|word| word.eq_ignore_ascii_case(name)))
}

fn grade_from(value: &str) -> Option<Grade> {
    match value.trim().to_ascii_lowercase().as_str() {
        "0" | "b" | "bronze" => Some(Grade::Bronze),
        "1" | "s" | "silver" => Some(Grade::Silver),
        "2" | "g" | "gold" => Some(Grade::Gold),
        "3" | "p" | "platinum" => Some(Grade::Platinum),
        _ => None,
    }
}

/// Returns original site timestamps in UTC microseconds, not local display
/// time. The site 0 (not obtained) maps to None.
fn psntl_timestamp(element: ElementRef<'_>) -> Result<Option<i64>> {
    let value = element.select(&select("span.sort")).next()
        .map(text_of).filter(|s| !s.is_empty())
        .or_else(|| element.value().attr("data-sort").map(ToOwned::to_owned))
        .or_else(|| element.value().attr("data-order").map(ToOwned::to_owned))
        .context("PSNTL row missing sortable earned time")?;
    let secs = value.parse::<i64>().context("invalid PSNTL earned timestamp")?;
    if secs == 0 { return Ok(None); }
    ensure!(secs > 0, "negative PSNTL timestamp");
    Ok(Some(secs.checked_mul(1_000_000).context("PSNTL timestamp overflow")?))
}

fn parse_psntl(html: &str) -> Result<Vec<RemoteTrophy>> {
    let doc = Html::parse_document(html);
    let mut result = Vec::new();
    for row in doc.select(&select("tr")) {
        let trophy_row = row.value().attr("class").is_some_and(|value|
            value.split_whitespace().any(|part| part.to_ascii_lowercase().starts_with("trophyvalue")));
        if !trophy_row { continue; }

        let title = row.select(&select("a[id^='trophytitle']")).next()
            .context("PSNTL trophy row has no title element")?;
        let name = text_of(title);
        ensure!(!name.is_empty(), "PSNTL trophy name is empty");
        let remote_id = title.value().attr("id")
            .and_then(|s| s.strip_prefix("trophytitle"))
            .and_then(|s| s.parse::<u32>().ok())
            .context("invalid PSNTL trophy identifier")?;
        let detail = row.select(&select("div[id^='trophydescription']")).next()
            .map(text_of).unwrap_or_default();
        let grade = row.select(&select("td.trophytype")).next()
            .and_then(|value| grade_from(&text_of(value)));
        let xmb_id = row.select(&select("td.XMB, td.xmb")).next()
            .and_then(|cell| text_of(cell).parse::<u32>().ok());
        let date = row.select(&select(".date_earned")).next()
            .context("PSNTL trophy row is missing earned date cell")?;
        result.push(RemoteTrophy {
            remote_id, xmb_id, name, detail, grade,
            earned_us: psntl_timestamp(date)?,
        });
    }
    ensure!(!result.is_empty(), "PSN Trophy Leaders returned no parseable trophy rows");
    Ok(result)
}

fn parse_profile_timestamp(date: &str, time: &str) -> Result<i64> {
    // PSNProfiles: "1st Jun 2026" and "7:22:10 PM". C# parses as UTC.
    let mut words = date.split_whitespace();
    let day = words.next().context("PSNProfiles date missing day")?;
    let day = day.trim_end_matches(|c: char| c.is_ascii_alphabetic());
    let remainder = words.collect::<Vec<_>>().join(" ");
    let value = format!("{day} {remainder} {time}");
    let parsed = NaiveDateTime::parse_from_str(&value, "%d %b %Y %I:%M:%S %p")
        .with_context(|| format!("unsupported PSNProfiles trophy date: {value}"))?;
    Utc.from_utc_datetime(&parsed).timestamp_micros()
        .checked_add(0).context("PSNProfiles date overflow")
}

fn parse_profiles(html: &str) -> Result<Vec<RemoteTrophy>> {
    let doc = Html::parse_document(html);
    let mut result = Vec::new();
    for row in doc.select(&select("tr")) {
        let Some(link) = row.select(&select("a[href^='/trophy/']")).next() else {
            continue;
        };
        let Some(path) = link.value().attr("href") else { continue; };
        let parts: Vec<_> = path.split('/').collect();
        if parts.len() < 4 || parts.get(1) != Some(&"trophy") { continue; }
        let remote_id = parts[3].split('-').next()
            .and_then(|id| id.parse::<u32>().ok())
            .filter(|&id| id > 0).context("invalid PSNProfiles trophy ID")?;

        let title = row.select(&select("a.title")).next()
            .context("PSNProfiles trophy has no name")?;
        let name = text_of(title);
        ensure!(!name.is_empty(), "PSNProfiles trophy has an empty title");
        let grade = row.select(&select("img[title]"))
            .find_map(|image| image.value().attr("title").and_then(grade_from));
        let completed = classes_contain(row, "completed")
            || row.select(&select("picture.trophy.earned")).next().is_some();
        let earned_us = if completed {
            let date = row.select(&select(".typo-top-date nobr")).next()
                .map(text_of).context("earned PSNProfiles trophy missing date")?;
            let time = row.select(&select(".typo-bottom-date nobr")).next()
                .map(text_of).context("earned PSNProfiles trophy missing time")?;
            Some(parse_profile_timestamp(&date, &time)?)
        } else {
            None
        };
        let detail = row.select(&select("td")).filter_map(|td| {
            let title_present = td.select(&select("a.title")).next().is_some();
            if title_present {
                let all = text_of(td);
                let rest = all.strip_prefix(&name).unwrap_or("").trim();
                Some(rest.to_owned())
            } else { None }
        }).next().unwrap_or_default();
        result.push(RemoteTrophy {
            remote_id, xmb_id: None, name, detail, grade, earned_us,
        });
    }
    ensure!(!result.is_empty(), "PSNProfiles returned no parseable trophy rows");
    Ok(result)
}

/// Read a captured trophy HTML page or fetch a supported PSN trophy page.
/// FlareSolverr is optional, configured through FLARESOLVERR_URL, and uses
/// its JSON HTTP API. No WebView, browser, or JavaScript is embedded.
pub fn load_copy_page(source: CopySource, path_or_url: &str) -> Result<String> {
    if std::path::Path::new(path_or_url).is_file() {
        return std::fs::read_to_string(path_or_url)
            .with_context(|| format!("read saved trophy HTML: {path_or_url}"));
    }

    let url = reqwest::Url::parse(path_or_url)
        .context("expected a saved HTML file or HTTPS trophy URL")?;
    ensure!(url.scheme() == "https", "Copy From only accepts HTTPS trophy pages");
    let host = url.host_str().unwrap_or("").trim_start_matches("www.");
    let expected = match source {
        CopySource::PsnProfiles => "psnprofiles.com",
        CopySource::PsnTrophyLeaders => "psntrophyleaders.com",
    };
    ensure!(host.eq_ignore_ascii_case(expected),
        "Copy From source {:?} does not match {host}", source);

    let client = reqwest::blocking::Client::builder()
        .timeout(std::time::Duration::from_secs(45))
        .user_agent("Mozilla/5.0 (compatible; PS3TrophyIsGood/0.1)")
        .build()?;

    // The original app manages FlareSolverr automatically. Try normal HTTPS
    // first for unrestricted pages, then an already running local helper.
    // Never quietly treat a Cloudflare page as a zero-trophy result.
    let direct = client.get(url.clone()).send()
        .and_then(|response| response.error_for_status())
        .and_then(|response| response.text());
    if let Ok(ref html) = direct {
        if contains_trophy_rows(source, html) || !looks_like_challenge(html) {
            return Ok(html.clone());
        }
    }

    let helper = std::env::var("FLARESOLVERR_URL")
        .unwrap_or_else(|_| "http://127.0.0.1:8191".into());
    let endpoint = if helper.trim_end_matches('/').ends_with("/v1") {
        helper.trim_end_matches('/').to_string()
    } else {
        format!("{}/v1", helper.trim_end_matches('/'))
    };
    let request = serde_json::json!({
        "cmd": "request.get",
        "url": url.as_str(),
        "maxTimeout": 75_000
    });
    let response = client.post(&endpoint).json(&request).send()
        .with_context(|| format!(
            "Cannot fetch trophy page directly and FlareSolverr is not reachable at {endpoint}. \
             Start FlareSolverr or import saved trophy HTML"
        ))?
        .error_for_status().context("FlareSolverr HTTP error")?;
    let response: serde_json::Value = response.json()
        .context("parse FlareSolverr JSON response")?;
    ensure!(response.get("status").and_then(|v| v.as_str()) == Some("ok"),
        "FlareSolverr failed to retrieve trophy page: {response}");
    let html = response.pointer("/solution/response")
        .and_then(|v| v.as_str())
        .context("FlareSolverr returned no HTML response")?;
    Ok(html.to_owned())
}

fn contains_trophy_rows(source: CopySource, html: &str) -> bool {
    let doc = Html::parse_document(html);
    match source {
        CopySource::PsnProfiles =>
            doc.select(&select("a[href^='/trophy/']")).next().is_some(),
        CopySource::PsnTrophyLeaders =>
            doc.select(&select("tr")).any(|row|
                row.value().attr("class").is_some_and(|c|
                    c.split_whitespace().any(|x| x.to_ascii_lowercase().starts_with("trophyvalue")))),
    }
}

fn looks_like_challenge(html: &str) -> bool {
    let lower = html.to_ascii_lowercase();
    ["cf-challenge", "cf-chl-", "challenge-platform", "cf-turnstile",
     "verify you are human", "checking your browser"]
        .iter().any(|marker| lower.contains(marker))
        || (lower.contains("just a moment") && lower.contains("cloudflare"))
}

pub fn parse_copy_page(source: CopySource, html: &str) -> Result<Vec<RemoteTrophy>> {
    // A trophy page may contain leftover Cloudflare scripts after successful
    // verification. Only reject challenges when there is no trophy content.
    ensure!(contains_trophy_rows(source, html) || !looks_like_challenge(html),
        "Cloudflare verification page received without any trophy rows");
    match source {
        CopySource::PsnTrophyLeaders => parse_psntl(html),
        CopySource::PsnProfiles => parse_profiles(html),
    }
}

fn normalized(value: &str) -> String {
    value.nfkc().map(|c| match c {
        '’' | '‘' => '\'',
        '“' | '”' => '"',
        _ => c,
    }).collect::<String>().split_whitespace().collect::<Vec<_>>()
        .join(" ").to_lowercase()
}

fn punctuation_normalized(value: &str) -> String {
    normalized(value).chars().map(|ch|
        if matches!(ch, '-' | '‐' | '‑' | '‒' | '–' | '—') { ' ' } else { ch })
        .collect::<String>().split_whitespace().collect::<Vec<_>>().join(" ")
}

fn normalized_detail(value: &str) -> String {
    let value = normalized(value);
    let trailing_removed = if value.ends_with(')') {
        value.rfind('(').map(|p| value[..p].trim()).unwrap_or(&value)
    } else { &value };
    trailing_removed.trim_end_matches(['.', ' ']).to_string()
}

/// Source numeric IDs are never treated as local indices. PSNTL XMB IDs
/// are used only when the remote name and grade corroborate the local ID.
/// Other mappings are identity-matched and must be one-to-one; source rows
/// specific to another version/DLC may be ignored only if every row in the
/// smaller side is unambiguously accounted for.
pub fn map_remote_to_local(
    local: &[Trophy], remote: &[RemoteTrophy],
) -> Result<Vec<(u32, Option<i64>)>> {
    ensure!(!local.is_empty() && !remote.is_empty(), "empty trophy set");
    let mut mapped: HashMap<u32, Option<i64>> = HashMap::new();
    let mut remote_ids = HashSet::new();
    for item in remote {
        ensure!(remote_ids.insert(item.remote_id),
            "duplicate remote trophy identifier {}", item.remote_id);
        let name = normalized(&item.name);
        ensure!(!name.is_empty(), "remote trophy has no title");

        let mut candidates: Vec<&Trophy> = local.iter()
            .filter(|t| normalized(&t.name) == name).collect();
        if let Some(grade) = item.grade {
            // When a valid grade is known it must agree; rejecting a
            // mismatched type is safer than accepting a false title match.
            candidates.retain(|t| t.grade == grade);
        }
        if candidates.len() > 1 {
            let detail = normalized_detail(&item.detail);
            if !detail.is_empty() {
                candidates.retain(|t| normalized_detail(&t.description) == detail);
            }
        }
        if candidates.is_empty() {
            if let Some(grade) = item.grade {
                let loose_name = punctuation_normalized(&item.name);
                candidates = local.iter().filter(|t|
                    t.grade == grade && punctuation_normalized(&t.name) == loose_name
                ).collect();
            }
        }
        if candidates.is_empty() { continue; }
        ensure!(candidates.len() == 1,
            "ambiguous local trophy identity for {:?}; no trophies modified", item.name);
        let candidate = candidates[0];

        if let Some(xmb) = item.xmb_id {
            if let Some(xmb_local) = local.iter().find(|t| t.id == xmb) {
                ensure!(xmb_local.id == candidate.id,
                    "XMB Trophy ID {} contradicts title {:?}; no trophies modified",
                    xmb, item.name);
            }
        }

        ensure!(mapped.insert(candidate.id, item.earned_us).is_none(),
            "multiple source trophies matched local ID {}; no trophies modified", candidate.id);
    }
    let expected = local.len().min(remote.len());
    ensure!(mapped.len() == expected,
        "only {} trophies matched by unique identity (local {}, remote {}); no trophies modified",
        mapped.len(), local.len(), remote.len());
    let mut result: Vec<_> = mapped.into_iter().collect();
    result.sort_by_key(|(id, _)| *id);
    Ok(result)
}

#[derive(Debug, Clone, Copy)]
pub struct SmartCopyOptions {
    pub years: u32,
    pub months: u32,
    pub days: u32,
    /// The original WinForms algorithm labels this as minutes but calls
    /// TimeSpan.FromSeconds; preserve that actual code behavior for parity.
    pub min_jitter_seconds: i64,
    pub max_jitter_seconds: i64,
}

/// Preserve the chronological order of obtained trophies and return results
/// indexed by actual trophy ID, not by sorted display order.
pub fn smart_copy(
    mapping: &mut [(u32, Option<i64>)], opts: SmartCopyOptions,
) -> Result<()> {
    ensure!(opts.min_jitter_seconds <= opts.max_jitter_seconds,
        "Smart Copy minimum exceeds maximum");
    let days = i64::from(opts.years).checked_mul(365)
        .and_then(|n| n.checked_add(i64::from(opts.months) * 30))
        .and_then(|n| n.checked_add(i64::from(opts.days)))
        .context("Smart Copy date offset overflow")?;
    let base_delta = days.checked_mul(86_400_000_000).context("Smart Copy time overflow")?;
    let mut rng = rand::thread_rng();
    let jitter = |rng: &mut rand::rngs::ThreadRng| {
        if opts.min_jitter_seconds == opts.max_jitter_seconds {
            opts.min_jitter_seconds
        } else {
            // .NET Random.Next(min, max) excludes the upper bound.
            rng.gen_range(opts.min_jitter_seconds..opts.max_jitter_seconds)
        }
    };
    let first_jitter = jitter(&mut rng).checked_mul(1_000_000)
        .context("Smart Copy initial jitter overflow")?;
    let mut delta = base_delta.checked_add(first_jitter)
        .context("Smart Copy delta overflow")?;
    let mut order: Vec<usize> = (0..mapping.len()).collect();
    order.sort_by_key(|&ix| mapping[ix].1);
    for (position, ix) in order.iter().copied().enumerate() {
        let Some(before) = mapping[ix].1 else { continue; };
        mapping[ix].1 = Some(before.checked_add(delta)
            .context("Smart Copy timestamp overflow")?);
        if let Some(&next) = order.get(position + 1) {
            if let (Some(now), Some(future)) = (mapping[ix].1, mapping[next].1) {
                if future.checked_sub(now).is_some_and(|gap| gap > 60_000_000) {
                    let extra = jitter(&mut rng).checked_mul(1_000_000)
                        .context("Smart Copy additional jitter overflow")?;
                    delta = delta.checked_add(extra)
                        .context("Smart Copy additional delta overflow")?;
                }
            }
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn local(id: u32, name: &str, grade: Grade) -> Trophy {
        Trophy { id, name: name.into(), description: "Do the thing".into(),
            grade, hidden: false, group_id: 0, obtained: false,
            synced: false, earned_us: None }
    }

    #[test]
    fn psnprofiles_maps_by_name_not_site_number() {
        let html = r#"<table><tr class="completed"><td><img title="Bronze">
            <a class="title" href="/trophy/123-game/99-trophy">A &amp; B</a><br>Do the thing</td>
            <td><span class="typo-top-date"><nobr>1st Jun 2026</nobr></span>
            <span class="typo-bottom-date"><nobr>7:22:10 PM</nobr></span></td></tr></table>"#;
        let remote = parse_copy_page(CopySource::PsnProfiles, html).unwrap();
        assert_eq!(remote[0].remote_id, 99);
        let mapped = map_remote_to_local(&[local(4, "A & B", Grade::Bronze)], &remote).unwrap();
        assert_eq!(mapped[0].0, 4);
        assert_eq!(mapped[0].1, Some(1_780_341_730_000_000));
    }

    #[test]
    fn psntl_validates_xmb_id_and_does_not_guess_translated_names() {
        let html = r#"<table><tr class="trophyvalue12">
            <td class="XMB">7</td><td class="trophytype">2</td>
            <td><a id="trophytitle12">Long-Awaited &amp; Earned</a>
            <div id="trophydescription12">Do the thing</div></td>
            <td class="date_earned"><span class="sort">1700000000</span></td>
            </tr></table>"#;
        let remote = parse_copy_page(CopySource::PsnTrophyLeaders, html).unwrap();
        let mapped = map_remote_to_local(&[local(7, "Long Awaited & Earned", Grade::Gold)], &remote).unwrap();
        assert_eq!(mapped, vec![(7, Some(1_700_000_000_000_000))]);
        assert!(map_remote_to_local(&[local(7, "異なる翻訳", Grade::Gold)], &remote).is_err());
        assert!(map_remote_to_local(&[local(8, "Long Awaited & Earned", Grade::Gold)], &remote).is_ok());
    }

    #[test]
    fn ambiguous_names_fail_without_mapping() {
        let r = vec![RemoteTrophy {
            remote_id: 9, xmb_id: None, name: "Duplicate".into(),
            detail: String::new(), grade: Some(Grade::Silver), earned_us: None,
        }];
        let local = vec![local(0, "Duplicate", Grade::Silver), local(1, "Duplicate", Grade::Silver)];
        assert!(map_remote_to_local(&local, &r).is_err());
    }

    #[test]
    fn auto_detects_both_live_urls_and_saved_html() {
        assert_eq!(detect_copy_source(
            "https://psnprofiles.com/trophies/999-game/user", None).unwrap(),
            CopySource::PsnProfiles);
        assert_eq!(detect_copy_source(
            "https://psntrophyleaders.com/user/view/user/game", None).unwrap(),
            CopySource::PsnTrophyLeaders);
        assert!(detect_copy_source("https://example.com/trophies/foo", None).is_err());
        assert!(detect_copy_source("http://psnprofiles.com/trophies/1", None).is_err());
        assert_eq!(detect_copy_source("saved.html", Some(
            "<tr class='trophyvalue2'><td>abc</td></tr>"
        )).unwrap(), CopySource::PsnTrophyLeaders);
        assert_eq!(detect_copy_source("saved.html", Some(
            "<tr><td><a href='/trophy/10-game/2-name'>Title</a></td></tr>"
        )).unwrap(), CopySource::PsnProfiles);
    }

    #[test]
    fn solved_cloudflare_page_with_real_rows_is_not_rejected() {
        let html = r#"<script>window.cf-challenge = true;</script>
            <table><tr class="trophyvalue12"><td class="trophytype">0</td>
            <td><a id="trophytitle12">First</a></td>
            <td class="date_earned" data-sort="0"></td></tr></table>"#;
        let items = parse_copy_page(CopySource::PsnTrophyLeaders, html).unwrap();
        assert_eq!(items.len(), 1);
        assert_eq!(items[0].earned_us, None);
        assert!(parse_copy_page(CopySource::PsnTrophyLeaders,
            "<html><title>Just a moment... Cloudflare</title></html>").is_err());
    }

    #[test]
    fn smart_copy_rejects_overflow_instead_of_panicking() {
        let mut mapping = [(0, Some(1_700_000_000_000_000))];
        let err = smart_copy(&mut mapping, SmartCopyOptions {
            years: 0, months: 0, days: 0,
            min_jitter_seconds: i64::MAX,
            max_jitter_seconds: i64::MAX,
        }).unwrap_err();
        assert!(err.to_string().contains("overflow"));
    }

    #[test]
    fn smart_copy_preserves_none_and_uses_id_not_index() {
        let mut result = vec![(7, Some(1_700_000_000_000_000)), (0, None),
            (3, Some(1_700_000_100_000_000))];
        smart_copy(&mut result, SmartCopyOptions {
            years: 0, months: 0, days: 1,
            min_jitter_seconds: 0, max_jitter_seconds: 0,
        }).unwrap();
        assert_eq!(result[0].1, Some(1_700_086_400_000_000));
        assert_eq!(result[1].1, None);
        assert_eq!(result[2].1, Some(1_700_086_500_000_000));
    }
}
