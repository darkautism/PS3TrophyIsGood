using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace PS3TrophyIsGood
{
    internal sealed class FlareSolverrManager : IDisposable
    {
        private const string ApiUrl = "http://127.0.0.1:8191/v1";
        private const string LatestReleaseUrl = "https://api.github.com/repos/FlareSolverr/FlareSolverr/releases/latest";
        private const string WindowsAssetName = "flaresolverr_windows_x64.zip";

        private readonly string cacheRoot;
        private Process ownedProcess;
        private WebClient downloadClient;
        private bool disposed;

        public event Action<string> StatusChanged;
        public event Action<int> ProgressChanged;

        public string Version { get; private set; }
        public bool OwnsProcess { get { return ownedProcess != null; } }

        public FlareSolverrManager()
        {
            cacheRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PS3TrophyIsGood",
                "FlareSolverr"
            );
        }

        public async Task EnsureReadyAsync()
        {
            ThrowIfDisposed();
            SetProgress(0);
            SetStatus("Checking for an existing FlareSolverr...");

            string externalVersion = await ProbeAsync();
            if (externalVersion != null)
            {
                Version = externalVersion;
                SetProgress(100);
                SetStatus("Using already-running FlareSolverr " + externalVersion + ".");
                return;
            }

            Directory.CreateDirectory(cacheRoot);
            ReleaseInfo release = null;
            Exception releaseError = null;

            try
            {
                SetStatus("Checking the latest FlareSolverr version...");
                release = await GetLatestReleaseAsync();
            }
            catch (Exception ex)
            {
                releaseError = ex;
            }

            if (release != null)
            {
                string versionDirectory = GetVersionDirectory(release.TagName);
                string executable = FindExecutable(versionDirectory);

                if (executable == null)
                {
                    try
                    {
                        await DownloadAndInstallAsync(release, versionDirectory);
                        executable = FindExecutable(versionDirectory);
                    }
                    catch (Exception downloadError)
                    {
                        string fallback = FindNewestCachedExecutable(null);
                        if (fallback == null)
                            throw new InvalidOperationException("Unable to download FlareSolverr and no cached version is available.", downloadError);

                        SetStatus("Update failed. Starting the last cached FlareSolverr...");
                        await StartOwnedProcessAsync(fallback, GetCachedVersionName(fallback));
                        CleanupCache(GetCachedVersionName(fallback));
                        return;
                    }
                }

                try
                {
                    await StartOwnedProcessAsync(executable, release.TagName);
                    CleanupCache(release.TagName);
                    return;
                }
                catch (Exception startError)
                {
                    StopOwnedProcess();
                    string fallback = FindNewestCachedExecutable(release.TagName);
                    if (fallback == null)
                        throw new InvalidOperationException("The latest FlareSolverr was installed but did not become ready.", startError);

                    SetStatus("Latest version did not start. Rolling back to the previous cached version...");
                    await StartOwnedProcessAsync(fallback, GetCachedVersionName(fallback));
                    CleanupCache(GetCachedVersionName(fallback));
                    return;
                }
            }

            string cachedExecutable = FindNewestCachedExecutable(null);
            if (cachedExecutable == null)
                throw new InvalidOperationException("Could not check the latest FlareSolverr version and no cached version is available.", releaseError);

            SetStatus("Version check failed. Starting cached FlareSolverr...");
            await StartOwnedProcessAsync(cachedExecutable, GetCachedVersionName(cachedExecutable));
            CleanupCache(GetCachedVersionName(cachedExecutable));
        }

        private async Task<ReleaseInfo> GetLatestReleaseAsync()
        {
            using (WebClient client = new WebClient())
            {
                client.Headers.Add(HttpRequestHeader.UserAgent, "PS3TrophyIsGood");
                string jsonText = await client.DownloadStringTaskAsync(LatestReleaseUrl);
                using (JsonDocument json = JsonDocument.Parse(jsonText))
                {
                    JsonElement root = json.RootElement;
                    string tag = root.GetProperty("tag_name").GetString();
                    foreach (JsonElement asset in root.GetProperty("assets").EnumerateArray())
                    {
                        if (!string.Equals(asset.GetProperty("name").GetString(), WindowsAssetName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        string digest = null;
                        JsonElement digestElement;
                        if (asset.TryGetProperty("digest", out digestElement) && digestElement.ValueKind == JsonValueKind.String)
                            digest = digestElement.GetString();

                        return new ReleaseInfo(
                            tag,
                            asset.GetProperty("browser_download_url").GetString(),
                            digest
                        );
                    }
                }
            }

            throw new InvalidOperationException("The latest FlareSolverr release does not contain " + WindowsAssetName + ".");
        }

        private async Task DownloadAndInstallAsync(ReleaseInfo release, string versionDirectory)
        {
            string archivePath = Path.Combine(cacheRoot, "flaresolverr-download.zip");
            string stagingDirectory = versionDirectory + ".staging";

            SafeDeleteFile(archivePath);
            SafeDeleteDirectory(stagingDirectory);

            SetStatus("Downloading FlareSolverr " + release.TagName + "...");
            SetProgress(0);

            downloadClient = new WebClient();
            downloadClient.Headers.Add(HttpRequestHeader.UserAgent, "PS3TrophyIsGood");
            downloadClient.DownloadProgressChanged += delegate(object sender, DownloadProgressChangedEventArgs e)
            {
                SetProgress(e.ProgressPercentage);
            };

            try
            {
                await downloadClient.DownloadFileTaskAsync(new Uri(release.DownloadUrl), archivePath);
            }
            finally
            {
                downloadClient.Dispose();
                downloadClient = null;
            }

            SetStatus("Verifying FlareSolverr download...");
            VerifyDigest(archivePath, release.Digest);

            SetStatus("Installing FlareSolverr " + release.TagName + "...");
            Directory.CreateDirectory(stagingDirectory);
            ZipFile.ExtractToDirectory(archivePath, stagingDirectory);

            if (FindExecutable(stagingDirectory) == null)
            {
                SafeDeleteDirectory(stagingDirectory);
                throw new InvalidOperationException("FlareSolverr archive layout changed: flaresolverr.exe was not found.");
            }

            SafeDeleteDirectory(versionDirectory);
            Directory.Move(stagingDirectory, versionDirectory);
            SafeDeleteFile(archivePath);
            SetProgress(100);
        }

        private static void VerifyDigest(string archivePath, string digest)
        {
            if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                return;

            string expected = digest.Substring("sha256:".Length).Trim().ToLowerInvariant();
            string actual;
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(archivePath))
            {
                actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }

            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("FlareSolverr SHA-256 checksum mismatch.");
        }

        private async Task StartOwnedProcessAsync(string executable, string version)
        {
            StopOwnedProcess();
            SetStatus("Starting FlareSolverr " + version + "...");
            SetProgress(0);

            Process process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Path.GetDirectoryName(executable),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
            {
                if (e.Data != null)
                    Debug.WriteLine("FlareSolverr: " + e.Data);
            };
            process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
            {
                if (e.Data != null)
                    Debug.WriteLine("FlareSolverr error: " + e.Data);
            };

            if (!process.Start())
                throw new InvalidOperationException("FlareSolverr process could not be started.");

            ownedProcess = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            SetStatus("Waiting for FlareSolverr health check...");
            DateTime deadline = DateTime.UtcNow.AddSeconds(35);
            while (DateTime.UtcNow < deadline)
            {
                if (ownedProcess.HasExited)
                    throw new InvalidOperationException("FlareSolverr exited before it became ready.");

                string detectedVersion = await ProbeAsync();
                if (detectedVersion != null)
                {
                    Version = string.IsNullOrWhiteSpace(detectedVersion) ? version : detectedVersion;
                    SetProgress(100);
                    SetStatus("FlareSolverr " + Version + " is ready.");
                    return;
                }

                await Task.Delay(500);
            }

            throw new TimeoutException("FlareSolverr did not become ready within 35 seconds.");
        }

        private async Task<string> ProbeAsync()
        {
            try
            {
                using (WebClient client = new WebClient())
                {
                    client.Headers.Add(HttpRequestHeader.ContentType, "application/json");
                    string response = await client.UploadStringTaskAsync(
                        new Uri(ApiUrl),
                        "POST",
                        "{\"cmd\":\"sessions.list\"}"
                    );
                    using (JsonDocument json = JsonDocument.Parse(response))
                    {
                        JsonElement root = json.RootElement;
                        JsonElement status;
                        if (root.TryGetProperty("status", out status) &&
                            string.Equals(status.GetString(), "ok", StringComparison.OrdinalIgnoreCase))
                        {
                            JsonElement version;
                            if (root.TryGetProperty("version", out version) && version.ValueKind == JsonValueKind.String)
                                return version.GetString();
                            return "(external)";
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private string GetVersionDirectory(string version)
        {
            return Path.Combine(cacheRoot, SanitizeFileName(version));
        }

        private static string FindExecutable(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return null;

            string direct = Path.Combine(directory, "flaresolverr.exe");
            if (File.Exists(direct))
                return direct;

            string nested = Path.Combine(directory, "flaresolverr", "flaresolverr.exe");
            if (File.Exists(nested))
                return nested;

            return Directory.GetFiles(directory, "flaresolverr.exe", SearchOption.AllDirectories).FirstOrDefault();
        }

        private string FindNewestCachedExecutable(string excludedVersion)
        {
            if (!Directory.Exists(cacheRoot))
                return null;

            return Directory.GetDirectories(cacheRoot)
                .Where(d => !d.EndsWith(".staging", StringComparison.OrdinalIgnoreCase))
                .Where(d => excludedVersion == null || !string.Equals(Path.GetFileName(d), SanitizeFileName(excludedVersion), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .Select(FindExecutable)
                .FirstOrDefault(p => p != null);
        }

        private string GetCachedVersionName(string executable)
        {
            DirectoryInfo current = new FileInfo(executable).Directory;
            while (current != null && !string.Equals(current.Parent == null ? null : current.Parent.FullName, cacheRoot, StringComparison.OrdinalIgnoreCase))
                current = current.Parent;

            return current == null ? "cached" : current.Name;
        }

        private void CleanupCache(string activeVersion)
        {
            try
            {
                if (!Directory.Exists(cacheRoot))
                    return;

                List<DirectoryInfo> versions = new DirectoryInfo(cacheRoot)
                    .GetDirectories()
                    .Where(d => !d.Name.EndsWith(".staging", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(d => d.LastWriteTimeUtc)
                    .ToList();

                HashSet<string> keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                keep.Add(SanitizeFileName(activeVersion));
                foreach (DirectoryInfo directory in versions)
                {
                    if (keep.Count >= 2)
                        break;
                    if (FindExecutable(directory.FullName) != null)
                        keep.Add(directory.Name);
                }

                foreach (DirectoryInfo directory in versions)
                {
                    if (!keep.Contains(directory.Name))
                        SafeDeleteDirectory(directory.FullName);
                }
            }
            catch
            {
            }
        }

        private static string SanitizeFileName(string value)
        {
            string result = value ?? "unknown";
            foreach (char invalid in Path.GetInvalidFileNameChars())
                result = result.Replace(invalid, '_');
            return result;
        }

        private void SetStatus(string status)
        {
            Action<string> handler = StatusChanged;
            if (handler != null)
                handler(status);
        }

        private void SetProgress(int progress)
        {
            Action<int> handler = ProgressChanged;
            if (handler != null)
                handler(Math.Max(0, Math.Min(100, progress)));
        }

        private void StopOwnedProcess()
        {
            if (ownedProcess == null)
                return;

            try
            {
                if (!ownedProcess.HasExited)
                {
                    ownedProcess.Kill();
                    ownedProcess.WaitForExit(2000);
                }
            }
            catch
            {
            }
            finally
            {
                ownedProcess.Dispose();
                ownedProcess = null;
            }
        }

        private static void SafeDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private static void SafeDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch
            {
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException("FlareSolverrManager");
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            if (downloadClient != null)
            {
                try { downloadClient.CancelAsync(); } catch { }
            }
            StopOwnedProcess();
        }

        private sealed class ReleaseInfo
        {
            public string TagName { get; private set; }
            public string DownloadUrl { get; private set; }
            public string Digest { get; private set; }

            public ReleaseInfo(string tagName, string downloadUrl, string digest)
            {
                TagName = tagName;
                DownloadUrl = downloadUrl;
                Digest = digest;
            }
        }
    }
}
