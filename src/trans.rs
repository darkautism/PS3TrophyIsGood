//! PS3 TROPTRNS.DAT: the chronological trophy log.
//! Binary record layout is taken from the original TROPHYParser/TROPTRNS.cs
//! and kept independent from the GPUI frontend.
use anyhow::{Context, Result, bail, ensure};
use std::collections::HashMap;
use crate::trophy::{Grade, Trophy};

const MAGIC: u64 = 0x00000100ad548f81;
const EPOCH_US: i64 = 62_135_596_800_000_000;
const HEADER_LEN: usize = 16;
const INFO_LEN: usize = 160;

fn be32(data: &[u8], at: usize) -> Result<u32> {
    Ok(u32::from_be_bytes(data.get(at..at + 4)
        .context("TROPTRNS field is outside file")?.try_into()?))
}
fn be64(data: &[u8], at: usize) -> Result<u64> {
    Ok(u64::from_be_bytes(data.get(at..at + 8)
        .context("TROPTRNS field is outside file")?.try_into()?))
}
fn write_be32(data: &mut [u8], at: usize, value: u32) -> Result<()> {
    data.get_mut(at..at + 4).context("TROPTRNS counter out of bounds")?
        .copy_from_slice(&value.to_be_bytes());
    Ok(())
}

#[derive(Debug, Clone)]
struct Entry {
    id: u32,
    sync: bool,
    time_us: i64,
    data: [u8; INFO_LEN],
}

/// A parsed, editable version of the trophy transaction log.
/// Non-edited header and record bytes are preserved.
#[derive(Debug, Clone)]
pub(crate) struct Trans {
    raw: Vec<u8>,
    entries: HashMap<u32, Entry>,
    type3: usize,
    type4: usize,
    record_stride: usize,
    capacity: usize,
}

impl Trans {
    pub(crate) fn load(raw: Vec<u8>, trophies: &mut [Trophy]) -> Result<Self> {
        ensure!(raw.len() >= 48, "TROPTRNS.DAT is truncated");
        ensure!(u64::from_le_bytes(raw[0..8].try_into()?) == MAGIC,
            "TROPTRNS.DAT is encrypted or uses an unrecognized layout");
        let n = be32(&raw, 8)? as usize;
        ensure!(n <= 128, "invalid TROPTRNS record table");
        let mut table = HashMap::new();
        for ix in 0..n {
            let at = 48 + ix * 32;
            let ty = be32(&raw, at)?;
            let count = be32(&raw, at + 4)? as usize;
            let offset = usize::try_from(be64(&raw, at + 16)?)?;
            ensure!(offset < raw.len(), "TROPTRNS record offset out of bounds");
            ensure!(table.insert(ty, (count, offset)).is_none(), "duplicate TROPTRNS type {ty}");
        }

        let (_, type3) = *table.get(&3).context("TROPTRNS is missing type-3 metadata")?;
        let (capacity, type4) = *table.get(&4).context("TROPTRNS is missing the event log")?;
        let original_count = be32(&raw, type3 + HEADER_LEN + 16 + 4)? as usize;
        ensure!(original_count >= 1 && original_count <= capacity + 1,
            "TROPTRNS event count exceeds reserved slots");

        let block_len = be32(&raw, type4 + 4)? as usize;
        ensure!(block_len == INFO_LEN, "unsupported TROPTRNS entry size: {block_len}");
        let record_stride = HEADER_LEN + block_len;
        let last = type4.checked_add(record_stride.checked_mul(capacity + 1)
            .context("TROPTRNS table size overflow")?)
            .context("TROPTRNS table size overflow")?;
        ensure!(last <= raw.len(), "truncated TROPTRNS event table");

        let mut entries = HashMap::new();
        for ix in 1..original_count {
            let at = type4 + ix * record_stride + HEADER_LEN;
            let slot: [u8; INFO_LEN] = raw[at..at + INFO_LEN].try_into()?;
            let id = be32(&slot, 32)?;
            ensure!(trophies.iter().any(|t| t.id == id), "unknown event trophy ID {id}");
            let trophy = trophies.iter_mut().find(|t| t.id == id).unwrap();
            ensure!(be32(&slot, 36)? == trophy.grade.type_id(),
                "TROPTRNS/TROPCONF grade mismatch for trophy #{id}");
            let time = be64(&slot, 48)?;
            ensure!(time >= EPOCH_US as u64, "invalid TROPTRNS trophy timestamp");
            let time_us = i64::try_from(time)? - EPOCH_US;
            let sync = slot[11] != 0;
            if !trophy.obtained || trophy.earned_us != Some(time_us) {
                trophy.obtained = true;
                trophy.earned_us = Some(time_us);
            }
            trophy.synced |= sync;
            ensure!(entries.insert(id, Entry {id, sync, time_us, data: slot}).is_none(),
                "duplicate trophy in TROPTRNS.DAT");
        }
        Ok(Self {raw, entries, type3, type4, record_stride, capacity})
    }

