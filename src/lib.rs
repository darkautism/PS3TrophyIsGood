//! Rust-only trophy data layer. No GPUI dependency in the core.
//!
//! Trophy IDs are stable only within their NPWR/npcommid trophy set.
//! Sort order, filters, display language, and UI row index never identify trophies.

mod trophy;
mod ps3;
mod trans;
mod copy_from;
pub use copy_from::{
    CopySource, RemoteTrophy, SmartCopyOptions, parse_copy_page,
    map_remote_to_local, smart_copy,
};
pub use trophy::{
    Format, Grade, SortKey, Trophy, TrophySession, TrophySet,
    format_timestamp, parse_tropconf,
};
