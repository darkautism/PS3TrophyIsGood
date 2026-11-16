# PS3TrophyIsGood: master → rust feature and verification audit

This document compares the **source code** of `master` (including the
`TROPHYParser` submodule) against the ongoing GPUI rewrite. It intentionally
does **not** claim native PS3 correctness from a successful Cargo build.
The `master` branch is read-only and remains the product specification.

Status terminology:

- **Code present** — implementation exists; not necessarily verified on hardware.
- **Synthetic-tested** — validated using generated fixtures only.
- **Needs real-file test** — requires unmodified native PS3 trophy data before release.
- **Missing** — not yet ported.
- **Partial** — parity or UI behavior differs from `master`.

| Original behavior / acceptance criterion | Master source of truth | Rust status | Outstanding verification or functionality |
|---|---|---|---|
| Open an original PS3 trophy directory | `MainAPP.OpenFile`, `Utility.decryptTrophy` | Code present | Decryption using genuine NPWR directory |
| RPCS3 format support (secondary) | `MainAPP`, `TROPHYParser` | Synthetic-tested | Real RPCS3 sample compatibility |
| Auto-detect PS3 vs RPCS3 | Original toggle; automatic detection is requested improvement | Code present | Test multiple retail and RPCS3 `TROPCONF.SFM` variants |
| Parse names, descriptions, grades, hidden, DLC GID, npcommid | `TROPCONF.cs` | Synthetic-tested | Multi-language SFM fixture coverage |
| Parse PS3 `TROPUSR.DAT` | `TROPUSR.cs` | Code present | Compare binary fields against retail inputs |
| Parse and edit PS3 `TROPTRNS.DAT` | `TROPTRNS.cs` | Synthetic-tested | Real encrypted/decrypted transaction logs |
| `PARAM.PFD`, `PARAM.SFO`, decrypt, encrypt, re-sign | `Utility.encryptTrophy` / pfdtool | Code present | Real PFD check + re-encrypt/decrypt/PS3 console validation |
| Fail save if PFD verification reports invalid hashes | pfdtool `-c` | Code present | Requires native save; checks `FAIL` output despite exit status 0 |
| Back up original bytes before write | `Utility.CopyTrophyData` | Code present | Real-file write/reopen verification |
| Modify an individual unsynced trophy's obtainment time | `ChangeTrophyTime` | Core and native GPUI editable ISO time field | Real PS3 save, date-picker UX refinements |
| Unlock/lock individual trophy | `UnlockTrophy`, `DeleteTrophy` | Synthetic-tested, GUI selected edit & double-click actions | Confirm actual desktop behavior and native PS3 writeback |
| Preserve PSN sync flags and last synced time boundary | `TROPUSR`, `TROPTRNS`, `MainAPP.ValidateSelectedDate` | Code present | Cross-check against genuine synchronized trophy data |
| Display all 8 original columns and PNG icons | `MainAPP.RefreshComponents` | Code present | Actual desktop visual review, resizing, tooltips |
| Independent trophy IDs vs row ordering | `TROPCONF`, UI | Code present | Tests for sorted and filtered editing |
| Sort, filter, virtual scrolling | `ListViewEx`; user-requested enhancement | All-column sorting/filter/virtualization code present | Resizable widths, horizontal scrolling and runtime test |
| PSN synchronized count, EXP weights, completion | `MainAPP.CompletionRates` | Synthetic-tested | Visual review with native synchronized files |
| Drag/drop directories and SFM file | `MainAPP.Form1_DragDrop` | Code present | Windows Explorer, Finder, Linux file manager tests |
| Open with standard folder picker | `MainAPP.OpenFile` | Code present | Desktop runtime tests |
| Save, refresh, close, unsaved-edit confirmation | `MainAPP.SaveFile`, `CloseFile` | GUI actions and window-close confirmation code present | Runtime check of native dialogs and window close handling |
| Instant Platinum base-game → Platinum → DLC | `MainAPP.瞬間白金...` | Synthetic-tested, GPUI random date controls present | Real native PS3 round trip and time semantics |
| Clear unsynchronized trophy log tail | `MainAPP.清除獎杯...`, `TROPTRNS.PopTrophy` | Synthetic-tested | Compare behavior on irregular/same-time event sequences |
| Random start/end settings, batch random dates | `MainAPP.setRandomStartTime...`, `setRandomEndTime...` | GUI interval controls and instant platinum | General batch time editor and validation on hardware |
| Copy From PSN Trophy Leaders | `CopyFrom.cs`, `CopyFrom.IdentityMapping.cs` | Automatic URL / saved-HTML detection, identity-based mapping, GUI + CLI, opt-in original sync reset, regression tests | Live page and actual native PS3 file tests |
| Copy From PSNProfiles | `CopyFrom.PSNProfiles.cs`, `CopyFrom.IdentityMapping.cs` | Auto-detected import, UTC timestamps, name/type mapping, optional synced overwrite with confirmation | Actual live HTML and native encrypted PS3 file tests |
| Smart Copy / time offset | `CopyFrom.smartCopy`, `smartCopyPairs` | Core algorithm, GUI options and CLI present; synthetic-tested | Offset edge cases, live import and original UI parity |
| XMB trophy ID mapping, name/description fallback, issue #65 | `CopyFrom.IdentityMapping.cs` | ID corroboration plus strict type/name/description mapping present | Different trophy-set versions, translated site names / live samples |
| Cloudflare/FlareSolverr verification | `FlareSolverrManager.cs`, CopyFrom UIs | Direct HTTPS then existing FlareSolverr endpoint (env URL or localhost:8191); saved HTML supported; residual challenge scripts tolerated | **Missing original auto-install/start helper and embedded WebView2 human verification**; live testing still needed |
| Profile resign using external SFO | `Utility.encryptTrophy`, profile combo | Native profile file picker, CLI and SFO account-ID copy synthetic tests | Real PS3 account signing and PFD verification |
| English, zh-Hant, pt-BR language settings | `MainAPP.cs`, `*.resx` | Missing | Persist settings and complete GUI localization |
| Original application icon, Windows without console | `orignal.ico` / Windows resource | Code present | Inspect actual Windows executable + taskbar/title icon |
| Standalone native helper on three platforms | original bundled Windows pfdtool | CI packaged; macOS/Linux now reuse original master PFD keys | Exercise helpers with genuine native PS3 saves |
| Low idle cost, event-driven GPU UI | GPUI target | Code present | Idle CPU/RAM/GPU profiling, DPI/scaling |
| Complete 1:1 parity | All original sources | **Not achieved** | Close all missing / partial rows and perform hardware acceptance |

