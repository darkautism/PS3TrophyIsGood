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

    pub(crate) fn save(
        &self, source: &Path, original_usr: &[u8], raw_usr: &[u8], raw_trns: &[u8],
    ) -> Result<PathBuf> {
        ensure!(fs::read(source.join("TROPUSR.DAT"))? == original_usr,
            "PS3 trophy files changed since opening; reopen first");
        let original_pfd = fs::read(source.join("PARAM.PFD"))?;
        let original_trns = fs::read(source.join("TROPTRNS.DAT"))?;

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

        let mut prepared = Vec::new();
        for file in ["PARAM.PFD", "TROPTRNS.DAT", "TROPUSR.DAT"] {
            prepared.push((file, fs::read(self.trophy_folder.join(file))?));
        }
        ensure!(fs::read(source.join("PARAM.PFD"))? == original_pfd
                && fs::read(source.join("TROPTRNS.DAT"))? == original_trns,
            "PS3 signed trophy files changed during editing");

        // Back up original files before replacing any output.
        let backup = source.join(format!("ps3trophyisgood-backup-{}",
            chrono::Utc::now().format("%Y%m%d-%H%M%S")));
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
