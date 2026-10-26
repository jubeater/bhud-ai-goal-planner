using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// Stores one API key per Provider, encrypted with Windows DPAPI for the current Windows user,
    /// in the module's data folder. Keys never go into Blish HUD's plaintext settings.json and are
    /// never logged.
    /// </summary>
    public sealed class ProviderKeyStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("jubeater.aigoalplanner:provider-key");

        private readonly string _directory;

        public ProviderKeyStore(string directory) {
            if (directory == null) throw new ArgumentNullException(nameof(directory));

            _directory = directory;
        }

        /// <summary>
        /// Returns the stored key for an endpoint, or null when none is stored or the stored entry
        /// cannot be read.
        /// </summary>
        public string Load(string endpoint) {
            if (string.IsNullOrWhiteSpace(endpoint)) return null;

            string path = PathFor(endpoint);

            if (File.Exists(path)) {
                return ReadKey(path);
            }

            string oldPath = OldPathFor(endpoint);
            if (!string.Equals(oldPath, path, StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath)) {
                string key = ReadKey(oldPath);
                if (key != null) {
                    try {
                        Directory.CreateDirectory(_directory);
                        File.Copy(oldPath, path, overwrite: true);
                        File.Delete(oldPath);
                    } catch {
                        // Migration is best-effort; return key regardless
                    }
                }
                return key;
            }

            return null;
        }

        /// <summary>
        /// Stores a key for an endpoint. An empty key removes the entry, so a local Provider can
        /// work without one.
        /// </summary>
        public void Save(string endpoint, string key) {
            if (string.IsNullOrWhiteSpace(endpoint)) throw new ArgumentException("An endpoint is required.", nameof(endpoint));

            if (string.IsNullOrEmpty(key)) {
                Remove(endpoint);
                return;
            }

            Directory.CreateDirectory(_directory);

            byte[] plain     = Encoding.UTF8.GetBytes(key);
            byte[] encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

            string path = PathFor(endpoint);
            File.WriteAllBytes(path, encrypted);

            string oldPath = OldPathFor(endpoint);
            if (!string.Equals(oldPath, path, StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath)) {
                try {
                    File.Delete(oldPath);
                } catch {
                    // Ignore deletion failure of old file
                }
            }
        }

        public bool HasKey(string endpoint) {
            return !string.IsNullOrEmpty(Load(endpoint));
        }

        public void Remove(string endpoint) {
            if (string.IsNullOrWhiteSpace(endpoint)) return;

            Exception deleteException = null;

            string path = PathFor(endpoint);
            if (File.Exists(path)) {
                try {
                    File.Delete(path);
                } catch (Exception exception) {
                    deleteException = exception;
                }
            }

            string oldPath = OldPathFor(endpoint);
            if (!string.Equals(oldPath, path, StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath)) {
                try {
                    File.Delete(oldPath);
                } catch (Exception exception) {
                    if (deleteException == null) {
                        deleteException = exception;
                    }
                }
            }

            if (deleteException != null) {
                throw deleteException;
            }
        }

        /// <summary>
        /// A file name that identifies the endpoint without putting the URL itself in a file name.
        /// Normalizes the endpoint (trim, drop trailing slash, ignore case) matching SavedProviders.
        /// </summary>
        public string PathFor(string endpoint) {
            string normalized = ProviderEndpointSuggestions.Normalize(endpoint).ToLowerInvariant();
            return FilePathFor(normalized);
        }

        /// <summary>
        /// Legacy file name before endpoint normalization, which kept a trailing slash.
        /// </summary>
        public string OldPathFor(string endpoint) {
            string normalized = ProviderEndpointSuggestions.Normalize(endpoint).ToLowerInvariant();
            return FilePathFor(normalized + "/");
        }

        private string FilePathFor(string normalizedEndpoint) {
            using (SHA256 sha256 = SHA256.Create()) {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalizedEndpoint));

                var name = new StringBuilder(hash.Length * 2);

                foreach (byte value in hash) name.Append(value.ToString("x2"));

                return Path.Combine(_directory, "provider-" + name + ".key");
            }
        }

        private static string ReadKey(string path) {
            try {
                byte[] encrypted = File.ReadAllBytes(path);
                byte[] plain     = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);

                return Encoding.UTF8.GetString(plain);
            } catch (Exception) {
                return null;
            }
        }
    }
}
