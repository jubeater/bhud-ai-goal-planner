using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using Newtonsoft.Json.Linq;

namespace jubeater.aigoalplanner.World
{
    public interface ICatalogDetailsService
    {
        Task<CatalogModuleDetails> FetchCatalogModuleDetailsAsync(CatalogPackageEntry entry, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Downloads candidate module packages (.bhm) to isolated temp files off the game thread,
    /// verifies their SHA-256 hash, extracts manifest.json for description and requested API
    /// permissions, cleans up temp files on all paths, and caches results by namespace@version.
    /// </summary>
    public sealed class CatalogDetailsService : ICatalogDetailsService
    {
        private readonly ConcurrentDictionary<string, CatalogModuleDetails> _detailsCache =
            new ConcurrentDictionary<string, CatalogModuleDetails>(StringComparer.OrdinalIgnoreCase);

        private readonly Func<string, string, CancellationToken, Task> _downloadFileAsync;
        private readonly string                                         _tempDirectory;
        private readonly Action<string>                                 _logWarn;

        public CatalogDetailsService(
            Func<string, string, CancellationToken, Task> downloadFileAsync = null,
            string tempDirectory = null,
            Action<string> logWarn = null) {

            _downloadFileAsync = downloadFileAsync ?? DefaultDownloadPackageAsync;
            _tempDirectory     = tempDirectory;
            _logWarn           = logWarn;
        }

        public async Task<CatalogModuleDetails> FetchCatalogModuleDetailsAsync(CatalogPackageEntry entry, CancellationToken cancellationToken) {
            if (entry == null) {
                return new CatalogModuleDetails(false, string.Empty, problem: "No catalog entry provided.");
            }

            string cacheKey = (entry.Namespace + "@" + entry.Version).ToLowerInvariant();
            if (_detailsCache.TryGetValue(cacheKey, out CatalogModuleDetails cached)) {
                return cached;
            }

            string tempDir = _tempDirectory ?? Path.GetTempPath();
            string tempFilePath = Path.Combine(tempDir, "bh-mod-" + Guid.NewGuid().ToString("N") + ".tmp");

            try {
                await _downloadFileAsync(entry.Location, tempFilePath, cancellationToken).ConfigureAwait(false);

                if (!File.Exists(tempFilePath)) {
                    return new CatalogModuleDetails(false, entry.Namespace, entry.Name, entry.Version, problem: "Download produced no file.");
                }

                // Verify SHA-256 hash
                if (string.IsNullOrWhiteSpace(entry.Hash)) {
                    return new CatalogModuleDetails(false, entry.Namespace, entry.Name, entry.Version, problem: "Catalog entry has no package hash.");
                }

                string computedHash;
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(tempFilePath)) {
                    byte[] hashBytes = sha.ComputeHash(stream);
                    computedHash = BitConverter.ToString(hashBytes).Replace("-", "");
                }

                string cleanExpected = entry.Hash.Replace("-", "");
                if (!string.Equals(computedHash, cleanExpected, StringComparison.OrdinalIgnoreCase)) {
                    return new CatalogModuleDetails(false, entry.Namespace, entry.Name, entry.Version, problem: "Hash mismatch.");
                }

                // Read manifest.json from the zip archive
                string manifestJson;
                using (var archive = ZipFile.OpenRead(tempFilePath)) {
                    ZipArchiveEntry manifestEntry = archive.GetEntry("manifest.json");
                    if (manifestEntry == null) {
                        return new CatalogModuleDetails(false, entry.Namespace, entry.Name, entry.Version, problem: "Package does not contain manifest.json.");
                    }

                    using (var entryStream = manifestEntry.Open())
                    using (var reader = new StreamReader(entryStream)) {
                        manifestJson = await reader.ReadToEndAsync().ConfigureAwait(false);
                    }
                }

                // Parse manifest.json for description and api_permissions
                string description = string.Empty;
                var apiPermissions = new List<string>();

                JObject obj = JObject.Parse(manifestJson);
                if (obj.TryGetValue("description", out JToken descToken)) {
                    description = descToken?.ToString() ?? string.Empty;
                }

                if (obj.TryGetValue("api_permissions", out JToken permsToken) && permsToken != null && permsToken.Type != JTokenType.Null) {
                    if (permsToken is JObject permsObj) {
                        foreach (JProperty prop in permsObj.Properties()) {
                            if (string.IsNullOrWhiteSpace(prop.Name)) {
                                return new CatalogModuleDetails(false, entry.Namespace, entry.Name, entry.Version, problem: "Invalid api_permissions format in manifest.json.");
                            }
                            apiPermissions.Add(prop.Name.Trim().ToLowerInvariant());
                        }
                    } else if (permsToken is JArray permsArr) {
                        foreach (JToken item in permsArr) {
                            if (item == null || item.Type != JTokenType.String) {
                                return new CatalogModuleDetails(false, entry.Namespace, entry.Name, entry.Version, problem: "Invalid api_permissions format in manifest.json.");
                            }
                            string val = item.ToString().Trim();
                            if (string.IsNullOrEmpty(val)) {
                                return new CatalogModuleDetails(false, entry.Namespace, entry.Name, entry.Version, problem: "Invalid api_permissions format in manifest.json.");
                            }
                            apiPermissions.Add(val.ToLowerInvariant());
                        }
                    } else {
                        return new CatalogModuleDetails(false, entry.Namespace, entry.Name, entry.Version, problem: "Invalid api_permissions format in manifest.json.");
                    }
                }

                var details = new CatalogModuleDetails(true, entry.Namespace, entry.Name, entry.Version, description, apiPermissions);
                _detailsCache[cacheKey] = details;
                return details;
            } catch (Exception ex) {
                _logWarn?.Invoke("Failed to load module details: " + ex.GetType().Name);
                return new CatalogModuleDetails(false, entry.Namespace, entry.Name, entry.Version, problem: "Failed to load module details (" + ex.GetType().Name + ").");
            } finally {
                try {
                    if (File.Exists(tempFilePath)) {
                        File.Delete(tempFilePath);
                    }
                } catch { }
            }
        }

        private static async Task DefaultDownloadPackageAsync(string url, string destPath, CancellationToken cancellationToken) {
            using (var client = new HttpClient()) {
                client.Timeout = TimeSpan.FromMinutes(2);
                using (HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false)) {
                    response.EnsureSuccessStatusCode();
                    using (Stream contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true)) {
                        await contentStream.CopyToAsync(fileStream, 8192, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }
    }
}