    // Preserve the original TROPTRNS event ordering when timestamps are
    // identical. Original TROPHYParser.PopTrophy removes events backwards
    // from the transaction list, not every unlock newer than a time cutoff.
    fn event_order_key(&self, trophy: &Trophy) -> (i64, u32, u32) {
        let original_index = self.entries.get(&trophy.id)
            .and_then(|entry| be32(&entry.data, 0).ok())
            .unwrap_or(u32::MAX);
        (trophy.earned_us.unwrap_or(0), original_index, trophy.id)
    }

    pub(crate) fn unsynced_tail_ids(&self, trophies: &[Trophy]) -> Vec<u32> {
        let mut earned: Vec<_> = trophies.iter().filter(|t| t.obtained).collect();
        earned.sort_by_key(|t| self.event_order_key(t));
        let mut result = Vec::new();
        for trophy in earned.into_iter().rev() {
            if trophy.synced {
                break;
            }
            result.push(trophy.id);
        }
        result
    }

    pub(crate) fn rebuild(&mut self, trophies: &[Trophy]) -> Result<&[u8]> {
        let mut got: Vec<_> = trophies.iter().filter(|t| t.obtained).collect();
        got.sort_by_key(|t| self.event_order_key(t));
        ensure!(got.len() <= self.capacity, "not enough TROPTRNS slots for unlocked trophies");

        for (ix, trophy) in got.iter().enumerate() {
            let id = trophy.id;
            let mut record = self.entries.get(&id).map(|e| e.data)
                .unwrap_or([0u8; INFO_LEN]);
            let t = trophy.earned_us.context("unlocked trophy without timestamp")?;
            let ticks = u64::try_from(t.checked_add(EPOCH_US)
                .context("TROPTRNS timestamp overflow")?)?;
            record[0..4].copy_from_slice(&u32::try_from(ix + 1)?.to_be_bytes());
            record[7] = 2; // IsExist
            record[11] = if trophy.synced { 1 } else { 0 };
            record[32..36].copy_from_slice(&id.to_be_bytes());
            record[36..40].copy_from_slice(&trophy.grade.type_id().to_be_bytes());
            if !self.entries.contains_key(&id) {
                // Original TrophyInfo constructor initializes this unknown field.
                record[40..44].copy_from_slice(&0x00100000_u32.to_le_bytes());
            }
            record[44..48].fill(0);
            record[48..56].copy_from_slice(&ticks.to_be_bytes());
            record[56..64].copy_from_slice(&ticks.to_be_bytes());
            let at = self.type4 + (ix + 1) * self.record_stride + HEADER_LEN;
            self.raw[at..at + INFO_LEN].copy_from_slice(&record);
        }
        for ix in got.len()..self.capacity {
            let at = self.type4 + (ix + 1) * self.record_stride + HEADER_LEN;
            self.raw[at..at + INFO_LEN].fill(0);
            self.raw[at..at + 4].copy_from_slice(&u32::try_from(ix + 1)?.to_be_bytes());
        }

        // Type 3 has a fixed-size 16-byte trophy-set ID followed by the
        // block type constant and both counters (the first record is an init).
        write_be32(&mut self.raw, self.type3 + HEADER_LEN + 16 + 4,
            u32::try_from(got.len() + 1)?)?;
        write_be32(&mut self.raw, self.type3 + HEADER_LEN + 16 + 8,
            u32::try_from(got.iter().filter(|t| t.synced).count() + 1)?)?;

        Ok(&self.raw)
    }
}


#[cfg(test)]
mod tests {
    use super::*;
    use crate::trophy::Trophy;

    fn trophy(id: u32, grade: Grade) -> Trophy {
        Trophy {
            id,
            name: format!("Trophy {id}"),
            description: String::new(),
            grade,
            hidden: false,
            group_id: 0,
            obtained: false,
            synced: false,
            earned_us: None,
        }
    }

