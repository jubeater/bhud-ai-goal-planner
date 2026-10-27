using System;
using System.Collections.Generic;
using System.IO;
using jubeater.aigoalplanner.World;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    public sealed class Gw2StaticDataCacheTests : IDisposable
    {
        private readonly string _tempDir;

        public Gw2StaticDataCacheTests() {
            _tempDir = Path.Combine(Path.GetTempPath(), "Gw2StaticDataCacheTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose() {
            try {
                if (Directory.Exists(_tempDir)) {
                    Directory.Delete(_tempDir, recursive: true);
                }
            } catch {
                // Ignore cleanup errors in tests
            }
        }

        [Fact]
        public void Masteries_are_loaded_from_disk_cache_when_present() {
            string cacheDir = Path.Combine(_tempDir, "cache");
            Directory.CreateDirectory(cacheDir);

            string masteriesJson = "[{\"id\":1,\"name\":\"Pact Commander\",\"region\":\"Tyria\",\"total_levels\":5},"
                                 + "{\"id\":2,\"name\":\"Gliding\",\"region\":\"Maguuma\",\"total_levels\":6}]";
            File.WriteAllText(Path.Combine(cacheDir, "masteries.json"), masteriesJson);

            var cache = new Gw2StaticDataCache(_tempDir);

            IReadOnlyList<CachedMasteryTrack> masteries = cache.GetMasteries();

            Assert.NotNull(masteries);
            Assert.Equal(2, masteries.Count);
            Assert.Equal(1, masteries[0].Id);
            Assert.Equal("Pact Commander", masteries[0].Name);
            Assert.Equal("Tyria", masteries[0].Region);
            Assert.Equal(5, masteries[0].TotalLevels);

            Assert.Equal(2, masteries[1].Id);
            Assert.Equal("Gliding", masteries[1].Name);
            Assert.Equal("Maguuma", masteries[1].Region);
            Assert.Equal(6, masteries[1].TotalLevels);
        }

        [Fact]
        public void Masteries_can_be_saved_to_disk_and_memory() {
            var cache = new Gw2StaticDataCache(_tempDir);

            var tracks = new[] {
                new CachedMasteryTrack(10, "Raptor", "Crystal Desert", 5)
            };

            cache.SaveMasteries(tracks);

            // Verify in-memory return
            var loaded = cache.GetMasteries();
            Assert.Single(loaded);
            Assert.Equal("Raptor", loaded[0].Name);

            // Verify a new cache instance reading from disk
            var freshCache = new Gw2StaticDataCache(_tempDir);
            var fromDisk = freshCache.GetMasteries();
            Assert.Single(fromDisk);
            Assert.Equal("Raptor", fromDisk[0].Name);
        }

        [Fact]
        public void Map_is_loaded_from_disk_cache_when_present() {
            string cacheDir = Path.Combine(_tempDir, "cache");
            Directory.CreateDirectory(cacheDir);

            string mapJson = "{\"id\":1205,\"name\":\"Domain of Vabbi\",\"region_id\":10,\"region_name\":\"Crystal Desert\","
                           + "\"continent_id\":1,\"continent_name\":\"Tyria\",\"skill_challenges\":[\"sc-1\",\"sc-2\",\"sc-3\"]}";
            File.WriteAllText(Path.Combine(cacheDir, "map_1205.json"), mapJson);

            var cache = new Gw2StaticDataCache(_tempDir);

            CachedMapInfo map = cache.GetMap(1205);

            Assert.NotNull(map);
            Assert.Equal(1205, map.Id);
            Assert.Equal("Domain of Vabbi", map.Name);
            Assert.Equal(10, map.RegionId);
            Assert.Equal("Crystal Desert", map.RegionName);
            Assert.Equal(1, map.ContinentId);
            Assert.Equal("Tyria", map.ContinentName);
            Assert.Equal(3, map.SkillChallenges.Count);
            Assert.Contains("sc-1", map.SkillChallenges);
            Assert.Contains("sc-2", map.SkillChallenges);
            Assert.Contains("sc-3", map.SkillChallenges);
        }

        [Fact]
        public void Map_can_be_saved_to_disk_and_reloaded() {
            var cache = new Gw2StaticDataCache(_tempDir);
            var mapInfo = new CachedMapInfo(200, "Test Map", 5, "Region 5", 1, "Continent 1", new[] { "sc-a", "sc-b" });

            cache.SaveMap(mapInfo);

            var freshCache = new Gw2StaticDataCache(_tempDir);
            CachedMapInfo loaded = freshCache.GetMap(200);

            Assert.NotNull(loaded);
            Assert.Equal("Test Map", loaded.Name);
            Assert.Equal(2, loaded.SkillChallenges.Count);
        }

        [Fact]
        public void Missing_hero_challenges_per_region_are_calculated_correctly_from_cached_maps() {
            string cacheDir = Path.Combine(_tempDir, "cache");
            Directory.CreateDirectory(cacheDir);

            string map1 = "{\"id\":101,\"name\":\"Map One\",\"region_id\":10,\"region_name\":\"Region A\","
                        + "\"continent_id\":1,\"continent_name\":\"Tyria\",\"skill_challenges\":[\"c1\",\"c2\",\"c3\"]}";
            string map2 = "{\"id\":102,\"name\":\"Map Two\",\"region_id\":10,\"region_name\":\"Region A\","
                        + "\"continent_id\":1,\"continent_name\":\"Tyria\",\"skill_challenges\":[\"c4\",\"c5\"]}";
            File.WriteAllText(Path.Combine(cacheDir, "map_101.json"), map1);
            File.WriteAllText(Path.Combine(cacheDir, "map_102.json"), map2);

            var cache = new Gw2StaticDataCache(_tempDir);

            cache.GetMap(101);
            cache.GetMap(102);

            var completed = new HashSet<string> { "c1", "c4" };
            int missing = cache.GetRegionMissingHeroChallenges(10, completed);

            // 5 total in region 10, 2 completed -> 3 missing
            Assert.Equal(3, missing);
        }

        [Fact]
        public void Malformed_cache_file_does_not_throw_and_logs_warning() {
            string cacheDir = Path.Combine(_tempDir, "cache");
            Directory.CreateDirectory(cacheDir);
            File.WriteAllText(Path.Combine(cacheDir, "masteries.json"), "invalid json content");

            var warnings = new List<string>();
            var cache = new Gw2StaticDataCache(_tempDir, warnings.Add);

            IReadOnlyList<CachedMasteryTrack> result = cache.GetMasteries();

            Assert.Empty(result);
            Assert.Single(warnings);
            Assert.Contains("Failed to load masteries cache", warnings[0]);
        }

        [Fact]
        public void Null_directory_is_handled_gracefully_without_throwing() {
            var cache = new Gw2StaticDataCache(null);

            IReadOnlyList<CachedMasteryTrack> masteries = cache.GetMasteries();
            CachedMapInfo map = cache.GetMap(1);
            int missing = cache.GetRegionMissingHeroChallenges(1, new HashSet<string>());

            Assert.Empty(masteries);
            Assert.Null(map);
            Assert.Equal(0, missing);
        }
    }
}
