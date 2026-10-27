using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.World;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    public sealed class Gw2HeroChallengeResolverTests : IDisposable
    {
        private readonly string _tempDir;

        public Gw2HeroChallengeResolverTests() {
            _tempDir = Path.Combine(Path.GetTempPath(), "Gw2HeroChallengeResolverTests_" + Guid.NewGuid().ToString("N"));
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

        private sealed class FakeGw2HeroChallengeApi : IGw2HeroChallengeApi
        {
            public List<int> FetchedFloorRegions { get; } = new List<int>();
            public List<(int floor, int mapId)> FetchedMapFloors { get; } = new List<(int floor, int mapId)>();

            public Func<int, int, int, Task<IReadOnlyDictionary<int, IReadOnlyList<string>>>> OnFetchFloorRegion;
            public Func<int, int, int, int, Task<IReadOnlyList<string>>> OnFetchMap;

            public Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> FetchFloorRegionChallengesAsync(int continentId, int floor, int regionId, CancellationToken cancellationToken) {
                FetchedFloorRegions.Add(floor);
                if (OnFetchFloorRegion != null) {
                    return OnFetchFloorRegion(continentId, floor, regionId);
                }
                return Task.FromResult<IReadOnlyDictionary<int, IReadOnlyList<string>>>(new Dictionary<int, IReadOnlyList<string>>());
            }

            public Task<IReadOnlyList<string>> FetchMapChallengesAsync(int continentId, int floor, int regionId, int mapId, CancellationToken cancellationToken) {
                FetchedMapFloors.Add((floor, mapId));
                if (OnFetchMap != null) {
                    return OnFetchMap(continentId, floor, regionId, mapId);
                }
                return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
            }
        }

        [Fact]
        public async Task Default_floor_404_second_floor_has_5_challenges_returns_5_and_second_floor_is_remembered() {
            var cache = new Gw2StaticDataCache(_tempDir);
            var fakeApi = new FakeGw2HeroChallengeApi();

            // Map 1210 (Crystal Oasis) has default floor 1, floors [1, 49]
            var map = new CachedMapInfo(1210, "Crystal Oasis", 12, "Crystal Desert", 1, "Tyria",
                                        Array.Empty<string>(), defaultFloor: 1, challengesLoaded: false,
                                        type: "Public", floors: new[] { 1, 49 });

            fakeApi.OnFetchMap = (continentId, floor, regionId, mapId) => {
                if (floor == 1) {
                    throw new Exception("Response status code does not indicate success: 404 (Not Found).");
                }
                if (floor == 49) {
                    return Task.FromResult<IReadOnlyList<string>>(new[] { "sc-1", "sc-2", "sc-3", "sc-4", "sc-5" });
                }
                throw new Exception("Unexpected floor: " + floor);
            };

            var resolver = new Gw2HeroChallengeResolver(cache, fakeApi);

            // Act 1: First resolve
            CachedMapInfo resolved = await resolver.ResolveMapAsync(map, CancellationToken.None);

            // Assert: returns 5 challenges, and working floor is 49
            Assert.NotNull(resolved);
            Assert.True(resolved.ChallengesLoaded);
            Assert.Equal(5, resolved.SkillChallenges.Count);
            Assert.Equal(49, resolved.WorkingFloor);

            // Verify cache saved working floor 49
            CachedMapInfo cached = cache.GetMap(1210);
            Assert.NotNull(cached);
            Assert.Equal(49, cached.WorkingFloor);
            Assert.Equal(5, cached.SkillChallenges.Count);

            // Act 2: Second resolve with remembered working floor: goes straight to floor 49
            fakeApi.FetchedMapFloors.Clear();
            var freshDir = Path.Combine(_tempDir, "fresh");
            Directory.CreateDirectory(freshDir);
            var freshCache = new Gw2StaticDataCache(freshDir);
            var secondResolver = new Gw2HeroChallengeResolver(freshCache, fakeApi);

            var mapWithWorkingFloor = new CachedMapInfo(1210, "Crystal Oasis", 12, "Crystal Desert", 1, "Tyria",
                                                        Array.Empty<string>(), defaultFloor: 1, challengesLoaded: false,
                                                        type: "Public", floors: new[] { 1, 49 }, workingFloor: 49);

            CachedMapInfo secondResolved = await secondResolver.ResolveMapAsync(mapWithWorkingFloor, CancellationToken.None);
            Assert.Equal(5, secondResolved.SkillChallenges.Count);
            Assert.Single(fakeApi.FetchedMapFloors);
            Assert.Equal((49, 1210), fakeApi.FetchedMapFloors[0]);
        }

        [Fact]
        public async Task All_floors_404_returns_0_loaded() {
            var cache = new Gw2StaticDataCache(_tempDir);
            var fakeApi = new FakeGw2HeroChallengeApi();

            // Map 1446 has default floor -53, floors [-53]
            var map = new CachedMapInfo(1446, "Aetherblade Armada", 37, "Cantha", 1, "Tyria",
                                        Array.Empty<string>(), defaultFloor: -53, challengesLoaded: false,
                                        type: "Public", floors: new[] { -53 });

            fakeApi.OnFetchMap = (continentId, floor, regionId, mapId) => {
                throw new Exception("Response status code does not indicate success: 404 (Not Found).");
            };

            var resolver = new Gw2HeroChallengeResolver(cache, fakeApi);

            CachedMapInfo resolved = await resolver.ResolveMapAsync(map, CancellationToken.None);

            Assert.NotNull(resolved);
            Assert.True(resolved.ChallengesLoaded);
            Assert.Empty(resolved.SkillChallenges);

            // Cached on disk / memory as loaded with 0 challenges
            CachedMapInfo cached = cache.GetMap(1446);
            Assert.NotNull(cached);
            Assert.True(cached.ChallengesLoaded);
            Assert.Empty(cached.SkillChallenges);
        }

        [Fact]
        public async Task Default_floor_404_second_floor_times_out_returns_unknown_not_0_retried_next_time() {
            var cache = new Gw2StaticDataCache(_tempDir);
            var fakeApi = new FakeGw2HeroChallengeApi();

            var map = new CachedMapInfo(1210, "Crystal Oasis", 12, "Crystal Desert", 1, "Tyria",
                                        Array.Empty<string>(), defaultFloor: 1, challengesLoaded: false,
                                        type: "Public", floors: new[] { 1, 49 });

            bool timeoutOnFloor49 = true;
            fakeApi.OnFetchMap = (continentId, floor, regionId, mapId) => {
                if (floor == 1) {
                    throw new Exception("Response status code does not indicate success: 404 (Not Found).");
                }
                if (floor == 49) {
                    if (timeoutOnFloor49) {
                        throw new TimeoutException("The operation has timed out.");
                    }
                    return Task.FromResult<IReadOnlyList<string>>(new[] { "sc-1", "sc-2", "sc-3", "sc-4", "sc-5" });
                }
                throw new Exception("Unexpected floor: " + floor);
            };

            var resolver = new Gw2HeroChallengeResolver(cache, fakeApi);

            // Attempt 1: Floor 49 times out
            CachedMapInfo attempt1 = await resolver.ResolveMapAsync(map, CancellationToken.None);

            // Must NOT be loaded, NEVER cached as 0
            Assert.NotNull(attempt1);
            Assert.False(attempt1.ChallengesLoaded);
            Assert.Null(cache.GetMap(1210)); // Never cached as 0 or complete

            // Attempt 2: Next time, timeout resolves and challenges load
            timeoutOnFloor49 = false;
            CachedMapInfo attempt2 = await resolver.ResolveMapAsync(map, CancellationToken.None);

            Assert.NotNull(attempt2);
            Assert.True(attempt2.ChallengesLoaded);
            Assert.Equal(5, attempt2.SkillChallenges.Count);
            Assert.Equal(49, attempt2.WorkingFloor);

            CachedMapInfo cached = cache.GetMap(1210);
            Assert.NotNull(cached);
            Assert.True(cached.ChallengesLoaded);
            Assert.Equal(5, cached.SkillChallenges.Count);
        }

        [Fact]
        public async Task Region_fetch_groups_by_working_floor_and_fetches_each_floor_once() {
            var cache = new Gw2StaticDataCache(_tempDir);
            var fakeApi = new FakeGw2HeroChallengeApi();

            // Populate all maps summary in cache:
            // Two maps in region 12 on floors [1, 49]
            var maps = new[] {
                new CachedMapInfo(1210, "Crystal Oasis", 12, "Crystal Desert", 1, "Tyria",
                                  Array.Empty<string>(), defaultFloor: 1, challengesLoaded: false,
                                  type: "Public", floors: new[] { 1, 49 }),
                new CachedMapInfo(1211, "Desert Highlands", 12, "Crystal Desert", 1, "Tyria",
                                  Array.Empty<string>(), defaultFloor: 1, challengesLoaded: false,
                                  type: "Public", floors: new[] { 1, 49 })
            };
            cache.SaveAllMaps(maps);

            fakeApi.OnFetchFloorRegion = (continentId, floor, regionId) => {
                if (floor == 1) {
                    throw new Exception("Response status code does not indicate success: 404 (Not Found).");
                }
                if (floor == 49) {
                    var dict = new Dictionary<int, IReadOnlyList<string>> {
                        [1210] = new[] { "sc-10", "sc-11", "sc-12", "sc-13", "sc-14" },
                        [1211] = new[] { "sc-20", "sc-21" }
                    };
                    return Task.FromResult<IReadOnlyDictionary<int, IReadOnlyList<string>>>(dict);
                }
                throw new Exception("Unexpected floor: " + floor);
            };

            var resolver = new Gw2HeroChallengeResolver(cache, fakeApi);

            // Act 1: Ensure region maps
            bool success = await resolver.EnsureRegionMapsAsync(1, 12, CancellationToken.None);

            Assert.True(success);
            Assert.True(cache.HasRegion(12));

            // Floor 1 fetched once (both maps were on floor 1)
            // Floor 49 fetched once (both maps advanced to floor 49)
            Assert.Equal(2, fakeApi.FetchedFloorRegions.Count);
            Assert.Equal(1, fakeApi.FetchedFloorRegions[0]);
            Assert.Equal(49, fakeApi.FetchedFloorRegions[1]);

            // Maps cached with their respective challenges and working floor 49
            CachedMapInfo map1210 = cache.GetMap(1210);
            Assert.NotNull(map1210);
            Assert.Equal(5, map1210.SkillChallenges.Count);
            Assert.Equal(49, map1210.WorkingFloor);

            CachedMapInfo map1211 = cache.GetMap(1211);
            Assert.NotNull(map1211);
            Assert.Equal(2, map1211.SkillChallenges.Count);
            Assert.Equal(49, map1211.WorkingFloor);
        }
    }
}
