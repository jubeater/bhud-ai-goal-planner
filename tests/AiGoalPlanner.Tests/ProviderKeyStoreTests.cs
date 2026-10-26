using System;
using System.IO;
using System.Linq;
using System.Text;
using jubeater.aigoalplanner.Core;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    /// <summary>
    /// Tests the store that keeps the API key: the Player gets the key back after a restart, the
    /// key is unreadable on disk, and a damaged file is treated as no key rather than an error.
    /// </summary>
    public class ProviderKeyStoreTests : IDisposable
    {
        private const string Endpoint = "https://api.deepseek.com";

        private readonly string _directory =
            Path.Combine(Path.GetTempPath(), "aigoalplanner-tests-" + Guid.NewGuid().ToString("n"));

        public void Dispose() {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        [Fact]
        public void A_saved_key_comes_back() {
            var store = new ProviderKeyStore(_directory);

            store.Save(Endpoint, "sk-the-key");

            Assert.Equal("sk-the-key", store.Load(Endpoint));
            Assert.True(store.HasKey(Endpoint));
        }

        [Fact]
        public void A_key_belongs_to_the_endpoint_it_was_saved_for() {
            var store = new ProviderKeyStore(_directory);

            store.Save(Endpoint, "sk-the-key");

            Assert.Null(store.Load("https://ollama.com/v1"));
            Assert.False(store.HasKey("https://ollama.com/v1"));
        }

        [Fact]
        public void The_same_endpoint_in_another_case_finds_the_stored_key() {
            var store = new ProviderKeyStore(_directory);

            store.Save("https://API.DeepSeek.com", "sk-the-key");

            Assert.Equal("sk-the-key", store.Load("https://api.deepseek.com"));
        }

        [Fact]
        public void Saving_an_empty_key_forgets_the_stored_one() {
            var store = new ProviderKeyStore(_directory);
            store.Save(Endpoint, "sk-the-key");

            store.Save(Endpoint, string.Empty);

            Assert.Null(store.Load(Endpoint));
        }

        [Fact]
        public void The_key_cannot_be_read_from_the_file() {
            var store = new ProviderKeyStore(_directory);

            store.Save(Endpoint, "sk-the-key");

            string file = Directory.GetFiles(_directory).Single();
            string raw  = Encoding.UTF8.GetString(File.ReadAllBytes(file));

            Assert.DoesNotContain("sk-the-key", raw);
        }

        [Fact]
        public void A_damaged_key_file_is_treated_as_no_key() {
            var store = new ProviderKeyStore(_directory);
            store.Save(Endpoint, "sk-the-key");

            File.WriteAllBytes(Directory.GetFiles(_directory).Single(), new byte[] { 1, 2, 3, 4 });

            Assert.Null(store.Load(Endpoint));
            Assert.False(store.HasKey(Endpoint));
        }

        [Fact]
        public void No_stored_key_is_no_key_rather_than_an_error() {
            var store = new ProviderKeyStore(_directory);

            Assert.Null(store.Load(Endpoint));
            Assert.Null(store.Load(string.Empty));
        }
    }
}
