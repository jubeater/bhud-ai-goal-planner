using System;
using System.Collections.Generic;
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

            if (!File.Exists(path)) return null;

            try {
                byte[] encrypted = File.ReadAllBytes(path);
                byte[] plain     = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);

                return Encoding.UTF8.GetString(plain);
            } catch (Exception) {
                return null;
            }
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

            File.WriteAllBytes(PathFor(endpoint), encrypted);
        }

        public bool HasKey(string endpoint) {
            return !string.IsNullOrEmpty(Load(endpoint));
        }

        public void Remove(string endpoint) {
            if (string.IsNullOrWhiteSpace(endpoint)) return;

            string path = PathFor(endpoint);

            if (File.Exists(path)) File.Delete(path);
        }

        /// <summary>
        /// A file name that identifies the endpoint without putting the URL itself in a file name.
        /// </summary>
        private string PathFor(string endpoint) {
            using (SHA256 sha256 = SHA256.Create()) {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(endpoint.Trim().ToLowerInvariant()));

                var name = new StringBuilder(hash.Length * 2);

                foreach (byte value in hash) name.Append(value.ToString("x2"));

                return Path.Combine(_directory, "provider-" + name + ".key");
            }
        }
    }
}