## Evidence classes

The GitHub Actions matrix runs `cargo test --no-default-features`
and builds the GUI for Windows, macOS and Linux. This proves only
test and compilation success on those runners. It does not launch the UI,
test drag-and-drop or verify native trophy files on PS3 hardware.

The new native PFD save pipeline:
1. Makes an isolated working copy; decrypts `TROPTRNS.DAT`.
2. Updates `TROPUSR.DAT` and reconstructs `TROPTRNS.DAT`.
3. Runs pfdtool `-u` and `-e`.
4. Runs pfdtool `-c`, checking the **report contents** for `FAIL`
   because the original CLI can return exit status 0 for failed checks.
5. Decrypts the just-encrypted output in **a second separate directory**
   and compares the plaintext to the expected bytes.
6. Re-parses the verified DAT outputs with Rust's native PS3 parsers and
   compares the obtained/synced state, trophy ID, and timestamp of every entry.
7. Backs up the originals, stages writes in the same filesystem and
   replaces the modified DAT/PFD files, plus SFO when a profile was chosen.

These validations add evidence but **are not a substitute** for a genuine
retail PS3 round-trip, PFD ownership/account checks, and PS3 loading/syncing.

## Release blockers (priority order)

1. Native PS3 real-file fixtures: open, edit, save, re-open, compare with original
   WinForms output and load on the hardware.
2. Real GPUI runtime checks: timestamp edit, double-click, full-width table
   scaling/resize, drag/drop, original window icon, refresh, and save/exit prompts.
3. Copy From / Smart Copy tested against real PSNProfiles and PSN Trophy Leaders
   HTML + account session/Cloudflare handling, and cross-version DLC sets.
4. Finish unported original behaviors: UI language translations and settings,
   general batch random time editing, native date picker and exact original UI flow.
5. Visual polish, resizable widths, idle profiling and proper platform packaging.
6. Reproducible physical PS3 verification and end-user acceptance evidence.

The product is the original **PlayStation 3** trophy editor. RPCS3 is only
an additional auto-detected input format.

## Copy From parity audit (2026-11-17)

The original `master` Copy From form automatically selects PSN Trophy Leaders or PSNProfiles from the URL, starts/manages FlareSolverr, can show an embedded WebView2 verification step, applies matched local trophy IDs (never website indices), and clears the sync status of **each matched trophy** before importing source timestamps. Its Smart Copy uses a year/month/day offset and a randomized amount labeled “minutes” but actually calculated as **seconds**.

The Rust core now offers both a conservative default (synced entries preserved and counted) and an explicit original-compatible option (`--overwrite-synced` in the CLI, confirmed opt-in within GUI). Changes stay in memory until Save, which makes a backup. An in-flight import cannot overwrite a folder whose trophy timestamps or sync state changed after import started. Smart Copy checks arithmetic and leaves the entire mapping intact on failure.

**Still not equivalent:** The original managed its own FlareSolverr install/process and guided human Cloudflare verification using WebView2. The Rust version can only use a running FlareSolverr helper or saved HTML. Its parsers have synthetic regressions, but live page formats and true native PS3 encrypted files have not yet been validated. No claim of complete 1:1 behavior should be made.
