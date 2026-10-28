using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.World;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    public class MarkerPackServiceTests : IDisposable
    {
        private readonly string _tempRoot;
        private readonly string _markersDir;
        private readonly string _cacheDir;

        private const string SampleRepoJson = @"[
  {
    ""Name"": ""Tekkit's All-In-One"",
    ""Description"": ""Jack-of-all-trades markers."",
    ""Download"": ""https://example.com/tw_ALL_IN_ONE.taco"",
    ""FileName"": ""tw_ALL_IN_ONE.taco"",
    ""Categories"": ""Hero Points, Routes"",
    ""Version"": ""12345"",
    ""Size"": 47.1,
    ""MapIds"": [ 1210, 1045 ]
  },
  {
    ""Name"": ""Fast TacO"",
    ""Description"": ""Farming routes."",
    ""Download"": ""https://example.com/fast_TacO_pack.taco"",
    ""FileName"": ""fast_TacO_pack.taco"",
    ""Categories"": ""Solo Farming"",
    ""Version"": ""67890"",
    ""Size"": 1.5,
    ""MapIds"": [ 1343 ]
  }
]";

        public MarkerPackServiceTests() {
            _tempRoot   = Path.Combine(Path.GetTempPath(), "MarkerPackServiceTests_" + Guid.NewGuid().ToString("N"));
            _markersDir = Path.Combine(_tempRoot, "markers");
            _cacheDir   = Path.Combine(_tempRoot, "cache");

            Directory.CreateDirectory(_markersDir);
            Directory.CreateDirectory(_cacheDir);
        }

        public void Dispose() {
            if (Directory.Exists(_tempRoot)) {
                try {
                    Directory.Delete(_tempRoot, recursive: true);
                } catch { }
            }
        }

        private static byte[] CreateValidZipBytes() {
            using (var memory = new MemoryStream()) {
                using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true)) {
                    ZipArchiveEntry entry = archive.CreateEntry("test.xml");
                    using (Stream writer = entry.Open()) {
                        byte[] bytes = Encoding.UTF8.GetBytes("<root />");
                        writer.Write(bytes, 0, bytes.Length);
                    }
                }
                return memory.ToArray();
            }
        }

        private static byte[] CreateEmptyZipBytes() {
            using (var memory = new MemoryStream()) {
                using (new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true)) { }
                return memory.ToArray();
            }
        }

        [Fact]
        public async Task GetPacksAsync_parses_official_repo_json_and_marks_installed() {
            // Put fast_TacO_pack.taco in markers directory
            File.WriteAllText(Path.Combine(_markersDir, "fast_TacO_pack.taco"), "dummy");

            var service = new MarkerPackService(
                _markersDir,
                _cacheDir,
                httpGet: url => Task.FromResult(SampleRepoJson));

            MarkerPacksResult result = await service.GetPacksAsync(pathingInstalled: true);

            Assert.Equal(MarkerPacksStatus.Ok, result.Status);
            Assert.True(result.PathingInstalled);
            Assert.Equal(2, result.Packs.Count);

            MarkerPackInfo tekkit = result.Packs[0];
            Assert.Equal("Tekkit's All-In-One", tekkit.Name);
            Assert.Equal("tw_ALL_IN_ONE.taco", tekkit.FileName);
            Assert.False(tekkit.Installed);
            Assert.Equal(47.1, tekkit.SizeMb);
            Assert.Equal("Hero Points, Routes", tekkit.Categories);

            MarkerPackInfo fast = result.Packs[1];
            Assert.Equal("Fast TacO", fast.Name);
            Assert.True(fast.Installed);
        }

        [Fact]
        public async Task GetPacksAsync_includes_unlisted_local_packs() {
            File.WriteAllText(Path.Combine(_markersDir, "custom_user_pack.taco"), "dummy");

            var service = new MarkerPackService(
                _markersDir,
                _cacheDir,
                httpGet: url => Task.FromResult(SampleRepoJson));

            MarkerPacksResult result = await service.GetPacksAsync(pathingInstalled: true);

            Assert.Equal(3, result.Packs.Count);
            Assert.Contains(result.Packs, p => p.FileName == "custom_user_pack.taco" && p.Installed);
        }

        [Fact]
        public async Task DownloadPackAsync_rejects_pack_not_on_official_list() {
            var service = new MarkerPackService(
                _markersDir,
                _cacheDir,
                httpGet: url => Task.FromResult(SampleRepoJson));

            MarkerPackChange change = await service.DownloadPackAsync("Unknown Shady Pack");

            Assert.False(change.Applied);
            Assert.Contains("not on Pathing's official", change.Problem);
        }

        [Fact]
        public async Task DownloadPackAsync_downloads_valid_zip_and_moves_to_markers_dir() {
            byte[] validZip = CreateValidZipBytes();

            var service = new MarkerPackService(
                _markersDir,
                _cacheDir,
                httpGet: url => Task.FromResult(SampleRepoJson),
                downloadFileAsync: (url, dest, token) => {
                    File.WriteAllBytes(dest, validZip);
                    return Task.CompletedTask;
                });

            MarkerPackChange change = await service.DownloadPackAsync("Tekkit's All-In-One");

            Assert.True(change.Applied);
            Assert.True(change.Changed);
            Assert.Equal("Tekkit's All-In-One", change.PackName);
            Assert.Equal("tw_ALL_IN_ONE.taco", change.FileName);

            string targetFile = Path.Combine(_markersDir, "tw_ALL_IN_ONE.taco");
            Assert.True(File.Exists(targetFile));
        }

        [Fact]
        public async Task DownloadPackAsync_rejects_corrupt_zip() {
            byte[] corruptData = Encoding.UTF8.GetBytes("not a zip file at all");

            var service = new MarkerPackService(
                _markersDir,
                _cacheDir,
                httpGet: url => Task.FromResult(SampleRepoJson),
                downloadFileAsync: (url, dest, token) => {
                    File.WriteAllBytes(dest, corruptData);
                    return Task.CompletedTask;
                });

            MarkerPackChange change = await service.DownloadPackAsync("Tekkit's All-In-One");

            Assert.False(change.Applied);
            Assert.Contains("not a valid", change.Problem);
            Assert.False(File.Exists(Path.Combine(_markersDir, "tw_ALL_IN_ONE.taco")));
        }

        [Fact]
        public async Task DownloadPackAsync_rejects_empty_zip() {
            byte[] emptyZip = CreateEmptyZipBytes();

            var service = new MarkerPackService(
                _markersDir,
                _cacheDir,
                httpGet: url => Task.FromResult(SampleRepoJson),
                downloadFileAsync: (url, dest, token) => {
                    File.WriteAllBytes(dest, emptyZip);
                    return Task.CompletedTask;
                });

            MarkerPackChange change = await service.DownloadPackAsync("Tekkit's All-In-One");

            Assert.False(change.Applied);
            Assert.Contains("empty", change.Problem);
            Assert.False(File.Exists(Path.Combine(_markersDir, "tw_ALL_IN_ONE.taco")));
        }

        [Fact]
        public async Task DownloadPackAsync_returns_not_changed_when_already_installed() {
            File.WriteAllText(Path.Combine(_markersDir, "tw_ALL_IN_ONE.taco"), "already there");

            var service = new MarkerPackService(
                _markersDir,
                _cacheDir,
                httpGet: url => Task.FromResult(SampleRepoJson));

            MarkerPackChange change = await service.DownloadPackAsync("Tekkit's All-In-One");

            Assert.True(change.Applied);
            Assert.False(change.Changed);
        }

        [Fact]
        public async Task DeletePackAsync_removes_file_from_markers_dir() {
            string filePath = Path.Combine(_markersDir, "tw_ALL_IN_ONE.taco");
            File.WriteAllText(filePath, "test");

            var service = new MarkerPackService(_markersDir, _cacheDir);

            MarkerPackChange change = await service.DeletePackAsync("tw_ALL_IN_ONE.taco");

            Assert.True(change.Applied);
            Assert.True(change.Changed);
            Assert.False(File.Exists(filePath));
        }
    }
}
