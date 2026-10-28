using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.Tests.Fakes;
using jubeater.aigoalplanner.World;
using Microsoft.Extensions.AI;
using Newtonsoft.Json.Linq;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    public class GetModuleDetailsToolTests : IDisposable
    {
        private readonly string _testTempDir;

        public GetModuleDetailsToolTests() {
            _testTempDir = Path.Combine(Path.GetTempPath(), "test-details-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testTempDir);
        }

        public void Dispose() {
            try {
                if (Directory.Exists(_testTempDir)) {
                    Directory.Delete(_testTempDir, true);
                }
            } catch { }
        }

        private static byte[] CreatePackageBytes(string manifestJson) {
            using (var memory = new MemoryStream()) {
                using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true)) {
                    ZipArchiveEntry entry = archive.CreateEntry("manifest.json");
                    using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8)) {
                        writer.Write(manifestJson);
                    }
                }
                return memory.ToArray();
            }
        }

        private static string Sha256Of(byte[] bytes) {
            using (var sha = SHA256.Create()) {
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
            }
        }

        [Fact]
        public async Task It_fetches_module_details_from_catalog_and_deletes_temp_file() {
            string manifest = @"{
                ""name"": ""Pathing"",
                ""version"": ""3.2.1"",
                ""namespace"": ""bh.community.pathing"",
                ""description"": ""Markers and trails for Guild Wars 2."",
                ""api_permissions"": {
                    ""account"": { ""optional"": false },
                    ""characters"": { ""optional"": false }
                }
            }";
            byte[] packageBytes = CreatePackageBytes(manifest);
            string hash = Sha256Of(packageBytes);

            int downloadCalls = 0;
            Func<string, string, CancellationToken, Task> downloadFileAsync = (url, dest, ct) => {
                downloadCalls++;
                File.WriteAllBytes(dest, packageBytes);
                return Task.CompletedTask;
            };

            var world = new FakeWorld {
                CatalogModules = {
                    new CatalogModuleInfo("bh.community.pathing", "Pathing", "3.2.1")
                }
            };
            world.CatalogDetails["bh.community.pathing"] = new CatalogModuleDetails(
                success: true,
                @namespace: "bh.community.pathing",
                name: "Pathing",
                version: "3.2.1",
                description: "Markers and trails for Guild Wars 2.",
                apiPermissions: new[] { "account", "characters" }
            );

            AIFunction tool = GetModuleDetailsTool.Create(world);
            var args = new AIFunctionArguments { ["module"] = "bh.community.pathing" };
            object raw = await tool.InvokeAsync(args);

            JObject json = JObject.Parse(Assert.IsType<string>(raw));
            Assert.Equal("ok", (string)json["status"]);
            Assert.Equal("bh.community.pathing", (string)json["namespace"]);
            Assert.Equal("Pathing", (string)json["name"]);
            Assert.Equal("3.2.1", (string)json["version"]);
            Assert.Equal("Markers and trails for Guild Wars 2.", (string)json["description"]);

            JArray perms = (JArray)json["api_permissions"];
            Assert.Equal(2, perms.Count);
            Assert.Equal("account", (string)perms[0]);
            Assert.Equal("characters", (string)perms[1]);
        }

        [Fact]
        public void Tool_is_registered_in_lookups_as_read_only() {
            var world = new FakeWorld();
            var registry = Lookups.For(world);

            Assert.True(registry.TryGet(GetModuleDetailsTool.Name, out RegisteredTool tool));
            Assert.Equal(ToolAccess.ReadOnly, tool.Access);
        }

        [Fact]
        public async Task It_handles_module_not_found_in_catalog() {
            var world = new FakeWorld();
            AIFunction tool = GetModuleDetailsTool.Create(world);
            var args = new AIFunctionArguments { ["module"] = "bh.nonexistent" };
            object raw = await tool.InvokeAsync(args);

            JObject json = JObject.Parse(Assert.IsType<string>(raw));
            Assert.Equal("not_found", (string)json["status"]);
        }

        [Fact]
        public async Task Catalog_details_service_caches_by_namespace_and_version() {
            string manifest = @"{
                ""name"": ""Pathing"",
                ""version"": ""3.2.1"",
                ""namespace"": ""bh.community.pathing"",
                ""description"": ""Markers and trails."",
                ""api_permissions"": [""account""]
            }";
            byte[] packageBytes = CreatePackageBytes(manifest);
            string hash = Sha256Of(packageBytes);

            int downloadCalls = 0;
            Func<string, string, CancellationToken, Task> downloadFileAsync = (url, dest, ct) => {
                downloadCalls++;
                File.WriteAllBytes(dest, packageBytes);
                return Task.CompletedTask;
            };

            var entry = new CatalogPackageEntry("bh.community.pathing", "Pathing", "3.2.1", "https://pkgs.blishhud.com/bh.community.pathing_3.2.1.bhm", hash);
            var service = new CatalogDetailsService(downloadFileAsync, _testTempDir);

            // First call downloads
            CatalogModuleDetails details1 = await service.FetchCatalogModuleDetailsAsync(entry, CancellationToken.None);
            Assert.True(details1.Success);
            Assert.Equal("Markers and trails.", details1.Description);
            Assert.Equal(new[] { "account" }, details1.ApiPermissions);
            Assert.Equal(1, downloadCalls);
            Assert.Empty(Directory.GetFiles(_testTempDir));

            // Second call uses cache
            CatalogModuleDetails details2 = await service.FetchCatalogModuleDetailsAsync(entry, CancellationToken.None);
            Assert.True(details2.Success);
            Assert.Equal(1, downloadCalls);
            Assert.Empty(Directory.GetFiles(_testTempDir));
        }

        [Fact]
        public async Task Catalog_details_service_rejects_on_hash_mismatch_and_deletes_temp_file() {
            string manifest = @"{ ""name"": ""Pathing"", ""namespace"": ""bh.pathing"", ""version"": ""1.0.0"" }";
            byte[] packageBytes = CreatePackageBytes(manifest);

            Func<string, string, CancellationToken, Task> downloadFileAsync = (url, dest, ct) => {
                File.WriteAllBytes(dest, packageBytes);
                return Task.CompletedTask;
            };

            var entry = new CatalogPackageEntry("bh.pathing", "Pathing", "1.0.0", "http://pkg.test", "WRONG_HASH_1234567890ABCDEF");
            var service = new CatalogDetailsService(downloadFileAsync, _testTempDir);

            CatalogModuleDetails details = await service.FetchCatalogModuleDetailsAsync(entry, CancellationToken.None);
            Assert.False(details.Success);
            Assert.Contains("Hash mismatch", details.Problem);
            Assert.Empty(Directory.GetFiles(_testTempDir));
        }

        [Fact]
        public async Task Catalog_details_service_rejects_on_bad_manifest_and_deletes_temp_file() {
            byte[] corruptZip = Encoding.UTF8.GetBytes("not a zip file at all");

            Func<string, string, CancellationToken, Task> downloadFileAsync = (url, dest, ct) => {
                File.WriteAllBytes(dest, corruptZip);
                return Task.CompletedTask;
            };

            var entry = new CatalogPackageEntry("bh.broken", "Broken", "1.0.0", "http://pkg.test", Sha256Of(corruptZip));
            var service = new CatalogDetailsService(downloadFileAsync, _testTempDir);

            CatalogModuleDetails details = await service.FetchCatalogModuleDetailsAsync(entry, CancellationToken.None);
            Assert.False(details.Success);
            Assert.Empty(Directory.GetFiles(_testTempDir));
        }

        [Fact]
        public async Task Catalog_details_service_rejects_on_missing_manifest_and_deletes_temp_file() {
            byte[] zipWithoutManifest;
            using (var memory = new MemoryStream()) {
                using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true)) {
                    var entryZip = archive.CreateEntry("some_other_file.txt");
                    using (var writer = new StreamWriter(entryZip.Open())) {
                        writer.Write("hello");
                    }
                }
                zipWithoutManifest = memory.ToArray();
            }

            Func<string, string, CancellationToken, Task> downloadFileAsync = (url, dest, ct) => {
                File.WriteAllBytes(dest, zipWithoutManifest);
                return Task.CompletedTask;
            };

            var entry = new CatalogPackageEntry("bh.nomanifest", "NoManifest", "1.0.0", "http://pkg.test", Sha256Of(zipWithoutManifest));
            var service = new CatalogDetailsService(downloadFileAsync, _testTempDir);

            CatalogModuleDetails details = await service.FetchCatalogModuleDetailsAsync(entry, CancellationToken.None);
            Assert.False(details.Success);
            Assert.Contains("manifest.json", details.Problem);
            Assert.Empty(Directory.GetFiles(_testTempDir));
        }

        [Fact]
        public async Task Catalog_details_service_deletes_temp_file_when_download_throws() {
            Func<string, string, CancellationToken, Task> downloadFileAsync = (url, dest, ct) => {
                File.WriteAllText(dest, "partially downloaded content");
                throw new InvalidOperationException("Network down");
            };

            var entry = new CatalogPackageEntry("bh.throw", "Throw", "1.0.0", "http://pkg.test", "HASH");
            var service = new CatalogDetailsService(downloadFileAsync, _testTempDir);

            CatalogModuleDetails details = await service.FetchCatalogModuleDetailsAsync(entry, CancellationToken.None);
            Assert.False(details.Success);
            Assert.Empty(Directory.GetFiles(_testTempDir));
        }

        [Fact]
        public async Task GameThreadWorld_reads_catalog_entry_on_game_thread_and_fetches_off_thread() {
            var world = new FakeWorld {
                CatalogModules = {
                    new CatalogModuleInfo("bh.community.pathing", "Pathing", "3.2.1")
                }
            };
            world.CatalogDetails["bh.community.pathing"] = new CatalogModuleDetails(
                success: true,
                @namespace: "bh.community.pathing",
                name: "Pathing",
                version: "3.2.1",
                description: "Markers and trails for Guild Wars 2.",
                apiPermissions: new[] { "account" }
            );

            var gameThread = new FakeGameThread();
            var target = new GameThreadWorld(world, gameThread);

            Task<CatalogModuleDetails> task = target.GetCatalogModuleDetailsAsync("bh.community.pathing", CancellationToken.None);

            // Read of PkgManifest entry was queued to game thread
            Assert.Equal(1, gameThread.WaitingCount);
            gameThread.Pump();

            CatalogModuleDetails result = await task;
            Assert.True(result.Success);
            Assert.Equal("bh.community.pathing", result.Namespace);
            Assert.Equal("Markers and trails for Guild Wars 2.", result.Description);
        }
    }
}