    // The actual type table and event slot layout are taken from the
    // WinForms TROPHYParser/TROPTRNS.cs binary structures, not from RPCS3.
    fn empty_native_trns(capacity: usize) -> Vec<u8> {
        let type3 = 48 + 2 * 32;
        let type4 = type3 + 64;
        let stride = 16 + INFO_LEN;
        let mut raw = vec![0u8; type4 + (capacity + 1) * stride];
        raw[0..8].copy_from_slice(&MAGIC.to_le_bytes());
        raw[8..12].copy_from_slice(&2u32.to_be_bytes());

        let a = 48;
        raw[a..a + 4].copy_from_slice(&3u32.to_be_bytes());
        raw[a + 4..a + 8].copy_from_slice(&1u32.to_be_bytes());
        raw[a + 16..a + 24].copy_from_slice(&(type3 as u64).to_be_bytes());

        let b = 48 + 32;
        raw[b..b + 4].copy_from_slice(&4u32.to_be_bytes());
        raw[b + 4..b + 8].copy_from_slice(&(capacity as u32).to_be_bytes());
        raw[b + 16..b + 24].copy_from_slice(&(type4 as u64).to_be_bytes());

        raw[type3..type3 + 4].copy_from_slice(&3u32.to_be_bytes());
        raw[type3 + 4..type3 + 8].copy_from_slice(&48u32.to_be_bytes());
        raw[type3 + 16 + 16 + 4..type3 + 16 + 16 + 8]
            .copy_from_slice(&1u32.to_be_bytes());
        raw[type3 + 16 + 16 + 8..type3 + 16 + 16 + 12]
            .copy_from_slice(&1u32.to_be_bytes());

        raw[type4..type4 + 4].copy_from_slice(&4u32.to_be_bytes());
        raw[type4 + 4..type4 + 8].copy_from_slice(&(INFO_LEN as u32).to_be_bytes());
        raw
    }

    #[test]
    fn native_transaction_log_rebuild_reload_and_sync_counters() {
        let mut raw = empty_native_trns(3);
        // Header and the init record must survive editing.
        let init = 48 + 2 * 32 + 64 + 16;
        raw[init + 70] = 0xA5;
        let initial = vec![
            trophy(0, Grade::Platinum),
            trophy(1, Grade::Bronze),
            trophy(2, Grade::Silver),
        ];
        let mut trophies = initial.clone();
        let mut trans = Trans::load(raw, &mut trophies).unwrap();
        assert!(trophies.iter().all(|t| !t.obtained));

        trophies[1].obtained = true;
        trophies[1].earned_us = Some(1_700_000_000_000_000);
        trophies[1].synced = true;
        trophies[2].obtained = true;
        trophies[2].earned_us = Some(1_700_000_100_000_000);
        let rebuilt = trans.rebuild(&trophies).unwrap().to_vec();
        assert_eq!(rebuilt[init + 70], 0xA5);
        let type3 = 48 + 2 * 32;
        assert_eq!(be32(&rebuilt, type3 + 16 + 16 + 4).unwrap(), 3);
        assert_eq!(be32(&rebuilt, type3 + 16 + 16 + 8).unwrap(), 2);

        let mut reopened = initial.clone();
        let mut second = Trans::load(rebuilt, &mut reopened).unwrap();
        assert!(reopened[1].obtained && reopened[1].synced);
        assert!(reopened[2].obtained && !reopened[2].synced);
        assert_eq!(reopened[1].earned_us, Some(1_700_000_000_000_000));
        assert_eq!(reopened[2].earned_us, Some(1_700_000_100_000_000));

        reopened[1].synced = false;
        reopened[1].obtained = false;
        reopened[1].earned_us = None;
        let rebuilt_again = second.rebuild(&reopened).unwrap().to_vec();
        let mut last = initial;
        let _ = Trans::load(rebuilt_again.clone(), &mut last).unwrap();
        assert!(!last[1].obtained);
        assert!(last[2].obtained);
        assert_eq!(be32(&rebuilt_again, type3 + 16 + 16 + 4).unwrap(), 2);
        assert_eq!(be32(&rebuilt_again, type3 + 16 + 16 + 8).unwrap(), 1);
    }

    #[test]
    fn clear_tail_preserves_unsynced_events_before_sync_with_equal_timestamps() {
        let mut init = vec![
            trophy(0, Grade::Bronze), trophy(1, Grade::Silver),
            trophy(2, Grade::Gold),
        ];
        let mut tr = Trans::load(empty_native_trns(3), &mut init).unwrap();
        for trophy in &mut init {
            trophy.obtained = true;
            trophy.earned_us = Some(1_700_000_000_000_000);
        }
        init[1].synced = true;
        let serialized = tr.rebuild(&init).unwrap().to_vec();
        let mut reopened = vec![
            trophy(0, Grade::Bronze), trophy(1, Grade::Silver),
            trophy(2, Grade::Gold),
        ];
        let parsed = Trans::load(serialized, &mut reopened).unwrap();
        assert!(reopened[1].synced);
        // Original PopTrophy stops when it reaches trophy #1, even though
        // trophy #0 has the exact same timestamp as the synced entry.
        assert_eq!(parsed.unsynced_tail_ids(&reopened), vec![2]);
    }

    #[test]
    fn native_transaction_log_rejects_overflow_and_unknown_trophies() {
        let raw = empty_native_trns(1);
        let mut trophies = vec![trophy(0, Grade::Bronze)];
        let mut trans = Trans::load(raw, &mut trophies).unwrap();
        trophies.push(trophy(1, Grade::Gold));
        trophies.iter_mut().for_each(|t| {
            t.obtained = true;
            t.earned_us = Some(1_700_000_000_000_000);
        });
        assert!(trans.rebuild(&trophies).is_err());
    }
}
