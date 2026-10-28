using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.Providers;
using jubeater.aigoalplanner.Tests.Fakes;
using jubeater.aigoalplanner.World;
using Microsoft.Extensions.AI;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    public class MarkerPackReviewRound4Tests : IDisposable
    {
        private readonly string _testDir;

        public MarkerPackReviewRound4Tests() {
            _testDir = Path.Combine(Path.GetTempPath(), "round4_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose() {
            try {
                if (Directory.Exists(_testDir)) {
                    Directory.Delete(_testDir, recursive: true);
                }
            } catch { }
        }

        private static Chat NewChat(ScriptedChatClient model, FakeWorld world) {
            IBlishHudWorld target = new GameThreadWorld(world, new ImmediateGameThread());
            var plans = new Plans(target);
            var tools = Lookups.For(target);
            plans.AddTo(tools);

            return new Chat(() => model, () => SystemPrompt.For("test-model", "https://provider.test/v1"),
                            tools, null, plans);
        }

        [Fact]
        public async Task Chat_reports_committed_pack_with_failed_restart_consistently() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld {
                MarkerPacks = {
                    new MarkerPackInfo("Official", "official.taco", "Desc", "Cat", 10.0, "url", "1", false)
                }
            };
            world.Modules.Add(new InstalledModuleInfo("bh.community.pathing", "Pathing", "1.13.2", "", enabled: true));

            world.AnswerDownloadMarkerPack = (pack, ct) => {
                world.DownloadedPacks.Add(pack);
                return Task.FromResult(new MarkerPackChange(true, true, pack, "official.taco", 10.0));
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

            model.WillCallTool("submit_plan", "call_1",
                               new Dictionary<string, object> {
                                   ["reply"] = "I will download Official.",
                                   ["actions"] = new object[] {
                                       new Dictionary<string, object> {
                                           ["kind"] = "download_pack",
                                           ["pack"] = "Official",
                                           ["reason"] = "Needed for markers",
                                           ["is_side_effect"] = false
                                       }
                                   }
                               });

            // Continuation reply
            model.WillReply("I noticed Pathing had an issue reloading.");

            var chat = NewChat(model, world);
            await chat.SendAsync("Get Official");

            Assert.NotNull(chat.Review);
            Assert.True(chat.Review.HasSomethingToApply);

            await chat.ApplyAsync(new[] { 0 });

            // Pack was downloaded, Pathing failed to re-enable, Undo is available
            Assert.Single(world.DownloadedPacks);
            Assert.False(world.Modules.First(m => m.Name == "Pathing").Enabled);
            Assert.True(chat.CanUndo);

            var report = chat.Messages.FirstOrDefault(m => m.Author == ChatMessageAuthor.Report)?.Text;
            Assert.NotNull(report);
            Assert.Contains("Applied 1 change: Pathing: download Official", report);
            Assert.Contains("Pathing crashed on reload", report);
            Assert.DoesNotContain("Not applied: Pathing: download Official", report);
            Assert.DoesNotContain("Not applied", report);
        }

        [Theory]
        [InlineData(" pack.taco")]
        [InlineData("pack.taco ")]
        [InlineData(" pack.zip ")]
        [InlineData("\tpack.taco")]
        [InlineData("pack.taco\n")]
        public void IsValidFileName_rejects_names_with_leading_or_trailing_whitespace(string fileName) {
            Assert.False(MarkerPackService.IsValidFileName(fileName));
        }

        [Fact]
        public async Task DeletePackAsync_does_not_trim_filename_and_rejects_whitespace_padded_name() {
            string markers = Path.Combine(_testDir, "markers");
            string cache = Path.Combine(_testDir, "cache");
            Directory.CreateDirectory(markers);
            Directory.CreateDirectory(cache);

            string realFile = Path.Combine(markers, "pack.taco");
            File.WriteAllText(realFile, "dummy");

            var service = new MarkerPackService(markers, cache);

            MarkerPackChange change = await service.DeletePackAsync(" pack.taco", CancellationToken.None);

            Assert.False(change.Applied);
            Assert.True(File.Exists(realFile));
        }
    }
}
