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
| Modify an individual unsynced trophy's obtainment time | `ChangeTrophyTime` | Core code present; GUI missing | GUI date/time editor and actual PS3 save |
| Unlock/lock individual trophy | `UnlockTrophy`, `DeleteTrophy` | Synthetic-tested; GUI partial | Confirmation and double-click behavior |
| Preserve PSN sync flags and last synced time boundary | `TROPUSR`, `TROPTRNS`, `MainAPP.ValidateSelectedDate` | Code present | Cross-check against genuine synchronized trophy data |
| Display all 8 original columns and PNG icons | `MainAPP.RefreshComponents` | Code present | Actual desktop visual review, resizing, tooltips |
| Independent trophy IDs vs row ordering | `TROPCONF`, UI | Code present | Tests for sorted and filtered editing |
| Sort, filter, virtual scrolling | `ListViewEx`; user-requested enhancement | Partial | Every column sorting, resizable widths, horizontal scrolling |
| PSN synchronized count, EXP weights, completion | `MainAPP.CompletionRates` | Synthetic-tested | Visual review with native synchronized files |
| Drag/drop directories and SFM file | `MainAPP.Form1_DragDrop` | Code present | Windows Explorer, Finder, Linux file manager tests |
| Open with standard folder picker | `MainAPP.OpenFile` | Code present | Desktop runtime tests |
| Save, refresh, close, unsaved-edit confirmation | `MainAPP.SaveFile`, `CloseFile` | Partial | Explicit close/refresh actions and window-close confirmation |
| Instant Platinum base-game → Platinum → DLC | `MainAPP.瞬間白金...` | Synthetic-tested; GUI action present | Random range selector and real native round-trip |
| Clear unsynchronized trophy log tail | `MainAPP.清除獎杯...`, `TROPTRNS.PopTrophy` | Synthetic-tested | Compare behavior on irregular/same-time event sequences |
| Random start/end settings, batch random dates | `MainAPP.setRandomStartTime...`, `setRandomEndTime...` | Partial CLI only | GUI controls for start/end and batch editing |
| Copy From PSN Trophy Leaders | `CopyFrom.cs`, `CopyFrom.IdentityMapping.cs` | Missing | Fetch, parse, safely map, import |
| Copy From PSNProfiles | `CopyFrom.PSNProfiles.cs`, `CopyFrom.IdentityMapping.cs` | Missing | Date parse, source identity validation |
| Smart Copy / time offset | `CopyFrom.smartCopy`, `smartCopyPairs` | Missing | Preset/random offset controls and tests |
| XMB trophy ID mapping, name/description fallback, issue #65 | `CopyFrom.IdentityMapping.cs` | Missing | Preserve original anti-mismatch checks |
| Cloudflare/FlareSolverr verification | `FlareSolverrManager.cs`, CopyFrom UIs | Missing | Cross-platform non-WebView equivalent |
| Profile resign using external SFO | `Utility.encryptTrophy`, profile combo | Missing | Correct account ID handling and PFD validation |
| English, zh-Hant, pt-BR language settings | `MainAPP.cs`, `*.resx` | Missing | Persist settings and complete GUI localization |
| Original application icon, Windows without console | `orignal.ico` / Windows resource | Code present | Inspect actual Windows executable + taskbar/title icon |
| Standalone native helper on three platforms | original bundled Windows pfdtool | CI packaged | Exercise all three actual binaries with native PS3 samples |
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
6. Backs up the originals, stages writes in the same filesystem and
   replaces the three modified files.

These validations add evidence but **are not a substitute** for a genuine
retail PS3 round-trip, PFD ownership/account checks, and PS3 loading/syncing.

## Release blockers (priority order)

1. Native PS3 real-file fixtures: open, edit, save, re-open, compare with original
   WinForms output and load on the hardware.
2. GPUI date/time editor, refresh/close/confirmation, full table interactions.
3. Profile/SFO support, general random time settings and other original menus.
4. Copy From / Smart Copy with robust identity matching and FlareSolverr.
5. Localization, visual UI and drag/drop runtime verification, idle performance.
6. Reproducible multi-platform artifact packaging and user acceptance.

The product is the original **PlayStation 3** trophy editor. RPCS3 is only
an additional auto-detected input format.
