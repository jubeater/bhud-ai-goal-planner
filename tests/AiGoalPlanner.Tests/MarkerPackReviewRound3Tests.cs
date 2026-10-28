using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.Tests.Fakes;
using jubeater.aigoalplanner.World;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    public class MarkerPackReviewRound3Tests : IDisposable
    {
        private readonly string _testDir;

        public MarkerPackReviewRound3Tests() {
            _testDir = Path.Combine(Path.GetTempPath(), "round3_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose() {
            try {
                if (Directory.Exists(_testDir)) {
                    Directory.Delete(_testDir, recursive: true);
                }
            } catch { }
        }

        private static async Task<PlanReview> CreateReviewAsync(FakeWorld world, string packName) {
            var plan = new Plan("Download", new[] {
                new PlanAction(PlanActionKind.DownloadPack, "download_pack", null, null, null,
                               "Needed for markers", isSideEffect: false, pack: packName)
            });
            return await PlanValidator.ReviewAsync(plan, world, CancellationToken.None);
        }

        private static byte[] CreateValidZipBytes() {
            using (var memoryStream = new MemoryStream()) {
                using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true)) {
                    var entry = archive.CreateEntry("marker.xml");
                    using (var writer = new StreamWriter(entry.Open())) {
                        writer.Write("<xml/>");
                    }
                }
                return memoryStream.ToArray();
            }
        }

        [Fact]
        public async Task Failed_restart_keeps_committed_download_in_module_Undo_journal() {
            var world = new FakeWorld {
                MarkerPacks = {
                    new MarkerPackInfo("Tekkit's All-In-One", "tw_ALL_IN_ONE.taco", "General", "Hero Points", 47.1, "url", "1", false)
                }
            };
            world.Modules.Add(new InstalledModuleInfo("bh.community.pathing", "Pathing", "1.13.2", "", enabled: true));

            world.AnswerDownloadMarkerPack = (pack, ct) => {
                world.DownloadedPacks.Add(pack);
                return Task.FromResult(new MarkerPackChange(true, true, pack, "tw_ALL_IN_ONE.taco", 47.1));
            };

            world.AnswerSetModuleEnabled = (@namespace, enabled) => {
                InstalledModuleInfo mod = world.Modules.First(m => m.Namespace == @namespace);
                if (!enabled) {
                    world.Modules.Remove(mod);
                    world.Modules.Add(new InstalledModuleInfo(mod.Namespace, mod.Name, mod.Version, mod.Description, false));
                    return new ModuleChange(true, true);
                }
                return new ModuleChange(false, false, "Pathing crashed on reload");
            };

            var target = new GameThreadWorld(world, new ImmediateGameThread());
            var plans = new Plans(target);
            PlanReview review = await CreateReviewAsync(world, "Tekkit's All-In-One");

            AppliedPlan applied = await plans.ApplyAsync(review, new[] { 0 }, CancellationToken.None);

            Assert.Single(world.DownloadedPacks);
            Assert.False(world.Modules.First(m => m.Name == "Pathing").Enabled);
            Assert.Single(applied.Undo);
            ApplyOutcome outcome = Assert.Single(applied.Outcomes);
            Assert.False(outcome.Succeeded);
            Assert.Contains("Pathing crashed on reload", outcome.Problem);

            // Standards 1 (P1): Succeeded is false, but Changed must be true and UndoJournal must retain it
            Assert.True(outcome.Changed);
            Assert.True(applied.AnyChanged);
            Assert.True(plans.CanUndo);

            // Undo can be executed to delete the pack
            var deletedPacks = new List<string>();
            world.AnswerDeleteMarkerPack = (fileName, ct) => {
                deletedPacks.Add(fileName);
                return Task.FromResult(new MarkerPackChange(true, true, "Tekkit's All-In-One", fileName, 47.1));
            };
            UndoResult undoResult = await plans.UndoAsync(CancellationToken.None);
            Assert.Contains("tw_ALL_IN_ONE.taco", deletedPacks);
            Assert.False(plans.CanUndo);
        }

        [Fact]
        public async Task PlanValidator_rejects_pack_with_invalid_file_name_before_review() {
            var world = new FakeWorld {
                MarkerPacks = {
                    new MarkerPackInfo("Dangerous Pack", "../outside.taco", "Desc", "Cat", 10.0, "url", "1", false)
                }
            };
            PlanReview review = await CreateReviewAsync(world, "Dangerous Pack");
            ReviewedAction action = Assert.Single(review.Actions);

            Assert.False(action.CanApply);
            Assert.Contains("not a valid marker pack file", action.Problem);
        }

        [Fact]
        public async Task Repository_file_name_cannot_write_outside_markers() {
            string markersDir = Path.Combine(_testDir, "markers");
            string cacheDir = Path.Combine(_testDir, "cache");
            Directory.CreateDirectory(markersDir);
            Directory.CreateDirectory(cacheDir);

            string repoJson = @"[
                {
                    ""Name"": ""Official"",
                    ""FileName"": ""../outside.taco"",
                    ""Description"": ""Malicious path pack"",
                    ""Categories"": ""Test"",
                    ""Download"": ""https://example.com/pack.zip"",
                    ""Size"": 1.0,
                    ""Version"": ""1.0""
                }
            ]";

            byte[] zipBytes = CreateValidZipBytes();

            var service = new MarkerPackService(
                markersDir,
                cacheDir,
                httpGetAsync: (url, ct) => Task.FromResult(repoJson),
                downloadFileAsync: (url, dest, ct) => {
                    File.WriteAllBytes(dest, zipBytes);
                    return Task.CompletedTask;
                }
            );

            MarkerPackChange change = await service.DownloadPackAsync("Official", CancellationToken.None);

            Assert.False(change.Applied);
            Assert.False(File.Exists(Path.Combine(_testDir, "outside.taco")));
            Assert.False(File.Exists(Path.Combine(markersDir, "outside.taco")));

            // Undo/delete also rejects paths outside markers
            MarkerPackChange deleteChange = await service.DeletePackAsync("../outside.taco", CancellationToken.None);
            Assert.False(deleteChange.Applied);
        }

        [Theory]
        [InlineData("tw_ALL_IN_ONE.taco", true)]
        [InlineData("pack.zip", true)]
        [InlineData("PACK.TACO", true)]
        [InlineData("my-pack_1.2.zip", true)]
        [InlineData("../outside.taco", false)]
        [InlineData("..\\outside.taco", false)]
        [InlineData("sub/pack.taco", false)]
        [InlineData("sub\\pack.taco", false)]
        [InlineData("/pack.taco", false)]
        [InlineData("\\pack.taco", false)]
        [InlineData(".", false)]
        [InlineData("..", false)]
        [InlineData(".taco", false)]
        [InlineData(".zip", false)]
        [InlineData("pack.exe", false)]
        [InlineData("pack.txt", false)]
        [InlineData("pack", false)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData(null, false)]
        [InlineData("pack<bad>.taco", false)]
        [InlineData("pack:bad.zip", false)]
        public void Marker_pack_filename_validation_rules(string fileName, bool expectedValid) {
            Assert.Equal(expectedValid, MarkerPackService.IsValidFileName(fileName));
        }

        [Fact]
        public void Marker_pack_path_inside_directory_guards() {
            string markers = Path.Combine(_testDir, "markers");
            string valid = Path.Combine(markers, "tw_ALL_IN_ONE.taco");
            string traversal = Path.Combine(markers, "..", "outside.taco");
            string sibling = Path.Combine(_testDir, "markers_other", "file.taco");

            Assert.True(MarkerPackService.IsInsideMarkersDirectory(valid, markers));
            Assert.False(MarkerPackService.IsInsideMarkersDirectory(traversal, markers));
            Assert.False(MarkerPackService.IsInsideMarkersDirectory(sibling, markers));
            Assert.False(MarkerPackService.IsInsideMarkersDirectory(null, markers));
            Assert.False(MarkerPackService.IsInsideMarkersDirectory(valid, null));
        }
    }
}
