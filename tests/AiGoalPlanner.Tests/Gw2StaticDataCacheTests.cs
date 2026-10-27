using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

            string mapJson = "{\"version\":2,\"id\":1205,\"name\":\"Domain of Vabbi\",\"region_id\":10,\"region_name\":\"Crystal Desert\","
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

            string map1 = "{\"version\":2,\"id\":101,\"name\":\"Map One\",\"region_id\":10,\"region_name\":\"Region A\","
                        + "\"continent_id\":1,\"continent_name\":\"Tyria\",\"skill_challenges\":[\"c1\",\"c2\",\"c3\"]}";
            string map2 = "{\"version\":2,\"id\":102,\"name\":\"Map Two\",\"region_id\":10,\"region_name\":\"Region A\","
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
        public void Region_missing_hero_challenges_sums_all_maps_loaded_from_disk_on_startup() {
            string cacheDir = Path.Combine(_tempDir, "cache");
            Directory.CreateDirectory(cacheDir);

            string map1 = "{\"version\":2,\"id\":101,\"name\":\"Map One\",\"region_id\":10,\"region_name\":\"Region A\","
                        + "\"continent_id\":1,\"continent_name\":\"Tyria\",\"skill_challenges\":[\"c1\",\"c2\",\"c3\"]}";
            string map2 = "{\"version\":2,\"id\":102,\"name\":\"Map Two\",\"region_id\":10,\"region_name\":\"Region A\","
                        + "\"continent_id\":1,\"continent_name\":\"Tyria\",\"skill_challenges\":[\"c4\",\"c5\"]}";
            File.WriteAllText(Path.Combine(cacheDir, "map_101.json"), map1);
            File.WriteAllText(Path.Combine(cacheDir, "map_102.json"), map2);

            // Fresh cache instance without calling GetMap first
            var cache = new Gw2StaticDataCache(_tempDir);

            var completed = new HashSet<string> { "c1" };
            int missing = cache.GetRegionMissingHeroChallenges(10, completed);

            // 5 total in region 10, 1 completed -> 4 missing
            Assert.Equal(4, missing);
        }

        [Fact]
        public void When_map_challenges_failed_to_load_it_is_not_cached_as_complete_and_subsequent_fetch_succeeds() {
            string cacheDir = Path.Combine(_tempDir, "cache");
            Directory.CreateDirectory(cacheDir);

            var cache = new Gw2StaticDataCache(_tempDir);

            // Attempt 1: Map created with challengesLoaded: false (e.g. floor map API call failed)
            var failedMap = new CachedMapInfo(301, "Map Incomplete", 12, "Region B", 1, "Tyria", Array.Empty<string>(), defaultFloor: 0, challengesLoaded: false);
            cache.SaveMap(failedMap);

            // Verify it was not cached as complete on disk (no file written) or GetMap does not return it as complete
            CachedMapInfo cachedAfterFailure = cache.GetMap(301);
            Assert.True(cachedAfterFailure == null || !cachedAfterFailure.ChallengesLoaded);

            // Attempt 2: Next fetch succeeds with challenges
            var successfulMap = new CachedMapInfo(301, "Map Incomplete", 12, "Region B", 1, "Tyria", new[] { "sc-10", "sc-11" }, defaultFloor: 0, challengesLoaded: true);
            cache.SaveMap(successfulMap);

            CachedMapInfo loaded = cache.GetMap(301);
            Assert.NotNull(loaded);
            Assert.True(loaded.ChallengesLoaded);
            Assert.Equal(2, loaded.SkillChallenges.Count);
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
        public void A_region_whose_maps_are_on_two_floors_still_counts_both_floors_challenges() {
            string cacheDir = Path.Combine(_tempDir, "cache");
            Directory.CreateDirectory(cacheDir);

            // Two maps in region 37 on different floors: Map 10 on floor 80 (small floor), Map 11 on floor 1
            string mapsJson = "{\"version\":2,\"maps\":[{\"id\":10,\"name\":\"Small Floor Map\",\"region_id\":37,\"region_name\":\"Region 37\",\"continent_id\":1,\"continent_name\":\"Tyria\",\"default_floor\":80,\"type\":\"Public\"},"
                            + "{\"id\":11,\"name\":\"Main Floor Map\",\"region_id\":37,\"region_name\":\"Region 37\",\"continent_id\":1,\"continent_name\":\"Tyria\",\"default_floor\":1,\"type\":\"Public\"}]}";
            File.WriteAllText(Path.Combine(cacheDir, "maps.json"), mapsJson);

            // Map 10 has 5 skill challenges, Map 11 has 10 skill challenges
            string map10 = "{\"version\":2,\"id\":10,\"name\":\"Small Floor Map\",\"region_id\":37,\"region_name\":\"Region 37\","
                         + "\"continent_id\":1,\"continent_name\":\"Tyria\",\"default_floor\":80,\"skill_challenges\":[\"sc-1\",\"sc-2\",\"sc-3\",\"sc-4\",\"sc-5\"]}";
            string map11 = "{\"version\":2,\"id\":11,\"name\":\"Main Floor Map\",\"region_id\":37,\"region_name\":\"Region 37\","
                         + "\"continent_id\":1,\"continent_name\":\"Tyria\",\"default_floor\":1,\"skill_challenges\":[\"sc-6\",\"sc-7\",\"sc-8\",\"sc-9\",\"sc-10\",\"sc-11\",\"sc-12\",\"sc-13\",\"sc-14\",\"sc-15\"]}";
            File.WriteAllText(Path.Combine(cacheDir, "map_10.json"), map10);
            File.WriteAllText(Path.Combine(cacheDir, "map_11.json"), map11);

            var cache = new Gw2StaticDataCache(_tempDir);

            // Verify both maps are returned for the region across both floors
            IReadOnlyList<CachedMapInfo> regionMaps = cache.GetMapsInRegion(37);
            Assert.Equal(2, regionMaps.Count);

            // Completed 2 on small floor, 3 on main floor (5 completed out of 15 total -> 10 missing)
            var completed = new HashSet<string> { "sc-1", "sc-2", "sc-6", "sc-7", "sc-8" };
            int missing = cache.GetRegionMissingHeroChallenges(37, completed);

            Assert.Equal(10, missing);
        }

        [Fact]
        public void A_region_with_a_Public_map_and_an_Instance_map_whose_floor_returns_404_is_complete_and_missing_covers_only_Public_map() {
            string cacheDir = Path.Combine(_tempDir, "cache");
            Directory.CreateDirectory(cacheDir);

            // Public map 10 on floor 1, Instance map 1446 on floor -53
            string mapsJson = "{\"version\":2,\"maps\":[{\"id\":10,\"name\":\"Public Map\",\"region_id\":37,\"region_name\":\"Region 37\",\"continent_id\":1,\"continent_name\":\"Tyria\",\"default_floor\":1,\"type\":\"Public\"},"
                            + "{\"id\":1446,\"name\":\"Aetherblade Armada\",\"region_id\":37,\"region_name\":\"Region 37\",\"continent_id\":1,\"continent_name\":\"Tyria\",\"default_floor\":-53,\"type\":\"Instance\"}]}";
            File.WriteAllText(Path.Combine(cacheDir, "maps.json"), mapsJson);

            // Public Map 10 has 5 challenges
            string map10 = "{\"version\":2,\"id\":10,\"name\":\"Public Map\",\"region_id\":37,\"region_name\":\"Region 37\","
                         + "\"continent_id\":1,\"continent_name\":\"Tyria\",\"default_floor\":1,\"challenges_loaded\":true,\"type\":\"Public\",\"skill_challenges\":[\"sc-1\",\"sc-2\",\"sc-3\",\"sc-4\",\"sc-5\"]}";
            // Instance Map 1446 floor returns 404 -> cached as loaded with 0 challenges
            string map1446 = "{\"version\":2,\"id\":1446,\"name\":\"Aetherblade Armada\",\"region_id\":37,\"region_name\":\"Region 37\","
                           + "\"continent_id\":1,\"continent_name\":\"Tyria\",\"default_floor\":-53,\"challenges_loaded\":true,\"type\":\"Instance\",\"skill_challenges\":[]}";
            File.WriteAllText(Path.Combine(cacheDir, "map_10.json"), map10);
            File.WriteAllText(Path.Combine(cacheDir, "map_1446.json"), map1446);

            var cache = new Gw2StaticDataCache(_tempDir);

            // 1. GetMapsInRegion returns only the Public map, skipping the Instance map
            IReadOnlyList<CachedMapInfo> regionMaps = cache.GetMapsInRegion(37);
            Assert.Single(regionMaps);
            Assert.Equal(10, regionMaps[0].Id);

            // 2. Region is saved with Public maps, and HasRegion is true (complete)
            cache.SaveRegion(37, regionMaps.Select(m => m.Id).ToList());
            Assert.True(cache.HasRegion(37));

            // 3. Completed 2 on Public map -> missing count covers only Public map (3 missing)
            var completed = new HashSet<string> { "sc-1", "sc-2" };
            int missing = cache.GetRegionMissingHeroChallenges(37, completed);
            Assert.Equal(3, missing);
        }

        [Fact]
        public void A_Public_map_whose_floor_fetch_times_out_leaves_region_incomplete() {
            string cacheDir = Path.Combine(_tempDir, "cache");
            Directory.CreateDirectory(cacheDir);

            string mapsJson = "{\"version\":2,\"maps\":[{\"id\":10,\"name\":\"Public Map\",\"region_id\":37,\"region_name\":\"Region 37\",\"continent_id\":1,\"continent_name\":\"Tyria\",\"default_floor\":1,\"type\":\"Public\"}]}";
            File.WriteAllText(Path.Combine(cacheDir, "maps.json"), mapsJson);

            // Map 10 floor fetch timed out -> not cached or challenges_loaded is false
            var cache = new Gw2StaticDataCache(_tempDir);

            IReadOnlyList<CachedMapInfo> regionMaps = cache.GetMapsInRegion(37);
            Assert.Single(regionMaps);

            // Even if an attempt is made to mark the region with map 10, HasRegion must be false because challenges are not loaded
            cache.SaveRegion(37, regionMaps.Select(m => m.Id).ToList());
            Assert.False(cache.HasRegion(37));
        }

        [Fact]
        public void All_maps_summary_can_be_saved_and_loaded_from_disk() {
            var cache = new Gw2StaticDataCache(_tempDir);
            var maps = new[] {
                new CachedMapInfo(10, "Map 10", 37, "Region 37", 1, "Tyria", Array.Empty<string>(), defaultFloor: 80, challengesLoaded: false),
                new CachedMapInfo(11, "Map 11", 37, "Region 37", 1, "Tyria", Array.Empty<string>(), defaultFloor: 1, challengesLoaded: false)
            };
            cache.SaveAllMaps(maps);

            var freshCache = new Gw2StaticDataCache(_tempDir);
            var loaded = freshCache.GetAllMaps();
            Assert.Equal(2, loaded.Count);
            Assert.Equal(10, loaded[0].Id);
            Assert.Equal(80, loaded[0].DefaultFloor);
            Assert.Equal(11, loaded[1].Id);
            Assert.Equal(1, loaded[1].DefaultFloor);
        }

        [Fact]
        public void IsNotFound_identifies_404_responses_and_not_timeouts_or_server_errors() {
            Assert.True(Gw2ApiExceptionHelper.IsNotFound(new Exception("Response status code does not indicate success: 404 (Not Found).")));
            Assert.False(Gw2ApiExceptionHelper.IsNotFound(new TimeoutException("The operation has timed out.")));
            Assert.False(Gw2ApiExceptionHelper.IsNotFound(new System.Threading.Tasks.TaskCanceledException()));
            Assert.False(Gw2ApiExceptionHelper.IsNotFound(new Exception("500 Internal Server Error")));
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
    
        [Fact]
        public void A_cache_from_the_old_format_is_not_trusted() {
            string cacheDir = Path.Combine(_tempDir, "cache");
            Directory.CreateDirectory(cacheDir);

            // Old format map without version (or version 1)
            string oldMapJson = "{\"id\":1210,\"name\":\"Crystal Oasis\",\"region_id\":12,\"region_name\":\"Crystal Desert\","
                              + "\"continent_id\":1,\"continent_name\":\"Tyria\",\"default_floor\":1,\"challenges_loaded\":true,"
                              + "\"skill_challenges\":[]}";
            File.WriteAllText(Path.Combine(cacheDir, "map_1210.json"), oldMapJson);

            // Old format maps.json (raw array)
            string oldMapsJson = "[{\"id\":1210,\"name\":\"Crystal Oasis\",\"region_id\":12,\"region_name\":\"Crystal Desert\","
                               + "\"continent_id\":1,\"continent_name\":\"Tyria\",\"default_floor\":1,\"type\":\"Public\"}]";
            File.WriteAllText(Path.Combine(cacheDir, "maps.json"), oldMapsJson);

            // Old format region
            string oldRegionJson = "{\"region_id\":12,\"map_ids\":[1210]}";
            File.WriteAllText(Path.Combine(cacheDir, "region_12.json"), oldRegionJson);

            var cache = new Gw2StaticDataCache(_tempDir);

            Assert.Null(cache.GetMap(1210));
            Assert.Empty(cache.GetAllMaps());
            Assert.False(cache.HasRegion(12));
        }
    }
}
