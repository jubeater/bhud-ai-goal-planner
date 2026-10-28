using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using Newtonsoft.Json.Linq;

namespace jubeater.aigoalplanner.World
{
    public interface IMarkerPackService
    {
        Task<MarkerPacksResult> GetPacksAsync(bool pathingInstalled, CancellationToken cancellationToken = default);
        Task<MarkerPackChange> DownloadPackAsync(string packNameOrFileName, CancellationToken cancellationToken = default);
        Task<MarkerPackChange> DeletePackAsync(string fileName, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Discovers official Marker Packs from Pathing's public repo (https://mp-repo.blishhud.com/repo.json)
    /// and local packs in Blish HUD's shared markers directory, handles downloads to temp files with zip
    /// validation, and uninstalls packs on Undo.
    /// </summary>
    public sealed class MarkerPackService : IMarkerPackService
    {
        public const string PublicRepoUrl = "https://mp-repo.blishhud.com/repo.json";

        private readonly string                                         _markersDirectory;
        private readonly string                                         _cacheDirectory;
        private readonly Action<string>                                 _logWarn;
        private readonly Func<string, CancellationToken, Task<string>> _httpGet;
        private readonly Func<string, string, CancellationToken, Task>  _downloadFileAsync;

        private readonly object                                         _lock = new object();
        private List<MarkerPackInfo>                                    _cachedOfficialPacks;
        private DateTime                                                _lastCacheTime = DateTime.MinValue;
        private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(10);

        public MarkerPackService(
            string markersDirectory,
            string cacheDirectory,
            Action<string> logWarn = null,
            Func<string, CancellationToken, Task<string>> httpGetAsync = null,
            Func<string, string, CancellationToken, Task> downloadFileAsync = null,
            Func<string, Task<string>> httpGet = null) {

            _markersDirectory  = markersDirectory;
            _cacheDirectory    = cacheDirectory;
            _logWarn           = logWarn;
            if (httpGetAsync != null) {
                _httpGet = httpGetAsync;
            } else if (httpGet != null) {
                _httpGet = (url, ct) => httpGet(url);
            } else {
                _httpGet = DefaultHttpGetAsync;
            }
            _downloadFileAsync = downloadFileAsync ?? DefaultDownloadFileAsync;
        }

        public async Task<MarkerPacksResult> GetPacksAsync(bool pathingInstalled, CancellationToken cancellationToken = default) {
            List<MarkerPackInfo> officialPacks = await LoadOfficialPacksAsync(cancellationToken).ConfigureAwait(false);

            var installedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var installedDirectoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(_markersDirectory) && Directory.Exists(_markersDirectory)) {
                try {
                    string[] files = Directory.GetFiles(_markersDirectory, "*.*", SearchOption.TopDirectoryOnly);
                    foreach (string file in files) {
                        string ext = Path.GetExtension(file);
                        if (string.Equals(ext, ".taco", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase)) {
                            installedFileNames.Add(Path.GetFileName(file));
                        }
                    }

                    string[] dirs = Directory.GetDirectories(_markersDirectory, "*", SearchOption.TopDirectoryOnly);
                    foreach (string dir in dirs) {
                        string dirName = Path.GetFileName(dir);
                        if (!string.Equals(dirName, "data", StringComparison.OrdinalIgnoreCase)) {
                            installedDirectoryNames.Add(dirName);
                        }
                    }
                } catch (Exception ex) {
                    _logWarn?.Invoke("Failed to read markers directory: " + ex.GetType().Name);
                }
            }

            var merged = new List<MarkerPackInfo>();

            if (officialPacks != null) {
                foreach (MarkerPackInfo official in officialPacks) {
                    bool isInstalled = installedFileNames.Contains(official.FileName) ||
                                       installedDirectoryNames.Contains(Path.GetFileNameWithoutExtension(official.FileName));

                    merged.Add(new MarkerPackInfo(
                        official.Name,
                        official.FileName,
                        official.Description,
                        official.Categories,
                        official.SizeMb,
                        official.DownloadUrl,
                        official.Version,
                        installed: isInstalled,
                        official.MapIds));

                    installedFileNames.Remove(official.FileName);
                    installedDirectoryNames.Remove(Path.GetFileNameWithoutExtension(official.FileName));
                }
            }

            // Any remaining installed files/folders not in the official repo are local packs
            foreach (string file in installedFileNames) {
                string packName = Path.GetFileNameWithoutExtension(file);
                merged.Add(new MarkerPackInfo(
                    packName,
                    file,
                    description: "Local custom marker pack.",
                    categories: string.Empty,
                    sizeMb: 0,
                    downloadUrl: string.Empty,
                    version: string.Empty,
                    installed: true));
            }

            foreach (string dir in installedDirectoryNames) {
                merged.Add(new MarkerPackInfo(
                    dir,
                    dir,
                    description: "Local unpacked marker pack directory.",
                    categories: string.Empty,
                    sizeMb: 0,
                    downloadUrl: string.Empty,
                    version: string.Empty,
                    installed: true));
            }

            if (officialPacks == null && merged.Count == 0) {
                return new MarkerPacksResult(MarkerPacksStatus.Unavailable, Array.Empty<MarkerPackInfo>(), pathingInstalled, "Unable to load Pathing marker pack list.");
            }

            return new MarkerPacksResult(MarkerPacksStatus.Ok, merged, pathingInstalled);
        }

        public async Task<MarkerPackChange> DownloadPackAsync(string packNameOrFileName, CancellationToken cancellationToken = default) {
            string wanted = (packNameOrFileName ?? string.Empty).Trim();
            if (wanted.Length == 0) {
                return new MarkerPackChange(false, false, wanted, null, 0, "The action does not say which marker pack to download.");
            }

            List<MarkerPackInfo> officialPacks = await LoadOfficialPacksAsync(cancellationToken).ConfigureAwait(false);
            if (officialPacks == null || officialPacks.Count == 0) {
                return new MarkerPackChange(false, false, wanted, null, 0, "Could not load Pathing's official marker pack repository.");
            }

            MarkerPackInfo targetPack = officialPacks.FirstOrDefault(p =>
                string.Equals(p.Name, wanted, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.FileName, wanted, StringComparison.OrdinalIgnoreCase));

            if (targetPack == null) {
                return new MarkerPackChange(false, false, wanted, null, 0, "\"" + wanted + "\" is not on Pathing's official marker pack list.");
            }

            if (!IsValidFileName(targetPack.FileName)) {
                return new MarkerPackChange(false, false, targetPack.Name, targetPack.FileName, targetPack.SizeMb,
                    "The marker pack file name \"" + targetPack.FileName + "\" is not valid.");
            }

            if (string.IsNullOrWhiteSpace(_markersDirectory)) {
                return new MarkerPackChange(false, false, targetPack.Name, targetPack.FileName, targetPack.SizeMb, "Markers directory is not configured.");
            }

            string destFile = Path.Combine(_markersDirectory, targetPack.FileName);
            if (!IsInsideMarkersDirectory(destFile, _markersDirectory)) {
                return new MarkerPackChange(false, false, targetPack.Name, targetPack.FileName, targetPack.SizeMb,
                    "The marker pack destination path is outside the markers directory.");
            }
            if (File.Exists(destFile)) {
                return new MarkerPackChange(applied: true, changed: false, targetPack.Name, targetPack.FileName, targetPack.SizeMb);
            }

            string tempDir = _cacheDirectory ?? Path.GetTempPath();
            try {
                if (!Directory.Exists(tempDir)) {
                    Directory.CreateDirectory(tempDir);
                }
            } catch (Exception ex) {
                _logWarn?.Invoke("Failed to create temp directory: " + ex.GetType().Name);
            }

            string tempFile = Path.Combine(tempDir, "pack_" + Guid.NewGuid().ToString("N") + ".tmp");

            try {
                await _downloadFileAsync(targetPack.DownloadUrl, tempFile, cancellationToken).ConfigureAwait(false);

                // Verify zip integrity: must be valid zip with at least one entry
                bool validZip = false;
                bool wasEmpty = false;
                try {
                    using (var archive = ZipFile.OpenRead(tempFile)) {
                        if (archive.Entries.Count > 0) {
                            validZip = true;
                        } else {
                            wasEmpty = true;
                        }
                    }
                } catch {
                    validZip = false;
                }

                if (!validZip) {
                    TryDeleteFile(tempFile);
                    string problem = wasEmpty
                        ? "The downloaded marker pack is not valid: archive is empty."
                        : "The downloaded file is not a valid zip archive.";

                    return new MarkerPackChange(false, false, targetPack.Name, targetPack.FileName, targetPack.SizeMb, problem);
                }

                if (!Directory.Exists(_markersDirectory)) {
                    Directory.CreateDirectory(_markersDirectory);
                }

                if (File.Exists(destFile)) {
                    TryDeleteFile(tempFile);
                    return new MarkerPackChange(applied: true, changed: false, targetPack.Name, targetPack.FileName, targetPack.SizeMb);
                }

                try {
                    if (!IsValidFileName(targetPack.FileName) || !IsInsideMarkersDirectory(destFile, _markersDirectory)) {
                        TryDeleteFile(tempFile);
                        return new MarkerPackChange(false, false, targetPack.Name, targetPack.FileName, targetPack.SizeMb,
                            "The marker pack destination path is outside the markers directory.");
                    }

                    File.Move(tempFile, destFile);
                } catch (IOException) when (File.Exists(destFile)) {
                    TryDeleteFile(tempFile);
                    return new MarkerPackChange(applied: true, changed: false, targetPack.Name, targetPack.FileName, targetPack.SizeMb);
                }

                return new MarkerPackChange(applied: true, changed: true, targetPack.Name, targetPack.FileName, targetPack.SizeMb);
            } catch (OperationCanceledException) {
                TryDeleteFile(tempFile);
                throw;
            } catch (Exception ex) {
                TryDeleteFile(tempFile);
                _logWarn?.Invoke("DownloadPack failed: " + ex.GetType().Name);
                return new MarkerPackChange(false, false, targetPack.Name, targetPack.FileName, targetPack.SizeMb, "Failed to download pack (" + ex.GetType().Name + ").");
            }
        }

        public Task<MarkerPackChange> DeletePackAsync(string fileName, CancellationToken cancellationToken = default) {
            string file = fileName ?? string.Empty;
            if (file.Length == 0) {
                return Task.FromResult(new MarkerPackChange(false, false, null, null, 0, "No file specified to delete."));
            }

            if (!IsValidFileName(file)) {
                return Task.FromResult(new MarkerPackChange(false, false, file, file, 0,
                    "The marker pack file name \"" + file + "\" is not valid."));
            }

            if (string.IsNullOrWhiteSpace(_markersDirectory) || !Directory.Exists(_markersDirectory)) {
                return Task.FromResult(new MarkerPackChange(true, false, file, file, 0));
            }

            string fullPath = Path.Combine(_markersDirectory, file);
            if (!IsInsideMarkersDirectory(fullPath, _markersDirectory)) {
                return Task.FromResult(new MarkerPackChange(false, false, file, file, 0,
                    "The target path is outside the markers directory."));
            }
            if (!File.Exists(fullPath)) {
                return Task.FromResult(new MarkerPackChange(true, false, file, file, 0));
            }

            try {
                File.Delete(fullPath);
                return Task.FromResult(new MarkerPackChange(true, true, file, file, 0));
            } catch (Exception ex) {
                _logWarn?.Invoke("DeletePack failed: " + ex.GetType().Name);
                return Task.FromResult(new MarkerPackChange(false, false, file, file, 0, "Could not delete pack file (" + ex.GetType().Name + ")."));
            }
        }

        private async Task<List<MarkerPackInfo>> LoadOfficialPacksAsync(CancellationToken cancellationToken) {
            lock (_lock) {
                if (_cachedOfficialPacks != null && DateTime.UtcNow - _lastCacheTime < CacheExpiry) {
                    return _cachedOfficialPacks;
                }
            }

            string json = null;
            try {
                json = await _httpGet(PublicRepoUrl, cancellationToken).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                _logWarn?.Invoke("Failed to fetch official marker pack repository: " + ex.GetType().Name);
            }

            // Fallback to disk cache if available
            string cacheFile = !string.IsNullOrWhiteSpace(_cacheDirectory)
                ? Path.Combine(_cacheDirectory, "repo.json")
                : null;

            if (string.IsNullOrWhiteSpace(json) && cacheFile != null && File.Exists(cacheFile)) {
                try {
                    json = File.ReadAllText(cacheFile);
                } catch (Exception ex) {
                    _logWarn?.Invoke("Failed to read cached repo.json: " + ex.GetType().Name);
                }
            }

            if (string.IsNullOrWhiteSpace(json)) {
                lock (_lock) {
                    return _cachedOfficialPacks;
                }
            }

            List<MarkerPackInfo> parsed = ParseRepoJson(json);
            if (parsed.Count > 0) {
                lock (_lock) {
                    _cachedOfficialPacks = parsed;
                    _lastCacheTime       = DateTime.UtcNow;
                }

                // Save to cache file
                if (cacheFile != null) {
                    try {
                        string dir = Path.GetDirectoryName(cacheFile);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) {
                            Directory.CreateDirectory(dir);
                        }
                        File.WriteAllText(cacheFile, json);
                    } catch { }
                }
            }

            return parsed;
        }

