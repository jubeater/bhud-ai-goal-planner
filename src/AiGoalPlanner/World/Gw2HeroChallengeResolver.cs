using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace jubeater.aigoalplanner.World
{
    public interface IGw2HeroChallengeApi
    {
        Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> FetchFloorRegionChallengesAsync(int continentId, int floor, int regionId, CancellationToken cancellationToken);
        Task<IReadOnlyList<string>> FetchMapChallengesAsync(int continentId, int floor, int regionId, int mapId, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Resolves hero challenge (skill challenge) lists for maps and regions across multiple floors,
    /// retrying candidate floors when a floor returns 404, caching 0 hero challenges only when all
    /// candidate floors return 404, and leaving incomplete on transient errors.
    /// </summary>
    public sealed class Gw2HeroChallengeResolver
    {
        private readonly IGw2StaticDataCache  _cache;
        private readonly IGw2HeroChallengeApi _api;
        private readonly Action<string>       _logWarn;

        public Gw2HeroChallengeResolver(IGw2StaticDataCache cache, IGw2HeroChallengeApi api, Action<string> logWarn = null) {
            _cache   = cache;
            _api     = api;
            _logWarn = logWarn;
        }

        public async Task<CachedMapInfo> ResolveMapAsync(CachedMapInfo map, CancellationToken cancellationToken) {
            if (map == null) return null;

            CachedMapInfo cached = _cache?.GetMap(map.Id);
            if (cached != null && cached.ChallengesLoaded) return cached;

            if (_api == null) return map;

            var candidateFloors = (cached?.GetCandidateFloors() ?? map.GetCandidateFloors()).ToList();
            if (candidateFloors.Count == 0) {
                candidateFloors.Add(map.DefaultFloor);
            }

            bool challengesLoaded = false;
            IReadOnlyList<string> skillChallenges = Array.Empty<string>();
            int? foundWorkingFloor = null;
            bool anyTransientError = false;
            var notFoundFloors = new HashSet<int>();

            foreach (int floor in candidateFloors) {
                try {
                    IReadOnlyList<string> sc = await _api.FetchMapChallengesAsync(map.ContinentId, floor, map.RegionId, map.Id, cancellationToken).ConfigureAwait(false);
                    skillChallenges = sc ?? Array.Empty<string>();
                    challengesLoaded = true;
                    foundWorkingFloor = floor;
                    break;
                } catch (Exception ex) {
                    if (Gw2ApiExceptionHelper.IsNotFound(ex)) {
                        notFoundFloors.Add(floor);
                    } else {
                        _logWarn?.Invoke("Could not fetch floor map for map " + map.Id + " floor " + floor + " from GW2 API: " + ex.GetType().Name);
                        anyTransientError = true;
                        break;
                    }
                }
            }

            if (!challengesLoaded && !anyTransientError && candidateFloors.Count > 0 && candidateFloors.All(f => notFoundFloors.Contains(f))) {
                challengesLoaded = true;
                skillChallenges = Array.Empty<string>();
                foundWorkingFloor = map.DefaultFloor;
            }

            var info = new CachedMapInfo(
                map.Id,
                map.Name,
                map.RegionId,
                map.RegionName,
                map.ContinentId,
                map.ContinentName,
                skillChallenges,
                map.DefaultFloor,
                challengesLoaded,
                type: map.Type,
                floors: map.Floors,
                workingFloor: foundWorkingFloor);

            if (challengesLoaded) {
                _cache?.SaveMap(info);
            }

            return info;
        }

        public async Task<bool> EnsureRegionMapsAsync(int continentId, int regionId, CancellationToken cancellationToken) {
            if (continentId <= 0 || regionId <= 0 || _api == null) return false;

            IReadOnlyList<CachedMapInfo> regionMaps = _cache?.GetMapsInRegion(regionId) ?? Array.Empty<CachedMapInfo>();
            if (regionMaps.Count == 0) return false;

            var states = new List<MapResolutionState>();
            foreach (CachedMapInfo m in regionMaps) {
                CachedMapInfo cached = _cache?.GetMap(m.Id);
                if (cached != null && cached.ChallengesLoaded) {
                    continue;
                }

                var candidateFloors = (cached?.GetCandidateFloors() ?? m.GetCandidateFloors()).ToList();
                if (candidateFloors.Count == 0) {
                    candidateFloors.Add(m.DefaultFloor);
                }

                states.Add(new MapResolutionState(m, candidateFloors));
            }

            if (states.Count == 0) {
                _cache?.SaveRegion(regionId, regionMaps.Select(m => m.Id).ToList());
                return true;
            }

            var fetchedFloors = new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyList<string>>>();
            var notFoundFloors = new HashSet<int>();
            var errorFloors = new HashSet<int>();

            while (states.Any(s => !s.Resolved && !s.HasTransientError && s.UntriedFloors.Count > 0)) {
                var activeStates = states.Where(s => !s.Resolved && !s.HasTransientError && s.UntriedFloors.Count > 0).ToList();

                var groups = activeStates.GroupBy(s => s.UntriedFloors[0]).ToList();

                foreach (var group in groups) {
                    int floor = group.Key;

                    if (!fetchedFloors.ContainsKey(floor) && !notFoundFloors.Contains(floor) && !errorFloors.Contains(floor)) {
                        try {
                            IReadOnlyDictionary<int, IReadOnlyList<string>> dict = await _api.FetchFloorRegionChallengesAsync(continentId, floor, regionId, cancellationToken).ConfigureAwait(false);
                            fetchedFloors[floor] = dict ?? new Dictionary<int, IReadOnlyList<string>>();
                        } catch (Exception ex) {
                            if (Gw2ApiExceptionHelper.IsNotFound(ex)) {
                                notFoundFloors.Add(floor);
                            } else {
                                _logWarn?.Invoke("Could not fetch floor " + floor + " region " + regionId + " from GW2 API: " + ex.GetType().Name);
                                errorFloors.Add(floor);
                            }
                        }
                    }

                    foreach (var s in group) {
                        s.UntriedFloors.Remove(floor);

                        if (errorFloors.Contains(floor)) {
                            s.HasTransientError = true;
                        } else if (notFoundFloors.Contains(floor)) {
                            s.NotFoundFloors.Add(floor);
                        } else if (fetchedFloors.TryGetValue(floor, out var mapsDict)) {
                            if (mapsDict.TryGetValue(s.MapInfo.Id, out var challenges)) {
                                var resolved = new CachedMapInfo(
                                    s.MapInfo.Id,
                                    s.MapInfo.Name,
                                    regionId,
                                    s.MapInfo.RegionName,
                                    continentId,
                                    s.MapInfo.ContinentName,
                                    challenges ?? Array.Empty<string>(),
                                    s.MapInfo.DefaultFloor,
                                    challengesLoaded: true,
                                    type: s.MapInfo.Type,
                                    floors: s.MapInfo.Floors,
                                    workingFloor: floor);

                                _cache?.SaveMap(resolved);
                                s.Resolved = true;
                            } else {
                                s.NotFoundFloors.Add(floor);
                            }
                        }
                    }
                }

                foreach (var s in states.Where(st => !st.Resolved && !st.HasTransientError)) {
                    if (s.CandidateFloors.Count > 0 && s.CandidateFloors.All(f => s.NotFoundFloors.Contains(f))) {
                        var resolved = new CachedMapInfo(
                            s.MapInfo.Id,
                            s.MapInfo.Name,
                            regionId,
                            s.MapInfo.RegionName,
                            continentId,
                            s.MapInfo.ContinentName,
                            Array.Empty<string>(),
                            s.MapInfo.DefaultFloor,
                            challengesLoaded: true,
                            type: s.MapInfo.Type,
                            floors: s.MapInfo.Floors,
                            workingFloor: s.MapInfo.DefaultFloor);

                        _cache?.SaveMap(resolved);
                        s.Resolved = true;
                    }
                }
            }

            bool allLoaded = true;
            foreach (CachedMapInfo m in regionMaps) {
                CachedMapInfo loaded = _cache?.GetMap(m.Id);
                if (loaded == null || !loaded.ChallengesLoaded) {
                    allLoaded = false;
                    break;
                }
            }

            if (allLoaded) {
                _cache?.SaveRegion(regionId, regionMaps.Select(m => m.Id).ToList());
                return true;
            }

            return false;
        }

        private sealed class MapResolutionState
        {
            public MapResolutionState(CachedMapInfo mapInfo, List<int> candidateFloors) {
                MapInfo         = mapInfo;
                CandidateFloors = candidateFloors;
                UntriedFloors   = new List<int>(candidateFloors);
            }

            public CachedMapInfo MapInfo         { get; }
            public List<int>     CandidateFloors { get; }
            public List<int>     UntriedFloors   { get; }
            public HashSet<int>  NotFoundFloors  { get; } = new HashSet<int>();
            public bool          HasTransientError { get; set; }
            public bool          Resolved          { get; set; }
        }
    }
}
