#![cfg_attr(all(windows, feature = "gui"), windows_subsystem = "windows")]

#[cfg(feature = "gui")]
mod gui;

use anyhow::{Context, Result};
use ps3trophyisgood::{
    TrophySession, format_timestamp, CopySource, SmartCopyOptions,
    load_copy_page, load_copy_page_auto, parse_copy_page, map_remote_to_local, smart_copy,
};
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
        Some("copy-from") => {
            let folder = PathBuf::from(args.next().context("expected trophy folder")?);
            // Accept the original explicit-source syntax and a simpler
            // automatic URL/HTML syntax, consistent with the desktop dialog.
            let source_or_target = args.next()
                .context("expected trophy URL/HTML or psnprofiles|psntl followed by URL/HTML")?;
            let (source, html) =
                if let Ok(source) = CopySource::parse(&source_or_target.to_string_lossy()) {
                    let target = args.next().context("expected trophy URL or HTML")?;
                    (source, load_copy_page(source, &target.to_string_lossy())?)
                } else {
                    load_copy_page_auto(&source_or_target.to_string_lossy())?
                };
            let remote = parse_copy_page(source, &html)?;
            let mut session = TrophySession::open(folder)?;
            let mut mapping = map_remote_to_local(&session.set.trophies, &remote)?;
            let mut reset_synced = false;
            let mut smart_used = false;
            while let Some(flag) = args.next() {
                match flag.to_str() {
                    Some("--smart") if !smart_used => {
                        smart_used = true;
                        let number = |args: &mut std::iter::Skip<std::env::ArgsOs>| -> Result<i64> {
                            Ok(args.next().context("missing Smart Copy offset value")?
                                .to_string_lossy().parse().context("invalid Smart Copy offset")?)
                        };
                        let years = u32::try_from(number(&mut args)?)?;
                        let months = u32::try_from(number(&mut args)?)?;
                        let days = u32::try_from(number(&mut args)?)?;
                        let min_jitter_seconds = number(&mut args)?;
                        let max_jitter_seconds = number(&mut args)?;
                        smart_copy(&mut mapping, SmartCopyOptions {
                            years, months, days, min_jitter_seconds, max_jitter_seconds,
                        })?;
                    }
                    Some("--overwrite-synced") if !reset_synced => {
                        // Explicitly request the original WinForms Copy From
                        // behavior: synced entries lose their sync flags.
                        reset_synced = true;
                    }
                    _ => anyhow::bail!("unknown or duplicate Copy From flag: {}", flag.to_string_lossy()),
                }
            }
            let synced_matched = mapping.iter().filter(|(id, _)|
                session.set.trophies.iter().any(|t| t.id == *id && t.synced)
            ).count();
            let changed = session.apply_copy_from_with_options(&mapping, reset_synced)?;
            if changed == 0 {
                println!("Copy From matched {} trophies; no edits. {} synced entries preserved.",
                    mapping.len(), synced_matched);
            } else {
                let backup = session.save_with_backup()?;
                println!("Copy From matched {} trophies and updated {changed}; {} synced entries {}. Originals backed up at {}",
                    mapping.len(), synced_matched,
                    if reset_synced { "reset (original-compatible)" } else { "preserved" },
                    backup.display());
            }
            Ok(())
        }
        Some("resign-profile") => {
            let folder = PathBuf::from(args.next().context("expected native PS3 trophy folder")?);
            let profile = PathBuf::from(args.next().context("expected profile PARAM.SFO")?);
            let mut session = TrophySession::open(folder)?;
            session.select_profile_for_resign(profile)?;
            let backup = session.save_with_backup()?;
            println!("Re-signed native PS3 trophy data. Originals backed up at {}", backup.display());
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
  ps3trophyisgood clear <folder>  clear unsynced trophy timeline tail
  ps3trophyisgood resign-profile <folder> <profile.sfo>
  ps3trophyisgood copy-from <folder> <URL|saved.html> [--smart <years> <months> <days> <minSeconds> <maxSeconds>] [--overwrite-synced]
  ps3trophyisgood copy-from <folder> <psnprofiles|psntl> <URL|saved.html> [--smart <years> <months> <days> <minSeconds> <maxSeconds>] [--overwrite-synced]");
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
