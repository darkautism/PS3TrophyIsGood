# PS3TrophyIsGood — Rust (experimental)

This is an **independent rewrite** on the `rust` branch. The original C#/WinForms tree and its
bottom promotional links are intentionally absent from this branch. The `master` branch is unchanged.

## Goal

A native, GPU-accelerated, no-JavaScript desktop trophy editor using the GPUI framework
(the community-maintained GPUI CE distribution, derived from Zed's GPUI), targeting
Windows, macOS and Linux. `trophy` is the pure-Rust core; the desktop frontend is an
optional Cargo feature.

### Implemented / in progress

- Native GPUI trophy table with a type column (Platinum / Gold / Silver / Bronze).
- PS3 vs RPCS3 auto-detection from `TROPCONF.SFM`; **no RPCS3 rejection checkbox**.
- Trophy metadata from SFM, earned/synced/time state from TROPUSR.DAT, with defensive bounds checking.
- Chronological sorting and status/type filtering in the UI.
- CLI read-only inspection for headless testing.
- Original PS3 encrypted data require a compatible external pfdtool and successful validation.

### Not yet claimed as equivalent to C# master

**Do not replace the working application yet.** This is a migration branch, not a 1:1 release.
PS3 signing/encryption, complete TROPTRNS rewrite, account profile editing, batch operations,
Smart Copy / the two web importers / Cloudflare, and full interactive time editing need
comparison against real fixtures. The GUI may change while GPUI APIs are stabilized.

## Build

```sh
cargo test --no-default-features
cargo run --no-default-features -- inspect /path/to/RPCS3/trophy/folder
cargo run --features gui
```

The graphical version includes a directory chooser; `TROPUSR.DAT` must be readable
(or provide a decrypted working copy for PS3). Do not edit your only copy of a trophy set.

### License and dependencies

Apache-2.0. GPUI CE is derived from Zed GPUI and is Apache-2.0. PFD/SFO files
have their own formats and must be separately tested with cross-platform pfdtool
(see https://github.com/bucanero/pfd_sfo_tools).

No advertisements, donation links, JavaScript, WebView, or Tauri are included.
