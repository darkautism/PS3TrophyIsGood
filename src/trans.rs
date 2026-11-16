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
#[derive(Debug)]
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

    pub(crate) fn rebuild(&mut self, trophies: &[Trophy]) -> Result<&[u8]> {
        let mut got: Vec<_> = trophies.iter().filter(|t| t.obtained).collect();
        got.sort_by_key(|t| (t.earned_us.unwrap_or(0), t.id));
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
