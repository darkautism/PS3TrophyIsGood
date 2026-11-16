# PS3TrophyIsGood — Rust migration branch

**Status: experimental; NOT a 1:1 replacement for `master`.**
The original WinForms application remains on `master` without changes.
The `rust` branch has a new Git root tree, so no legacy C# source, old GUI submodules,
bundled Windows pfdtool.exe, or bottom advertising/donation links are shipped here.

## Technology

- Rust 2024 with **GPUI CE** (community-maintained, Zed-derived native GPU UI).
- Native folder picker, virtualized list, colored dark theme and trophy PNG icons.
- No JavaScript, WebView, Tauri, egui, HTML or tracking.
- Intended targets: Windows, Linux, macOS (see CI; runtime launch still needs testing).

## Current implemented functions

| Feature | Current Rust status |
| --- | --- |
| `TROPCONF.SFM` trophy names, descriptions, grades, groups and hidden state | Implemented |
| PS3/RPCS3 detection | Auto-detect from SFM XML offset; **no manual RPCS3 checkbox** |
| RPCS3 `TROPUSR.DAT` reading | Implemented with type/ID/size checks; synthetic tests |
| Trophy list, GPU image icons, virtual scrolling, sorting and obtained/locked filter | Implemented in GPUI |
| View status, trophy type, UTC earned time and synced state | Implemented |
| Selected trophy unlock/lock | Implemented in memory and via GUI; synced trophies refused |
| Assign a specific timestamp | Implemented in CLI (`set-time`); UI date picker pending |
| RPCS3 DAT save with backup | Implemented; refuses to overwrite changed original; real sample testing pending |
| Retail PS3 decryption/re-encryption and `PARAM.PFD` signing | **Not implemented**; writes refused |
| `TROPTRNS.DAT` editing and original PS3 account-profile handling | Not implemented |
| Copy From PSN Trophy Leaders and PSNProfiles | Not implemented |
| Cloudflare/FlareSolverr and Smart Copy | Not implemented |
| Random times, other batch operations, localization | Not implemented |
| 1:1 functional parity | **Not yet achieved** |

This table documents migration gaps rather than hiding them. Older features are still on
`master` until independently reimplemented and tested in Rust. Production PS3 trophy
files must never be used as the first validation data.

## Build and run

Rust stable with host GUI platform development headers / SDK:

```sh
cargo test --no-default-features
cargo run --no-default-features -- inspect /path/to/trophy/folder
cargo run -- /path/to/trophy/folder
```

GUI offers **Open Trophy Folder**, **Unlock Now**, **Lock**, **Save RPCS3**,
clickable **Trophy Name**, **Type** and **Obtainment Time** headers, and
**All / Obtained / Locked** filters. Select a row first before editing.
Saving RPCS3 produces a `TROPUSR.DAT.bak-...` backup and rejects retail PS3 files.

CLI for explicit RFC3339 UTC time editing, with a backup:

```sh
cargo run --no-default-features -- set-time /path/to/rpcs3/trophy/folder 1 2026-11-16T12:00:00Z
cargo run --no-default-features -- lock /path/to/rpcs3/trophy/folder 1
```

## Safety and parity plan

1. Verify RPCS3 parsing and modified DAT files with real archived fixture sets, including
   SFM translations, hidden trophies, platinum, synchronized records and DLC.
2. Port and test `TROPTRNS.DAT` alongside `TROPUSR.DAT`; integrate cross-platform
   pfdtool from [bucanero/pfd_sfo_tools](https://github.com/bucanero/pfd_sfo_tools)
   before enabling retail PS3 saves.
3. Port Copy From, strict trophy identity validation, Smart Copy, profiles, randomized
   timestamps and other original workflows.
4. Run behavior comparisons against original C# program and produce downloadable
   Win/macOS/Linux binaries.

`Cargo.lock` should be generated and committed after platform checks stabilize.

## License

Apache-2.0. The GPUI CE fork is packaged under its own Apache-2.0 terms.
