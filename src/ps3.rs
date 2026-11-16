//! Native PS3 trophy workspace. Retail PS3 records use a signed PARAM.PFD
//! plus encrypted TROPTRNS.DAT; TROPUSR.DAT is a separate plaintext record.
//! Reuse the original pfdtool protocol while the Rust PFD engine is ported.
use anyhow::{Context, Result, bail, ensure};
use std::{env, fs, path::{Path, PathBuf}, process::Command};
use tempfile::TempDir;

fn find_pfdtool() -> Result<PathBuf> {
    if let Some(path) = env::var_os("PFDTOOL_BIN") {
        let path = PathBuf::from(path);
        ensure!(path.is_file(), "PFDTOOL_BIN does not point to a file: {}", path.display());
        return Ok(path);
    }
    let bin = if cfg!(windows) { "pfdtool.exe" } else { "pfdtool" };
    let mut paths = Vec::new();
    if let Ok(exe) = env::current_exe() {
        if let Some(parent) = exe.parent() {
            paths.push(parent.join("pfdtool").join(bin));
            paths.push(parent.join(bin));
        }
    }
    paths.push(PathBuf::from("assets/pfdtool").join(bin));
    paths.push(PathBuf::from("pfdtool").join(bin));
    if let Some(path) = paths.into_iter().find(|path| path.is_file()) {
        return Ok(path.canonicalize()?);
    }
    bail!("pfdtool was not found. Install the matching PS3 pfdtool executable and global.conf next to this application (in the pfdtool folder).")
}

fn run_tool(tool: &Path, folder: &Path, command: &str, filename: Option<&str>) -> Result<()> {
    let cwd = tool.parent().context("pfdtool has no parent directory")?;
    let mut child = Command::new(tool);
    child.current_dir(cwd)
        .arg(command)
        .arg(folder);
    if let Some(file) = filename {
        child.arg(file);
    }
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        // Hide the external helper console in Windows GUI builds.
        child.creation_flags(0x08000000);
    }
    let output = child.output().context("launch pfdtool")?;
    ensure!(output.status.success(), "pfdtool {command} failed: {} {}",
        String::from_utf8_lossy(&output.stdout),
        String::from_utf8_lossy(&output.stderr));
    // pfdtool has historical versions that print an error and still exit 0.
    let text = String::from_utf8_lossy(&output.stdout).to_ascii_lowercase();
    if text.contains("[*] error:") || text.contains("error occurred") {
        bail!("pfdtool {command} reported an error: {text}");
    }
    if command == "-c" {
        // flatz's pfdtool v0.2.3 ignores backend_cmd_check's return
        // value and exits 0 even when validation reports FAIL.
        ensure!(text.contains("statuses:") && text.contains("type: trophy"),
            "pfdtool returned no recognizable trophy verification report: {text}");
        ensure!(!text.lines().any(|line| line.trim_end().ends_with("fail")),
            "pfdtool reported an invalid PS3 PARAM.PFD hash: {text}");
    }
    Ok(())
}

#[derive(Debug)]
pub(crate) struct Ps3Working {
    stage: TempDir,
    trophy_folder: PathBuf,
    tool: PathBuf,
}

impl Ps3Working {
    pub(crate) fn open(source: &Path) -> Result<Self> {
        ensure!(source.join("PARAM.PFD").is_file(),
            "PS3 trophy folder is missing PARAM.PFD");
        ensure!(source.join("TROPTRNS.DAT").is_file(),
            "PS3 trophy folder is missing TROPTRNS.DAT");
        let tool = find_pfdtool()?;
        let stage = tempfile::tempdir().context("create PS3 working directory")?;
        let trophy_folder = stage.path().join(source.file_name()
            .unwrap_or_else(|| std::ffi::OsStr::new("Trophy")));
        fs::create_dir_all(&trophy_folder)?;
        for item in fs::read_dir(source).context("list trophy source files")? {
            let entry = item?;
            let kind = entry.path().extension().unwrap_or_default()
                .to_string_lossy().to_ascii_lowercase();
            if matches!(kind.as_str(), "dat" | "sfm" | "sfo" | "pfd") {
                fs::copy(entry.path(), trophy_folder.join(entry.file_name()))?;
            }
        }
        run_tool(&tool, &trophy_folder, "-d", Some("TROPTRNS.DAT"))
            .context("decrypt native PS3 transaction log")?;
        Ok(Self { stage, trophy_folder, tool })
    }

    pub(crate) fn decrypted_path(&self) -> &Path {
        &self.trophy_folder
    }

