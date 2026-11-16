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
        Some("--help") | Some("-h") => {
            println!("PS3TrophyIsGood Rust — GPU trophy viewer\n\
                      Usage:\n  ps3trophyisgood [<folder>]  open graphical viewer\n\
                      ps3trophyisgood inspect <folder>  inspect DAT and SFM on CLI");
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