        private static List<MarkerPackInfo> ParseRepoJson(string json) {
            var list = new List<MarkerPackInfo>();
            if (string.IsNullOrWhiteSpace(json)) return list;

            try {
                JArray array = JArray.Parse(json);
                foreach (JToken item in array) {
                    if (!(item is JObject obj)) continue;

                    string name        = (string)obj["Name"] ?? string.Empty;
                    string fileName    = (string)obj["FileName"] ?? string.Empty;
                    string description = (string)obj["Description"] ?? string.Empty;
                    string categories  = (string)obj["Categories"] ?? string.Empty;
                    string download    = (string)obj["Download"] ?? string.Empty;
                    string version     = (string)obj["Version"] ?? string.Empty;

                    double sizeMb = 0;
                    if (obj["Size"] != null) {
                        try {
                            sizeMb = Convert.ToDouble(obj["Size"], CultureInfo.InvariantCulture);
                        } catch { }
                    }

                    var mapIds = new List<int>();
                    if (obj["MapIds"] is JArray mapArray) {
                        foreach (JToken m in mapArray) {
                            try {
                                mapIds.Add(Convert.ToInt32(m, CultureInfo.InvariantCulture));
                            } catch { }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(fileName)) {
                        list.Add(new MarkerPackInfo(name, fileName, description, categories, sizeMb, download, version, installed: false, mapIds));
                    }
                }
            } catch { }

            return list;
        }

        private static async Task<string> DefaultHttpGetAsync(string url, CancellationToken cancellationToken) {
            using (var client = new HttpClient()) {
                client.Timeout = TimeSpan.FromSeconds(15);
                using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false)) {
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
        }

        private static async Task DefaultDownloadFileAsync(string url, string destPath, CancellationToken cancellationToken) {
            using (var client = new HttpClient()) {
                client.Timeout = TimeSpan.FromMinutes(5);
                using (HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false)) {
                    response.EnsureSuccessStatusCode();
                    using (Stream contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true)) {
                        await contentStream.CopyToAsync(fileStream, 8192, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        public static bool IsValidFileName(string name) {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (char.IsWhiteSpace(name[0]) || char.IsWhiteSpace(name[name.Length - 1])) return false;
            if (name == "." || name == "..") return false;
            if (name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0) return false;
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
            if (!string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal)) return false;

            string ext = Path.GetExtension(name);
            if (!string.Equals(ext, ".taco", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            string stem = Path.GetFileNameWithoutExtension(name);
            if (string.IsNullOrWhiteSpace(stem) || stem == "." || stem == "..") return false;

            return true;
        }

        public static bool IsInsideMarkersDirectory(string path, string markersDirectory) {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(markersDirectory)) return false;

            try {
                string fullDir = Path.GetFullPath(markersDirectory);
                if (!fullDir.EndsWith(Path.DirectorySeparatorChar.ToString()) &&
                    !fullDir.EndsWith(Path.AltDirectorySeparatorChar.ToString())) {
                    fullDir += Path.DirectorySeparatorChar;
                }

                string fullPath = Path.GetFullPath(path);
                return fullPath.StartsWith(fullDir, StringComparison.OrdinalIgnoreCase);
            } catch {
                return false;
            }
        }

        private static void TryDeleteFile(string path) {
            if (string.IsNullOrEmpty(path)) return;
            try {
                if (File.Exists(path)) File.Delete(path);
            } catch { }
        }
    }
}
