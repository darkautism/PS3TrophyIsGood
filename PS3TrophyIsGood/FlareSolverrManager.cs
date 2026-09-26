using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
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
        private IntPtr ownedProcessJob = IntPtr.Zero;
        private WebClient releaseClient;
        private WebClient downloadClient;
        private WebClient requestClient;
        private bool disposed;

        public event Action<string> StatusChanged;
        public event Action<int> ProgressChanged;

        public string Version { get; private set; }
        public bool OwnsProcess { get { return ownedProcess != null; } }
        public bool IsOwnedProcessRunning
        {
            get
            {
                if (ownedProcess == null)
                    return false;

                try
                {
                    return !ownedProcess.HasExited;
                }
                catch
                {
                    return false;
                }
            }
        }

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
            ThrowIfDisposed();
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

            ThrowIfDisposed();
            if (release != null)
            {
                string versionDirectory = GetVersionDirectory(release.TagName);
                string executable = FindExecutable(versionDirectory);

                if (executable == null)
                {
                    try
                    {
                        await DownloadAndInstallAsync(release, versionDirectory);
                        ThrowIfDisposed();
                        executable = FindExecutable(versionDirectory);
                    }
                    catch (Exception downloadError)
                    {
                        ThrowIfDisposed();
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
                    ThrowIfDisposed();
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

        public async Task<bool> IsReadyAsync()
        {
            ThrowIfDisposed();
            return await ProbeAsync() != null;
        }

        public async Task<PageResult> RequestPageAsync(string targetUrl)
        {
            ThrowIfDisposed();
            string payload = JsonSerializer.Serialize(new
            {
                cmd = "request.get",
                url = targetUrl,
                maxTimeout = 60000
            });

            string response = await PostJsonAsync(payload);
            ThrowIfDisposed();

            using (JsonDocument json = JsonDocument.Parse(response))
            {
                JsonElement root = json.RootElement;
                EnsureOkResponse(root, "FlareSolverr request failed");

                JsonElement solution;
                JsonElement htmlElement;
                if (!root.TryGetProperty("solution", out solution) ||
                    !solution.TryGetProperty("response", out htmlElement) ||
                    htmlElement.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidOperationException("FlareSolverr returned no page HTML.");
                }

                int httpStatus = 0;
                JsonElement statusCodeElement;
                if (solution.TryGetProperty("status", out statusCodeElement) && statusCodeElement.ValueKind == JsonValueKind.Number)
                    statusCodeElement.TryGetInt32(out httpStatus);

                return new PageResult(htmlElement.GetString(), httpStatus);
            }
        }

        private async Task<string> PostJsonAsync(string payload)
        {
            ThrowIfDisposed();
            requestClient = new WebClient();
            requestClient.Headers.Add(HttpRequestHeader.ContentType, "application/json");
            try
            {
                return await requestClient.UploadStringTaskAsync(
                    new Uri(ApiUrl),
                    "POST",
                    payload
                );
            }
            finally
            {
                requestClient.Dispose();
                requestClient = null;
            }
        }

        private static void EnsureOkResponse(JsonElement root, string prefix)
        {
            JsonElement statusElement;
            if (root.TryGetProperty("status", out statusElement) &&
                statusElement.ValueKind == JsonValueKind.String &&
                string.Equals(statusElement.GetString(), "ok", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            JsonElement messageElement;
            string message = root.TryGetProperty("message", out messageElement) && messageElement.ValueKind == JsonValueKind.String
                ? messageElement.GetString()
                : "unknown FlareSolverr error";
            throw new InvalidOperationException(prefix + ": " + message);
        }

        private async Task<ReleaseInfo> GetLatestReleaseAsync()
        {
            releaseClient = new WebClient();
            releaseClient.Headers.Add(HttpRequestHeader.UserAgent, "PS3TrophyIsGood");
            try
            {
                string jsonText = await releaseClient.DownloadStringTaskAsync(LatestReleaseUrl);
                ThrowIfDisposed();
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
            finally
            {
                releaseClient.Dispose();
                releaseClient = null;
            }

            throw new InvalidOperationException("The latest FlareSolverr release does not contain " + WindowsAssetName + ".");
        }

        private async Task DownloadAndInstallAsync(ReleaseInfo release, string versionDirectory)
        {
            ThrowIfDisposed();
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
                ThrowIfDisposed();
            }
            finally
            {
                downloadClient.Dispose();
                downloadClient = null;
            }

            SetStatus("Verifying FlareSolverr download...");
            VerifyDigest(archivePath, release.Digest);
            ThrowIfDisposed();

            SetStatus("Installing FlareSolverr " + release.TagName + "...");
            Directory.CreateDirectory(stagingDirectory);
            ZipFile.ExtractToDirectory(archivePath, stagingDirectory);
            ThrowIfDisposed();

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
            ThrowIfDisposed();
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

            try
            {
                if (!process.Start())
                    throw new InvalidOperationException("FlareSolverr process could not be started.");

                ownedProcess = process;
                ownedProcessJob = CreateKillOnCloseJob(process);
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                SetStatus("Waiting for FlareSolverr health check...");
                DateTime deadline = DateTime.UtcNow.AddSeconds(35);
                while (DateTime.UtcNow < deadline)
                {
                    ThrowIfDisposed();
                    if (ownedProcess == null || ownedProcess.HasExited)
                        throw new InvalidOperationException("FlareSolverr exited before it became ready.");

                    string detectedVersion = await ProbeAsync();
                    ThrowIfDisposed();
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
            catch
            {
                if (ownedProcess == null)
                    process.Dispose();
                StopOwnedProcess();
                throw;
            }
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
            {
                CloseOwnedProcessJob();
                return;
            }

            Process process = ownedProcess;
            ownedProcess = null;

            try
            {
                if (ownedProcessJob != IntPtr.Zero)
                {
                    CloseOwnedProcessJob();
                    try { process.WaitForExit(5000); } catch { }
                }
                else if (!process.HasExited)
                {
                    KillProcessTree(process);
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        private static IntPtr CreateKillOnCloseJob(Process process)
        {
            IntPtr job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero)
                return IntPtr.Zero;

            int length = Marshal.SizeOf(typeof(JobObjectExtendedLimitInformation));
            IntPtr infoPointer = Marshal.AllocHGlobal(length);
            try
            {
                JobObjectExtendedLimitInformation info = new JobObjectExtendedLimitInformation();
                info.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
                Marshal.StructureToPtr(info, infoPointer, false);

                if (!SetInformationJobObject(
                        job,
                        JobObjectInfoType.ExtendedLimitInformation,
                        infoPointer,
                        (uint)length) ||
                    !AssignProcessToJobObject(job, process.Handle))
                {
                    CloseHandle(job);
                    return IntPtr.Zero;
                }

                return job;
            }
            catch
            {
                CloseHandle(job);
                return IntPtr.Zero;
            }
            finally
            {
                Marshal.FreeHGlobal(infoPointer);
            }
        }

        private void CloseOwnedProcessJob()
        {
            if (ownedProcessJob == IntPtr.Zero)
                return;

            try
            {
                CloseHandle(ownedProcessJob);
            }
            catch
            {
            }
            finally
            {
                ownedProcessJob = IntPtr.Zero;
            }
        }

        private static void KillProcessTree(Process process)
        {
            if (process == null)
                return;

            try
            {
                int processId = process.Id;
                if (!process.HasExited)
                {
                    ProcessStartInfo taskKillStartInfo = new ProcessStartInfo
                    {
                        FileName = Path.Combine(Environment.SystemDirectory, "taskkill.exe"),
                        Arguments = "/PID " + processId + " /T /F",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using (Process taskKill = Process.Start(taskKillStartInfo))
                    {
                        if (taskKill != null)
                            taskKill.WaitForExit(5000);
                    }
                }
            }
            catch
            {
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(2000);
                }
            }
            catch
            {
            }
        }

        private const uint JobObjectLimitKillOnJobClose = 0x00002000;

        private enum JobObjectInfoType
        {
            ExtendedLimitInformation = 9
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectBasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectExtendedLimitInformation
        {
            public JobObjectBasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(
            IntPtr hJob,
            JobObjectInfoType jobObjectInfoType,
            IntPtr lpJobObjectInfo,
            uint cbJobObjectInfoLength
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

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
            if (releaseClient != null)
            {
                try { releaseClient.CancelAsync(); } catch { }
            }
            if (downloadClient != null)
            {
                try { downloadClient.CancelAsync(); } catch { }
            }
            if (requestClient != null)
            {
                try { requestClient.CancelAsync(); } catch { }
            }
            StopOwnedProcess();
        }

        internal sealed class PageResult
        {
            public string Html { get; private set; }
            public int HttpStatus { get; private set; }

            public PageResult(string html, int httpStatus)
            {
                Html = html;
                HttpStatus = httpStatus;
            }
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