    /// Copy the original WinForms "profiles/*.sfo" account identifier into
    /// the working PARAM.SFO. The modified SFO is re-signed with PARAM.PFD
    /// and written alongside the DATs during the normal save operation.
    pub(crate) fn select_profile(&mut self, profile: &Path) -> Result<()> {
        let bytes = fs::read(profile)
            .with_context(|| format!("read profile {}", profile.display()))?;
        ensure!(bytes.len() >= 16, "profile SFO header is truncated");
        let index = u32::from_le_bytes(bytes[12..16].try_into()?) as usize;
        let id = bytes.get(index..index.saturating_add(16))
            .filter(|bytes| bytes.len() == 16)
            .context("SFO profile ID offset does not contain 16 bytes")?;

        let target = self.trophy_folder.join("PARAM.SFO");
        let mut sfo = fs::read(&target).context("read native PARAM.SFO")?;
        ensure!(sfo.len() >= 0x284, "native PARAM.SFO cannot hold an account ID");
        sfo[0x274..0x284].copy_from_slice(id);
        fs::write(target, sfo).context("update temporary PARAM.SFO profile")?;
        Ok(())
    }

    pub(crate) fn save(
        &self, source: &Path, original_usr: &[u8], raw_usr: &[u8], raw_trns: &[u8],
        expected: &[crate::trophy::Trophy],
    ) -> Result<PathBuf> {
        ensure!(fs::read(source.join("TROPUSR.DAT"))? == original_usr,
            "PS3 trophy files changed since opening; reopen first");
        let original_pfd = fs::read(source.join("PARAM.PFD"))?;
        let original_trns = fs::read(source.join("TROPTRNS.DAT"))?;
        let original_sfo = fs::read(source.join("PARAM.SFO"))?;

        fs::write(self.trophy_folder.join("TROPUSR.DAT"), raw_usr)?;
        fs::write(self.trophy_folder.join("TROPTRNS.DAT"), raw_trns)?;
        run_tool(&self.tool, &self.trophy_folder, "-u", None)
            .context("update PS3 PFD hashes")?;
        run_tool(&self.tool, &self.trophy_folder, "-e", Some("TROPTRNS.DAT"))
            .context("encrypt native PS3 trophy log and update PFD")?;
        for file in ["PARAM.PFD", "TROPTRNS.DAT", "TROPUSR.DAT"] {
            ensure!(self.trophy_folder.join(file).is_file(),
                "pfdtool did not produce {file}");
        }

        // Compilation alone cannot establish that the output is a valid PS3 save.
        // Have pfdtool verify the re-signed PFD, then perform a second, isolated
        // decrypt pass on the actual ciphertext. Never decrypt the files we are
        // about to install: pfdtool can update PARAM.PFD as a side effect.
        run_tool(&self.tool, &self.trophy_folder, "-c", None)
            .context("verify re-signed PS3 PARAM.PFD")?;
        let verify_stage = tempfile::tempdir().context("create PS3 verification directory")?;
        let verify_folder = verify_stage.path().join("Trophy");
        fs::create_dir(&verify_folder)?;
        for file in ["PARAM.PFD", "PARAM.SFO", "TROPTRNS.DAT", "TROPUSR.DAT"] {
            let input = self.trophy_folder.join(file);
            if input.is_file() {
                fs::copy(input, verify_folder.join(file))?;
            }
        }
        run_tool(&self.tool, &verify_folder, "-d", Some("TROPTRNS.DAT"))
            .context("verify PS3 encrypted trophy log is decryptable")?;
        ensure!(fs::read(verify_folder.join("TROPTRNS.DAT"))? == raw_trns,
            "pfdtool round-trip changed TROPTRNS.DAT; refusing PS3 write-back");
        ensure!(fs::read(verify_folder.join("TROPUSR.DAT"))? == raw_usr,
            "PS3 TROPUSR.DAT changed during encryption; refusing write-back");

        // Re-parse the *actual* verified output with the same native PS3
        // readers used when opening the application, before overwriting a
        // single source file. This detects stale IDs, sync metadata and
        // accidental timestamp/record changes that a hash check cannot.
        let sfm = fs::read(self.trophy_folder.join("TROPCONF.SFM"))?;
        let mut reopened = crate::trophy::parse_tropconf(&sfm)
            .context("parse written PS3 TROPCONF.SFM")?;
        crate::trophy::read_dat(
            &fs::read(verify_folder.join("TROPUSR.DAT"))?, &mut reopened,
        ).context("re-read written PS3 TROPUSR.DAT")?;
        let _verified_transactions = crate::trans::Trans::load(
            fs::read(verify_folder.join("TROPTRNS.DAT"))?, &mut reopened.trophies,
        ).context("re-read written PS3 TROPTRNS.DAT")?;
        ensure!(reopened.trophies.len() == expected.len(),
            "PS3 trophy count changed after re-encryption");
        for (actual, intended) in reopened.trophies.iter().zip(expected) {
            ensure!(actual.id == intended.id &&
                    actual.obtained == intended.obtained &&
                    actual.synced == intended.synced &&
                    actual.earned_us == intended.earned_us,
                "reopened PS3 trophy #{} differs from intended obtainment or sync state",
                intended.id);
        }

        let mut prepared = Vec::new();
        for file in ["PARAM.PFD", "TROPTRNS.DAT", "TROPUSR.DAT"] {
            prepared.push((file, fs::read(self.trophy_folder.join(file))?));
        }
        let staged_sfo = fs::read(self.trophy_folder.join("PARAM.SFO"))?;
        if staged_sfo != original_sfo {
            prepared.push(("PARAM.SFO", staged_sfo));
        }
        ensure!(fs::read(source.join("PARAM.PFD"))? == original_pfd
                && fs::read(source.join("TROPTRNS.DAT"))? == original_trns
                && fs::read(source.join("PARAM.SFO"))? == original_sfo,
            "PS3 signed trophy files changed during editing");

        // Back up original files before replacing any output.
        let backup = source.join(format!("ps3trophyisgood-backup-{}-{}",
            chrono::Utc::now().format("%Y%m%d-%H%M%S-%3f"),
            std::process::id()));
        fs::create_dir(&backup).context("create PS3 save backup directory")?;
        for (name, _) in &prepared {
            fs::copy(source.join(name), backup.join(name))
                .with_context(|| format!("back up {name}"))?;
        }
        // Stage all completed output bytes on the same filesystem, then rename.
        let mut staged = Vec::new();
        for (name, bytes) in &prepared {
            let mut temp = tempfile::NamedTempFile::new_in(source)
                .with_context(|| format!("stage {name}"))?;
            use std::io::Write;
            temp.write_all(bytes)?;
            temp.as_file_mut().sync_all()?;
            staged.push((*name, temp));
        }
        for (name, staged_file) in staged {
            staged_file.persist(source.join(name)).map_err(|error| anyhow::anyhow!(
                "could not save PS3 {name}; originals backed up at {}: {error}",
                backup.display()
            ))?;
        }
        Ok(backup)
    }
}


