#![cfg_attr(all(windows, feature = "gui"), windows_subsystem = "windows")]

#[cfg(feature = "gui")]
mod gui;

use anyhow::{Context, Result};
use ps3trophyisgood::{TrophySession, format_timestamp};
use std::path::PathBuf;

fn inspect(path: PathBuf) -> Result<()> {
    let session = TrophySession::open(&path).with_context(|| format!("open {}", path.display()))?;
    println!("{} [{}] — {}", session.set.title, session.set.npcommid, session.set.format.label());
    println!("ID\tGrade\tEarned\tSynced\tUTC timestamp\tName");
    for trophy in &session.set.trophies {
        println!("{}\t{}\t{}\t{}\t{}\t{}",
            trophy.id,
            trophy.grade.label(),
            trophy.obtained,
            trophy.synced,
            format_timestamp(trophy.earned_us),
            trophy.name);
    }
    Ok(())
}

fn main() -> Result<()> {
    let mut args = std::env::args_os().skip(1);
    match args.next().as_deref().and_then(|s| s.to_str()) {
        Some("inspect") => {
            let path = args.next().context("usage: ps3trophyisgood inspect <trophy-directory>")?;
            inspect(PathBuf::from(path))
        }
        Some("unlock") | Some("set-time") => {
            let folder = PathBuf::from(args.next().context("expected trophy folder")?);
            let id: u32 = args.next().context("expected trophy ID")?
                .to_string_lossy().parse().context("invalid trophy ID")?;
            let date = args.next().context("expected RFC3339 UTC timestamp")?;
            let micros = chrono::DateTime::parse_from_rfc3339(&date.to_string_lossy())
                .context("date must be ISO-8601, e.g. 2026-11-16T12:00:00Z")?
                .timestamp_micros();
            let mut session = TrophySession::open(&folder)?;
            session.unlock_at_utc(id, micros)?;
            let format = session.set.format.label();
            let backup = session.save_with_backup()?;
            println!("Saved {format} trophy #{id}. Originals backed up at {}", backup.display());
            Ok(())
        }
        Some("lock") => {
            let folder = PathBuf::from(args.next().context("expected trophy folder")?);
            let id: u32 = args.next().context("expected trophy ID")?
                .to_string_lossy().parse().context("invalid trophy ID")?;
            let mut session = TrophySession::open(&folder)?;
            session.lock(id)?;
            let format = session.set.format.label();
            let backup = session.save_with_backup()?;
            println!("Locked {format} trophy #{id}. Originals backed up at {}", backup.display());
            Ok(())
        }
        Some("instant-platinum") => {
            let folder = PathBuf::from(args.next().context("expected trophy folder")?);
            let parse = |value: std::ffi::OsString| -> Result<i64> {
                Ok(chrono::DateTime::parse_from_rfc3339(&value.to_string_lossy())
                    .context("expected RFC3339 UTC timestamp")?.timestamp_micros())
            };
            let start = args.next().map(parse).transpose()?
                .unwrap_or(1_199_145_600_000_000);
            let end = args.next().map(parse).transpose()?
                .unwrap_or_else(|| chrono::Utc::now().timestamp_micros());
            let mut session = TrophySession::open(folder)?;
            let count = session.instant_platinum(start, end)?;
            if count > 0 {
                let backup = session.save_with_backup()?;
                println!("Unlocked {count} trophies. Originals backed up at {}", backup.display());
            } else {
                println!("No locked trophies to change.");
            }
            Ok(())
        }
        Some("clear") => {
            let folder = PathBuf::from(args.next().context("expected trophy folder")?);
            let mut session = TrophySession::open(folder)?;
            let count = session.clear_unsynced_tail()?;
            if count > 0 {
                let backup = session.save_with_backup()?;
                println!("Cleared {count} unsynchronized trophies. Originals backed up at {}", backup.display());
            } else {
                println!("No unsynchronized trophies after the last PSN sync.");
            }
            Ok(())
        }
        Some("--help") | Some("-h") => {
            println!("PS3TrophyIsGood Rust — native PlayStation 3 trophy editor
Usage:
  ps3trophyisgood [<folder>]  open GPU desktop editor
  ps3trophyisgood inspect <folder>  inspect PS3/RPCS3 DAT and SFM
  ps3trophyisgood unlock <folder> <id> <UTC RFC3339>
  ps3trophyisgood set-time <folder> <id> <UTC RFC3339>
  ps3trophyisgood lock <folder> <id>
  ps3trophyisgood instant-platinum <folder> [<start RFC3339>] [<end RFC3339>]
  ps3trophyisgood clear <folder>  clear unsynced trophy timeline tail");
            Ok(())
        }
        _ => {
            #[cfg(feature = "gui")]
            { gui::run(); Ok(()) }
            #[cfg(not(feature = "gui"))]
            { anyhow::bail!("no GUI built; try 'inspect <trophy-directory>'") }
        }
    }
}
