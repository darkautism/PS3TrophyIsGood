use anyhow::{Context as _, Result, bail, ensure};
use chrono::{DateTime, Utc};
use std::collections::HashSet;
use std::fs;
use std::path::{Path, PathBuf};
use crate::{ps3::Ps3Working, trans::Trans};

const MAGIC: u64 = 0x0000_0100_ad54_8f81;
const EPOCH_DELTA_US: i64 = 62_135_596_800_000_000;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Format {
    Ps3,
    Rpcs3,
}

impl Format {
    pub fn label(self) -> &'static str {
        match self {
            Self::Ps3 => "PlayStation 3",
            Self::Rpcs3 => "RPCS3",
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum Grade {
    Platinum,
    Gold,
    Silver,
    Bronze,
}

impl Grade {
    pub fn xp(self) -> u32 {
        match self {
            Self::Platinum => 180,
            Self::Gold => 90,
            Self::Silver => 30,
            Self::Bronze => 15,
        }
    }

    pub fn label(self) -> &'static str {
        match self {
            Self::Platinum => "Platinum",
            Self::Gold => "Gold",
            Self::Silver => "Silver",
            Self::Bronze => "Bronze",
        }
    }

    pub fn parse_code(value: &str) -> Result<Self> {
        match value {
            "P" => Ok(Self::Platinum),
            "G" => Ok(Self::Gold),
            "S" => Ok(Self::Silver),
            "B" => Ok(Self::Bronze),
            _ => bail!("unsupported trophy grade: {value:?}"),
        }
    }

    pub(crate) fn type_id(self) -> u32 {
        match self {
            Self::Platinum => 1,
            Self::Gold => 2,
            Self::Silver => 3,
            Self::Bronze => 4,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SortKey {
    Id,
    Name,
    Grade,
    EarnedTime,
}

#[derive(Debug, Clone)]
pub struct Trophy {
    pub id: u32,
    pub name: String,
    pub description: String,
    pub grade: Grade,
    pub hidden: bool,
    pub group_id: u32,
    pub obtained: bool,
    pub synced: bool,
    /// Microseconds since Unix epoch, as encoded in DAT; None for locked entries.
    pub earned_us: Option<i64>,
}

#[derive(Debug, Clone)]
pub struct TrophySet {
    pub npcommid: String,
    pub title: String,
    pub format: Format,
    pub trophies: Vec<Trophy>,
}

impl TrophySet {
    /// Original PS3TrophyIsGood PSN EXP table; only synced trophies count as
    /// earned PSN experience, while the denominator includes every trophy.
    pub fn psn_xp(&self) -> (u32, u32) {
        let available = self.trophies.iter().map(|t| t.grade.xp()).sum();
        let synced = self.trophies.iter().filter(|t| t.synced)
            .map(|t| t.grade.xp()).sum();
        (synced, available)
    }

    pub fn synced_count(&self) -> usize {
        self.trophies.iter().filter(|t| t.synced).count()
    }

    pub fn obtained_count(&self) -> usize {
        self.trophies.iter().filter(|t| t.obtained).count()
    }
}

pub fn format_timestamp(micros: Option<i64>) -> String {
    micros
        .and_then(DateTime::<Utc>::from_timestamp_micros)
        .map(|time| time.format("%Y-%m-%d %H:%M:%S UTC").to_string())
        .unwrap_or_else(|| "—".to_string())
}

fn xml_from_sfm(raw: &[u8]) -> Result<(&str, Format)> {
    let (input, kind) = if raw.starts_with(b"<") || raw.starts_with(&[0xef, 0xbb, 0xbf]) {
        (raw, Format::Rpcs3)
    } else if raw.len() > 64 &&
        (raw[64..].starts_with(b"<") || raw[64..].starts_with(&[0xef, 0xbb, 0xbf]))
    {
        (&raw[64..], Format::Ps3)
    } else {
        bail!("TROPCONF.SFM has no recognized PS3/RPCS3 XML header")
    };
    let trimmed = input.strip_prefix(&[0xef, 0xbb, 0xbf]).unwrap_or(input);
    let trimmed = trimmed.split(|b| *b == 0).next().unwrap_or(trimmed);
    Ok((std::str::from_utf8(trimmed).context("SFM XML is not UTF-8")?, kind))
}

pub fn parse_tropconf(raw: &[u8]) -> Result<TrophySet> {
    let (text, format) = xml_from_sfm(raw)?;
    let doc = roxmltree::Document::parse(text).context("invalid TROPCONF.SFM XML")?;
    let get = |tag: &str| -> Result<String> {
        Ok(doc.descendants()
            .find(|n| n.is_element() && n.tag_name().name() == tag)
            .and_then(|n| n.text())
            .with_context(|| format!("TROPCONF missing <{tag}>"))?
            .to_string())
    };
    let npcommid = get("npcommid")?;
    ensure!(!npcommid.trim().is_empty(), "empty trophy-set identity");
    let title = get("title-name")?;
    let mut trophies = Vec::new();
    let mut ids = HashSet::new();
    for entry in doc.descendants().filter(|n| n.is_element() && n.tag_name().name() == "trophy") {
        let id: u32 = entry.attribute("id").context("missing trophy id")?.parse()
            .context("invalid trophy id")?;
        ensure!(ids.insert(id), "duplicate trophy id {id} in SFM");
        let grade = Grade::parse_code(entry.attribute("ttype").context("missing trophy grade")?)?;
        let group_id: u32 = entry.attribute("gid").unwrap_or("0").parse()
            .context("invalid group id")?;
        let field = |tag: &str| {
            entry.children().find(|node| node.is_element() && node.tag_name().name() == tag)
                .and_then(|node| node.text()).unwrap_or("").to_string()
        };
        trophies.push(Trophy {
            id,
            name: field("name"),
            description: field("detail"),
            grade,
            hidden: entry.attribute("hidden") == Some("yes"),
            group_id,
            obtained: false,
            synced: false,
            earned_us: None,
        });
    }
    ensure!(!trophies.is_empty(), "no trophies in TROPCONF.SFM");
    trophies.sort_by_key(|t| t.id);
    Ok(TrophySet { npcommid, title, format, trophies })
}

fn be_u32(data: &[u8], at: usize) -> Result<u32> {
    let bytes: [u8; 4] = data.get(at..at+4).context("DAT truncated (u32)")?.try_into()?;
    Ok(u32::from_be_bytes(bytes))
}

fn be_u64(data: &[u8], at: usize) -> Result<u64> {
    let bytes: [u8; 8] = data.get(at..at+8).context("DAT truncated (u64)")?.try_into()?;
    Ok(u64::from_be_bytes(bytes))
}

fn read_dat(raw: &[u8], set: &mut TrophySet) -> Result<Vec<usize>> {
    ensure!(raw.len() >= 48, "TROPUSR.DAT header is truncated");
    ensure!(u64::from_le_bytes(raw[..8].try_into()?) == MAGIC, "TROPUSR.DAT is encrypted or has an unsupported magic");
    let types = be_u32(raw, 8)? as usize;
    ensure!(types < 4096, "unreasonable DAT record table length");
    let header_end = 48usize.checked_add(types.checked_mul(32).context("record table overflow")?)
        .context("record table overflow")?;
    ensure!(header_end <= raw.len(), "DAT record table is truncated");

    let mut trophy_types = Vec::new();
    let mut state = Vec::new();
    let mut pos = header_end;
    while pos + 16 <= raw.len() {
        let kind = be_u32(raw, pos)?;
        let size = be_u32(raw, pos + 4)? as usize;
        let payload_start = pos + 16;
        let next = payload_start.checked_add(size).context("DAT block overflow")?;
        ensure!(next <= raw.len(), "DAT block type {kind} is truncated");
        if kind == 0 && size == 0 { break; }
        if kind == 4 {
            ensure!(size >= 8, "DAT trophy type record too short");
            let seq = be_u32(raw, payload_start)?;
            let type_id = be_u32(raw, payload_start + 4)?;
            trophy_types.push((seq, type_id));
        } else if kind == 6 {
            ensure!(size >= 32, "DAT trophy time record too short");
            let seq = be_u32(raw, payload_start)?;
            let obtained = raw[payload_start+7] != 0;
            let sync = u32::from_le_bytes(raw[payload_start+8..payload_start+12].try_into()?);
            let synced = sync & 0x100 != 0;
            let micros = be_u64(raw, payload_start + 16)?;
            let earned_us = if obtained && micros != 0 {
                Some(i64::try_from(micros)?.checked_sub(EPOCH_DELTA_US)
                    .context("TROPUSR timestamp overflow")?)
            } else {
                None
            };
            state.push((seq, obtained, synced, earned_us, payload_start));
        }
        pos = next;
    }

    ensure!(trophy_types.len() == set.trophies.len(),
        "TROPUSR type count mismatch: {} vs {} SFM trophies", trophy_types.len(), set.trophies.len());
    ensure!(state.len() == set.trophies.len(),
        "TROPUSR trophy state count mismatch: {} vs {} SFM trophies", state.len(), set.trophies.len());
    let mut offset_by_id = Vec::new();
    for (index, trophy) in set.trophies.iter_mut().enumerate() {
        // The original parser indexes TROPUSR by trophy ID. Reject unsupported
        // layouts rather than accidentally editing the wrong entry.
        ensure!(trophy.id as usize == index, "non-contiguous trophy ID layout");
        let (type_seq, type_id) = trophy_types[index];
        ensure!(type_seq == index as u32, "unexpected DAT trophy type ID");
        ensure!(type_id == trophy.grade.type_id(),
            "DAT/SFM trophy grade mismatch at #{index}");
        let (time_seq, obtained, synced, earned_us, payload_at) = state[index];
        ensure!(time_seq == index as u32, "unexpected DAT trophy state ID");
        trophy.obtained = obtained;
        trophy.synced = synced;
        trophy.earned_us = earned_us;
        offset_by_id.push(payload_at);
    }
    Ok(offset_by_id)
}

/// An in-memory session with the raw TROPUSR bytes retained unchanged.
/// The first migration milestone is conservative: opening never modifies disk.
#[derive(Debug)]
pub struct TrophySession {
    pub source: PathBuf,
    pub set: TrophySet,
    raw_usr: Vec<u8>,
    original_usr: Vec<u8>,
    state_offsets: Vec<usize>,
    native_ps3: Option<Ps3Working>,
    transactions: Option<Trans>,
}

impl TrophySession {
    pub fn open(folder: impl AsRef<Path>) -> Result<Self> {
        let folder = folder.as_ref();
        let sfm = fs::read(folder.join("TROPCONF.SFM"))
            .with_context(|| format!("read {}", folder.join("TROPCONF.SFM").display()))?;
        let mut set = parse_tropconf(&sfm)?;
        let native_ps3 = if set.format == Format::Ps3 {
            Some(Ps3Working::open(folder).context("open and decrypt PS3 trophy folder")?)
        } else { None };
        let work_path = native_ps3.as_ref().map(|w| w.decrypted_path()).unwrap_or(folder);
        let raw_usr = fs::read(work_path.join("TROPUSR.DAT"))
            .with_context(|| format!("read {}", work_path.join("TROPUSR.DAT").display()))?;
        let state_offsets = read_dat(&raw_usr, &mut set)?;
        let transactions = if native_ps3.is_some() {
            Some(Trans::load(fs::read(work_path.join("TROPTRNS.DAT"))?,
                &mut set.trophies).context("read PS3 TROPTRNS.DAT")?)
        } else { None };
        Ok(Self { source: folder.to_path_buf(), set, original_usr: raw_usr.clone(),
            raw_usr, state_offsets, native_ps3, transactions })
    }

    /// Re-sign a native PlayStation 3 trophy set for a profile SFO.
    /// Mirrors the original WinForms profile selector: copy the 16-byte
    /// account identifier from the supplied SFO into the temporary PARAM.SFO.
    /// The actual PFD update and encrypted save occur in save_with_backup().
    pub fn select_profile_for_resign(&mut self, profile: impl AsRef<Path>) -> Result<()> {
        self.native_ps3.as_mut()
            .context("account profile re-signing requires a native PlayStation 3 trophy set")?
            .select_profile(profile.as_ref())
    }

    pub fn state_record_count(&self) -> usize {
        self.state_offsets.len()
    }

    pub fn raw_usr_len(&self) -> usize {
        self.raw_usr.len()
    }

    fn change_state(&mut self, id: u32, earned_us: Option<i64>) -> Result<()> {
        let index = usize::try_from(id)?;
        let trophy = self.set.trophies.get(index).context("unknown trophy ID")?;
        ensure!(trophy.id == id, "trophy ID and state index disagree");
        ensure!(!trophy.synced, "PSN-synced trophies cannot be edited");

        if let Some(micros) = earned_us {
            // The first PlayStation trophies were introduced in 2008.
            ensure!(micros >= 1_199_145_600_000_000, "date is earlier than 2008-01-01 UTC");
            ensure!(DateTime::<Utc>::from_timestamp_micros(micros).is_some(), "invalid date");
            let last_synced = self.set.trophies.iter()
                .filter(|t| t.synced)
                .filter_map(|t| t.earned_us)
                .max();
            if let Some(last) = last_synced {
                ensure!(micros >= last, "date predates the last PSN-synced trophy");
            }
            if trophy.grade == Grade::Platinum && !trophy.obtained {
                ensure!(self.set.trophies.iter()
                    .filter(|t| t.group_id == trophy.group_id && t.id != id)
                    .all(|t| t.obtained),
                    "unlock the other base-game trophies before Platinum");
            }
        } else if trophy.grade != Grade::Platinum && trophy.obtained {
            ensure!(!self.set.trophies.iter().any(|t|
                t.grade == Grade::Platinum && t.group_id == trophy.group_id && t.obtained),
                "lock the Platinum trophy before other base-game trophies");
        }

        let offset = *self.state_offsets.get(index).context("missing binary state offset")?;
        ensure!(offset + 32 <= self.raw_usr.len(), "state offset beyond DAT boundary");
        let converted = earned_us.map(|microseconds|
            microseconds.checked_add(EPOCH_DELTA_US).context("date overflow")
                .and_then(|v| u64::try_from(v).map_err(Into::into)))
            .transpose()?
            .unwrap_or(0);
        let bytes = converted.to_be_bytes();
        self.raw_usr[offset + 7] = if earned_us.is_some() { 1 } else { 0 };
        // Preserve the PS3TrophyIsGood unsynced flag, never mark a trophy PSN-synced.
        let state: u32 = if earned_us.is_some() { 0x0010_0000 } else { 0 };
        self.raw_usr[offset + 8..offset + 12].copy_from_slice(&state.to_le_bytes());
        self.raw_usr[offset + 16..offset + 24].copy_from_slice(&bytes);
        self.raw_usr[offset + 24..offset + 32].copy_from_slice(&bytes);

        let trophy = &mut self.set.trophies[index];
        trophy.obtained = earned_us.is_some();
        trophy.earned_us = earned_us;
        Ok(())
    }

    pub fn unlock_at_utc(&mut self, id: u32, micros: i64) -> Result<()> {
        self.change_state(id, Some(micros))
    }

    pub fn lock(&mut self, id: u32) -> Result<()> {
        self.change_state(id, None)
    }

    /// Match the original "Instant Platinum" order: unlock missing base-game
    /// trophies, unlock Platinum after the latest obtained trophy, then unlock
    /// missing DLC trophies. Each edit updates the in-memory TROPUSR state;
    /// the native TROPTRNS record is rebuilt with the *same* timestamp on save.
    /// Errors restore the entire in-memory edit, rather than leaving half a set.
    pub fn instant_platinum(&mut self, start_us: i64, end_us: i64) -> Result<usize> {
        ensure!(start_us <= end_us, "random start is after random end");
        ensure!(DateTime::<Utc>::from_timestamp_micros(start_us).is_some()
                && DateTime::<Utc>::from_timestamp_micros(end_us).is_some(),
                "random time interval is invalid");
        let last_synced = self.set.trophies.iter()
            .filter(|t| t.synced)
            .filter_map(|t| t.earned_us)
            .max();
        let lower = start_us.max(last_synced.unwrap_or(start_us));
        ensure!(lower <= end_us, "random interval ends before last synced trophy");
        let before_usr = self.raw_usr.clone();
        let before_trophies = self.set.trophies.clone();
        let result = (|| {
            use rand::Rng;
            let mut rng = rand::thread_rng();
            let mut changed = 0;
            let base: Vec<u32> = self.set.trophies.iter()
                .filter(|t| t.group_id == 0 && t.grade != Grade::Platinum && !t.obtained)
                .map(|t| t.id).collect();
            let dlc: Vec<u32> = self.set.trophies.iter()
                .filter(|t| t.group_id != 0 && !t.obtained)
                .map(|t| t.id).collect();
            let platinum: Vec<u32> = self.set.trophies.iter()
                .filter(|t| t.group_id == 0 && t.grade == Grade::Platinum && !t.obtained)
                .map(|t| t.id).collect();

            for id in base {
                let at = rng.gen_range(lower..=end_us);
                self.unlock_at_utc(id, at)?;
                changed += 1;
            }
            for id in platinum {
                let last = self.set.trophies.iter().filter_map(|t| t.earned_us)
                    .max().unwrap_or(lower);
                let at = last.checked_add(1_000_000)
                    .context("platinum timestamp overflow")?;
                self.unlock_at_utc(id, at)?;
                changed += 1;
            }
            for id in dlc {
                let at = rng.gen_range(lower..=end_us);
                self.unlock_at_utc(id, at)?;
                changed += 1;
            }
            Ok(changed)
        })();
        if result.is_err() {
            self.raw_usr = before_usr;
            self.set.trophies = before_trophies;
        }
        result
    }

    /// Emulate the original transaction-log "Clear Trophies" operation:
    /// remove only unsynchronized unlocks from the end of the timeline,
    /// stopping at the most recent synchronized entry. Synchronized trophies
    /// and older entries are not silently discarded.
    pub fn clear_unsynced_tail(&mut self) -> Result<usize> {
        let last_synced = self.set.trophies.iter()
            .filter(|t| t.synced)
            .filter_map(|t| t.earned_us).max();
        let mut candidates: Vec<(bool, i64, u32)> = self.set.trophies.iter()
            .filter(|t| t.obtained && !t.synced)
            .filter(|t| last_synced.is_none_or(|cutoff| t.earned_us.is_some_and(|t| t >= cutoff)))
            .map(|t| (t.grade == Grade::Platinum, t.earned_us.unwrap_or(0), t.id))
            .collect();
        // Remove platinum first when clearing the full set so its prerequisite
        // rules cannot prevent removing previously obtained base-game trophies.
        candidates.sort_by_key(|&(plat, time, id)| (!plat, std::cmp::Reverse(time), id));
        let before_usr = self.raw_usr.clone();
        let before_trophies = self.set.trophies.clone();
        let result = (|| {
            for &(_, _, id) in &candidates {
                self.lock(id)?;
            }
            Ok(candidates.len())
        })();
        if result.is_err() {
            self.raw_usr = before_usr;
            self.set.trophies = before_trophies;
        }
        result
    }

    /// Apply safely matched Copy From timestamps by real Trophy ID.
    /// Leave trophies not present in a different DLC/version source unchanged.
    /// The synchronized entries must never be modified. The edit is atomic
    /// in memory: a failed timestamp or Platinum prerequisite reverts all DATs.
    pub fn apply_copy_from(&mut self, mapping: &[(u32, Option<i64>)]) -> Result<usize> {
        let mut used_ids = HashSet::new();
        let mut targets = Vec::new();
        for &(id, time) in mapping {
            ensure!(used_ids.insert(id), "Copy From repeated trophy ID {id}");
            let existing = self.set.trophies.iter().find(|t| t.id == id)
                .with_context(|| format!("Copy From unknown local trophy ID {id}"))?;
            if existing.synced {
                continue; // Always keep PSN-synced entries unchanged.
            }
            if existing.earned_us != time || existing.obtained != time.is_some() {
                targets.push((id, time, existing.grade, existing.group_id));
            }
        }
        let before_usr = self.raw_usr.clone();
        let before_trophies = self.set.trophies.clone();
        let result = (|| {
            // Lock obtained Platinum before locking any prerequisite trophy;
            // then update base trophies before unlocking Platinum.
            for &(id, time, grade, _) in &targets {
                if time.is_none() && grade == Grade::Platinum {
                    self.lock(id)?;
                }
            }
            for &(id, time, grade, _) in &targets {
                if time.is_none() && grade != Grade::Platinum {
                    self.lock(id)?;
                }
            }
            let mut unlocks: Vec<_> = targets.iter()
                .filter_map(|&(id, time, grade, gid)| time.map(|t| (id, t, grade, gid)))
                .collect();
            // Actual earned times are not local Trophy IDs: sort to keep the
            // original chronology, always enforcing the Platinum prerequisite.
            unlocks.sort_by_key(|&(id, date, grade, gid)|
                (grade == Grade::Platinum, gid != 0, date, id));
            for (id, at, _, _) in unlocks {
                self.unlock_at_utc(id, at)?;
            }
            Ok(targets.len())
        })();
        if result.is_err() {
            self.raw_usr = before_usr;
            self.set.trophies = before_trophies;
        }
        result
    }

    /// Save through the original PS3 PFD signing sequence or the RPCS3 DAT
    /// writer, depending on the loaded trophy-set format.
    pub fn save_with_backup(&self) -> Result<PathBuf> {
        if let Some(working) = self.native_ps3.as_ref() {
            let mut trans = self.transactions.as_ref()
                .context("missing PS3 TROPTRNS transaction log")?.clone();
            let raw_trans = trans.rebuild(&self.set.trophies)?;
            let mut raw_usr = self.raw_usr.clone();
            refresh_ps3_usr_metadata(&mut raw_usr, &self.set.trophies)?;
            return working.save(&self.source, &self.original_usr, &raw_usr, raw_trans);
        }
        let original = self.source.join("TROPUSR.DAT");
        let current = fs::read(&original).context("re-read original DAT")?;
        ensure!(current == self.original_usr,
            "original DAT changed on disk; reopen before saving to avoid lost updates");

        // Compare outside the intentionally changed state bytes: open the
        // original for a fresh full structural check before replacement.
        let reread = TrophySession::open(&self.source)?;
        ensure!(reread.set.npcommid == self.set.npcommid &&
                reread.set.trophies.len() == self.set.trophies.len(),
                "the source trophy set changed; refuse saving");

        let mut temp = tempfile::NamedTempFile::new_in(&self.source)
            .context("create temporary DAT in trophy directory")?;
        use std::io::Write;
        temp.write_all(&self.raw_usr).context("write staged trophy DAT")?;
        temp.as_file_mut().sync_all().context("sync staged DAT")?;

        // Re-read staged file with the production validator before replacement.
        let mut check = self.set.clone();
        let _ = read_dat(&self.raw_usr, &mut check)?;

        let backup_name = format!("TROPUSR.DAT.bak-{}-{}",
            Utc::now().timestamp_millis(), std::process::id());
        let backup = self.source.join(backup_name);
        fs::copy(&original, &backup).context("create backup before writing")?;
        temp.persist(&original).map_err(|error| anyhow::anyhow!(
            "could not replace DAT, original should remain intact: {error}"
        ))?;
        Ok(backup)
    }

    /// Kept as an alias for existing CLI/tests; use save_with_backup in new UI.
    pub fn save_rpcs3_with_backup(&self) -> Result<PathBuf> {
        self.save_with_backup()
    }
}

/// Mirrors original TROPUSR.Save/UnlockTrophy counters, group bit fields,
/// and update timestamp fields for native PS3 saves.
fn refresh_ps3_usr_metadata(data: &mut [u8], trophies: &[Trophy]) -> Result<()> {
    let n = be_u32(data, 8)? as usize;
    ensure!(n < 128, "bad TROPUSR metadata table");
    let mut table = std::collections::HashMap::new();
    for ix in 0..n {
        let at = 48 + ix * 32;
        let kind = be_u32(data, at)?;
        let offset = usize::try_from(be_u64(data, at + 16)?)?;
        table.insert(kind, offset);
    }
    let tr = *table.get(&3).context("PS3 type3 record missing")?;
    let list = *table.get(&5).context("PS3 type5 metadata missing")?;
    let state = *table.get(&7).context("PS3 type7 counters missing")?;

    let count = trophies.iter().filter(|t| t.obtained).count();
    let value = u32::try_from(count)?.to_be_bytes();
    let put = |data: &mut [u8], at: usize, bytes: &[u8]| -> Result<()> {
        data.get_mut(at..at + bytes.len())
            .context("PS3 metadata offset out of range")?
            .copy_from_slice(bytes);
        Ok(())
    };
    put(data, list + 16 + 112, &value)?;
    put(data, state + 16, &value)?;

    // TROPUSR type3 has its own little-endian achievement bitmap.
    // Type5 bitmap is big-endian, matching the C# TROPHYParser serializer.
    for word in 0..4 {
        let mask = trophies.iter().filter(|t| t.obtained)
            .filter(|t| (t.id / 32) as usize == word)
            .fold(0u32, |bits, t| bits | (1u32 << (t.id % 32)));
        put(data, tr + 16 + 64 + word * 4, &mask.to_le_bytes())?;
        put(data, list + 16 + 128 + word * 4, &mask.to_be_bytes())?;
    }
    let last = trophies.iter().filter_map(|t| t.earned_us).max();
    if let Some(last) = last {
        let value = u64::try_from(last.checked_add(EPOCH_DELTA_US)
            .context("PS3 timestamp overflow")?)?.to_be_bytes();
        for at in [list + 16 + 64, list + 16 + 72] {
            put(data, at, &value)?;
        }
    }
    let now = u64::try_from(Utc::now().timestamp_micros()
        .checked_add(EPOCH_DELTA_US).context("PS3 date overflow")?)?.to_be_bytes();
    for at in [list + 16 + 32, list + 16 + 40] {
        put(data, at, &now)?;
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn xml(kind: Format) -> Vec<u8> {
        let xml = r#"<?xml version="1.0"?><trophyconf><npcommid>NPWR01682_00</npcommid><title-name>Test</title-name>
            <trophy id="0" ttype="P" hidden="no" gid="0"><name>Le véritable insurgé</name><detail>French</detail></trophy>
            <trophy id="1" ttype="B" hidden="yes" gid="0"><name>真の覚者</name><detail>Japanese</detail></trophy></trophyconf>"#.as_bytes();
        let mut bytes = if kind == Format::Ps3 { vec![0u8; 64] } else { vec![] };
        bytes.extend(xml);
        bytes
    }

    #[test]
    fn ps3_and_rpcs3_auto_detect_without_checkboxes() {
        for format in [Format::Ps3, Format::Rpcs3] {
            let set = parse_tropconf(&xml(format)).unwrap();
            assert_eq!(set.format, format);
            assert_eq!(set.npcommid, "NPWR01682_00");
            assert_eq!(set.trophies[0].grade, Grade::Platinum);
            assert_eq!(set.trophies[1].grade, Grade::Bronze);
            assert!(set.trophies[1].hidden);
        }
    }

    fn fake_rpcs3_dat() -> Vec<u8> {
        let mut out = vec![0_u8; 48 + 2 * 32];
        out[..8].copy_from_slice(&MAGIC.to_le_bytes());
        out[8..12].copy_from_slice(&2_u32.to_be_bytes());
        let mut type_records = Vec::new();
        let mut state_records = Vec::new();
        for id in 0..2_u32 {
            let mut typ = [0_u8; 96];
            typ[0..4].copy_from_slice(&4_u32.to_be_bytes());
            typ[4..8].copy_from_slice(&80_u32.to_be_bytes());
            typ[8..12].copy_from_slice(&id.to_be_bytes());
            typ[16..20].copy_from_slice(&id.to_be_bytes());
            typ[20..24].copy_from_slice(&(if id == 0 { 1_u32 } else { 4_u32 }).to_be_bytes());
            type_records.extend_from_slice(&typ);

            let mut state = [0_u8; 112];
            state[0..4].copy_from_slice(&6_u32.to_be_bytes());
            state[4..8].copy_from_slice(&96_u32.to_be_bytes());
            state[8..12].copy_from_slice(&id.to_be_bytes());
            state[16..20].copy_from_slice(&id.to_be_bytes());
            if id == 1 {
                state[23] = 1; // obtained flag
                let micros = (EPOCH_DELTA_US + 1_700_000_000_000_000) as u64;
                state[32..40].copy_from_slice(&micros.to_be_bytes());
                state[40..48].copy_from_slice(&micros.to_be_bytes());
            }
            state_records.extend_from_slice(&state);
        }
        out.extend_from_slice(&type_records);
        out.extend_from_slice(&state_records);
        out
    }

    #[test]
    fn rpcs3_binary_state_and_grades_parse() {
        let mut set = parse_tropconf(&xml(Format::Rpcs3)).unwrap();
        let offsets = read_dat(&fake_rpcs3_dat(), &mut set).unwrap();
        assert_eq!(offsets.len(), 2);
        assert!(!set.trophies[0].obtained);
        assert!(set.trophies[1].obtained);
        assert_eq!(set.trophies[1].earned_us, Some(1_700_000_000_000_000));
    }

    #[test]
    fn rpc3_trophy_edit_is_backed_up_and_reloads() {
        let dir = tempfile::tempdir().unwrap();
        fs::write(dir.path().join("TROPCONF.SFM"), xml(Format::Rpcs3)).unwrap();
        fs::write(dir.path().join("TROPUSR.DAT"), fake_rpcs3_dat()).unwrap();

        let mut session = TrophySession::open(dir.path()).unwrap();
        assert!(session.unlock_at_utc(0, 1_700_000_001_000_000).is_ok());
        let backup = session.save_rpcs3_with_backup().unwrap();
        assert!(backup.exists());
        let reloaded = TrophySession::open(dir.path()).unwrap();
        assert_eq!(reloaded.set.trophies[0].earned_us, Some(1_700_000_001_000_000));
        assert!(reloaded.set.trophies[0].obtained);
        assert!(reloaded.set.trophies[1].obtained);

        // A second write from the same session must not overwrite a newer DAT.
        assert!(session.save_rpcs3_with_backup().is_err());
    }

    #[test]
    fn refuses_to_unlock_platinum_before_base_game() {
        let dir = tempfile::tempdir().unwrap();
        fs::write(dir.path().join("TROPCONF.SFM"), xml(Format::Rpcs3)).unwrap();
        let mut dat = fake_rpcs3_dat();
        let state_offset_id1 = 112 + 96 * 2 + 112;
        dat[state_offset_id1 + 16 + 7] = 0;
        fs::write(dir.path().join("TROPUSR.DAT"), dat).unwrap();
        let mut session = TrophySession::open(dir.path()).unwrap();
        assert!(session.unlock_at_utc(0, 1_700_000_001_000_000).is_err());
    }

    #[test]
    fn psn_experience_uses_original_weights_and_only_synced_trophies() {
        let mut set = parse_tropconf(&xml(Format::Rpcs3)).unwrap();
        set.trophies[0].synced = true;
        set.trophies[1].obtained = true; // Not synced: should not count.
        assert_eq!(set.psn_xp(), (180, 195));
        assert_eq!(set.synced_count(), 1);
        assert_eq!(set.obtained_count(), 1);
    }

    #[test]
    fn instant_platinum_and_clear_round_trip() {
        let dir = tempfile::tempdir().unwrap();
        fs::write(dir.path().join("TROPCONF.SFM"), xml(Format::Rpcs3)).unwrap();
        fs::write(dir.path().join("TROPUSR.DAT"), fake_rpcs3_dat()).unwrap();
        let mut session = TrophySession::open(dir.path()).unwrap();
        let new_count = session.instant_platinum(1_700_000_100_000_000,
            1_700_000_110_000_000).unwrap();
        assert_eq!(new_count, 1);
        let plat = &session.set.trophies[0];
        assert!(plat.obtained);
        assert!(plat.earned_us > session.set.trophies[1].earned_us);
        session.save_with_backup().unwrap();

        let mut reloaded = TrophySession::open(dir.path()).unwrap();
        assert!(reloaded.set.trophies[0].obtained);
        assert_eq!(reloaded.clear_unsynced_tail().unwrap(), 2);
        reloaded.save_with_backup().unwrap();
        let cleared = TrophySession::open(dir.path()).unwrap();
        assert_eq!(cleared.set.obtained_count(), 0);
    }

    #[test]
    fn instant_platinum_rejects_invalid_range_without_mutation() {
        let dir = tempfile::tempdir().unwrap();
        fs::write(dir.path().join("TROPCONF.SFM"), xml(Format::Rpcs3)).unwrap();
        fs::write(dir.path().join("TROPUSR.DAT"), fake_rpcs3_dat()).unwrap();
        let mut session = TrophySession::open(dir.path()).unwrap();
        assert!(session.instant_platinum(1_700_000_110_000_000,
            1_700_000_100_000_000).is_err());
        assert!(!session.set.trophies[0].obtained);
        assert!(session.set.trophies[1].obtained);
    }

    #[test]
    fn duplicate_ids_rejected() {
        let mut data = xml(Format::Rpcs3);
        data = String::from_utf8(data).unwrap().replace("id=\"1\"", "id=\"0\"").into_bytes();
        assert!(parse_tropconf(&data).is_err());
    }

    #[test]
    fn time_is_unix_utc_not_localized_text_sorting() {
        assert_eq!(format_timestamp(Some(0)), "1970-01-01 00:00:00 UTC");
        assert_eq!(format_timestamp(None), "—");
    }
}