#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn profile_rewrites_only_account_id_in_staged_sfo() {
        let root = tempfile::tempdir().unwrap();
        let working = tempfile::tempdir().unwrap();
        let trophy_folder = working.path().join("native");
        fs::create_dir(&trophy_folder).unwrap();
        let mut original_sfo = vec![0xABu8; 0x300];
        original_sfo[0x274..0x284].fill(0);
        fs::write(trophy_folder.join("PARAM.SFO"), &original_sfo).unwrap();

        // Original Utility.encryptTrophy reads a little-endian 32-bit offset
        // from profile+0xC and copies 16 bytes at that offset.
        let mut profile = vec![0u8; 0x80];
        profile[12..16].copy_from_slice(&0x30u32.to_le_bytes());
        let account: [u8; 16] = *b"ABCDEFGHIJKLMNOP";
        profile[0x30..0x40].copy_from_slice(&account);
        let profile_path = root.path().join("other-account.sfo");
        fs::write(&profile_path, profile).unwrap();

        let mut session = Ps3Working {
            stage: working,
            trophy_folder: trophy_folder.clone(),
            tool: PathBuf::from("not-invoked"),
        };
        session.select_profile(&profile_path).unwrap();
        let updated = fs::read(trophy_folder.join("PARAM.SFO")).unwrap();
        assert_eq!(&updated[0x274..0x284], &account);
        let mut expected = original_sfo;
        expected[0x274..0x284].copy_from_slice(&account);
        assert_eq!(updated, expected);
    }

    #[test]
    fn invalid_profile_does_not_change_native_param_sfo() {
        let root = tempfile::tempdir().unwrap();
        let working = tempfile::tempdir().unwrap();
        let trophy_folder = working.path().join("native");
        fs::create_dir(&trophy_folder).unwrap();
        let original_sfo = vec![42u8; 0x300];
        fs::write(trophy_folder.join("PARAM.SFO"), &original_sfo).unwrap();

        let mut profile = vec![0u8; 0x20];
        profile[12..16].copy_from_slice(&0xffff_ffffu32.to_le_bytes());
        let profile_path = root.path().join("broken.sfo");
        fs::write(&profile_path, profile).unwrap();
        let mut session = Ps3Working {
            stage: working,
            trophy_folder: trophy_folder.clone(),
            tool: PathBuf::from("not-invoked"),
        };
        assert!(session.select_profile(&profile_path).is_err());
        assert_eq!(fs::read(trophy_folder.join("PARAM.SFO")).unwrap(), original_sfo);
    }
}
