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
            let backup = session.save_rpcs3_with_backup()?;
            println!("Saved RPCS3 trophy #{id}. Original DAT backed up at {}", backup.display());
            Ok(())
        }
        Some("lock") => {
            let folder = PathBuf::from(args.next().context("expected trophy folder")?);
            let id: u32 = args.next().context("expected trophy ID")?
                .to_string_lossy().parse().context("invalid trophy ID")?;
            let mut session = TrophySession::open(&folder)?;
            session.lock(id)?;
            let backup = session.save_rpcs3_with_backup()?;
            println!("Locked RPCS3 trophy #{id}. Original DAT backed up at {}", backup.display());
            Ok(())
        }
        Some("--help") | Some("-h") => {
            println!("PS3TrophyIsGood Rust — GPU trophy viewer\n\
                      Usage:\n  ps3trophyisgood [<folder>]  open graphical viewer\n\
                      ps3trophyisgood inspect <folder>  inspect DAT and SFM on CLI\n\
                      ps3trophyisgood unlock <folder> <id> <UTC RFC3339>\n\
                      ps3trophyisgood lock <folder> <id>");
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
