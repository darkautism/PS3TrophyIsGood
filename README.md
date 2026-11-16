# PS3TrophyIsGood — Rust + GPUI rewrite

**Primary target: native PlayStation 3 trophies.** RPCS3 is an additional compatible
format, not the purpose of this project. This `rust` branch replaces the C# tree with
Rust code; the established `master` version is unchanged.

This branch is in **active functional migration**, not a 1:1 release. Any functionality
not in the checklist below must be compared to the original before being declared done.

## Desktop UI

GPU-accelerated GPUI CE, without JavaScript, HTML, WebView, Tauri or egui.

- Original application icon embedded in Windows executable; Windows GUI no longer
  opens a second terminal window.
- Original eight data columns: trophy name, description, type, hidden, obtained,
  PSN synchronized, timestamp and base game/DLC group. A trophy ID is also shown.
- Trophy PNG icons and virtualized scrolling.
- Original **PSN EXP** calculation: Bronze 15, Silver 30, Gold 90, Platinum 180;
  the progress bar counts synchronized trophies as in the WinForms application.
- Clickable sorting across the eight original data fields and Trophy ID;
  obtained/locked filters, row selection and double-click unlock/lock.
- Editable trophy obtainment time (ISO-8601 with timezone), random-time
  start/end fields, Instant Platinum, Clear Unsynced, native SFO profile
  re-sign selector, Refresh, Close and save/close confirmation.
- Copy From PSNProfiles and PSN Trophy Leaders with strict identity matching,
  async fetching, optional external FlareSolverr API, and Smart Copy offsets.
- Drop a trophy directory or `TROPCONF.SFM` from File Explorer/Finder to open.

## Native PS3 data pipeline

The original C# program uses `pfdtool` to decrypt `TROPTRNS.DAT`, edit
`TROPUSR.DAT` plus `TROPTRNS.DAT`, then update `PARAM.PFD` hashes and
re-encrypt the transaction log. The Rust implementation now follows that same
sequence, with the binary format code written in Rust.

On Windows the test artifact **bundles the original pfdtool executable and
configuration from `master`** as a compatibility component. The Rust branch does
not track those old Windows binaries in Git. On macOS/Linux, GitHub Actions builds and bundles pfdtool from the public
`bucanero/pfd_sfo_tools` source, **using the non-empty original master
`global.conf` and `games.conf` rather than its blank sample crypto keys**.
For a local checkout, supply the original compatible config files, or set
the `PFDTOOL_BIN` environment variable to a configured tool executable.

Native PS3 write-back now also invokes pfdtool's PFD verification and performs an
independent encrypted→decrypted `TROPTRNS.DAT` byte comparison, and re-parses
the resulting PS3 DATs to check every trophy ID, sync flag and obtainment
timestamp before writing to the original folder. It still requires
**real trophy-file and PS3 hardware comparison testing**.
Do not replace the original `master` program with this branch until that testing
is complete.

## Migration checklist

| Original feature | Current status |
| --- | --- |
| PS3 and RPCS3 SFM detection, trophy types and descriptions | Ported |
| 8-column trophy list, trophy PNG icons and original PSN EXP | Ported |
| Drag/drop trophy folder, sorting and simple filtering | Ported |
| RPCS3 TROPUSR state read and simple edit | Ported; synthetic tests |
| Native PS3 TROPTRNS chronological state parse/rebuild | Synthetic-tested; **real-file tests pending** |
| Native PS3 pfdtool decrypt/re-encrypt/PFD resign sequence | PFD check and independent decrypt verification added; **real-file tests pending** |
| Timestamp edit by explicit input | CLI and GPUI ISO-8601 editor; actual date picker not yet added |
| User-selectable account profile/resign to other SFO | GUI/CLI, synthetic-tested; native hardware verification pending |
| Copy From PSN Trophy Leaders / PSNProfiles | Rust HTML/HTTPS parser, identity checks, GUI/CLI present; live site testing pending |
| Instant Platinum and clear unsynchronized tail | Core and GUI, synthetic-tested; PS3 hardware validation pending |
| Smart Copy, user-configurable random intervals | Rust and GUI fields present, synthetic-tested |
| Full original language translations and configuration | Not implemented |
| 1:1 functional parity | **Not achieved** |

For the complete source-based parity audit and release blockers, see
[docs/FEATURE_PARITY.md](docs/FEATURE_PARITY.md).

## Build

```sh
cargo test --no-default-features
cargo run -- /path/to/ps3/trophy/directory
```

To set an exact UTC timestamp for an unlocked trophy or unlock a trophy:

```sh
cargo run --no-default-features -- set-time /path/to/trophy/folder 1 2026-11-16T12:00:00Z
```

For the original batch edit commands:

```sh
cargo run --no-default-features -- instant-platinum /path/to/trophy/folder 2026-01-01T00:00:00Z 2026-11-16T12:00:00Z
cargo run --no-default-features -- clear /path/to/trophy/folder
```

To import trophy times from a saved HTML page or an HTTPS URL:

```sh
cargo run --no-default-features -- copy-from /path/to/trophy psnprofiles ./profile.html
cargo run --no-default-features -- copy-from /path/to/trophy psntl https://psntrophyleaders.com/example
cargo run --no-default-features -- resign-profile /path/to/native/ps3/trophy ./profile.sfo
```

Set `FLARESOLVERR_URL` to an **external** FlareSolverr HTTP service if
Cloudflare prevents direct HTTPS retrieval. Downloaded pages are parsed
and matched by verified local trophy identity; unmatched or ambiguous
identities cause refusal without modifying the file.

For the GPU desktop build:

```sh
cargo build --features gui
```

Windows, macOS and Linux development builds are generated by
[GitHub Actions](https://github.com/darkautism/PS3TrophyIsGood/actions/workflows/rust.yml).
When testing real trophies, keep a separate original copy until read/write
round trips have been validated.

## License

Apache-2.0 project code. pfdtool and all external dependencies retain their
own applicable licenses. The bottom advertising links from the original
WinForms program are intentionally omitted.
