//! Rust-only trophy data layer. No GPUI dependency in the core.
//!
//! Trophy IDs are stable only within their NPWR/npcommid trophy set.
//! Sort order, filters, display language, and UI row index never identify trophies.

mod trophy;
pub use trophy::{
    Format, Grade, SortKey, Trophy, TrophySession, TrophySet,
    format_timestamp, parse_tropconf,
};
