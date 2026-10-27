using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace jubeater.aigoalplanner.World
{
    /// <summary>
    /// Static Guild Wars 2 data: masteries and maps.
    /// </summary>
    public sealed class CachedMasteryTrack
    {
        public CachedMasteryTrack(int id, string name, string region, int totalLevels) {
            Id          = id;
            Name        = name ?? string.Empty;
            Region      = region ?? string.Empty;
            TotalLevels = totalLevels;
        }

        public int    Id          { get; }
        public string Name        { get; }
        public string Region      { get; }
        public int    TotalLevels { get; }
    }

    /// <summary>
    /// One map with its region and hero challenges (skill challenges).
    /// </summary>
    public sealed class CachedMapInfo
    {
        public CachedMapInfo(int id, string name, int regionId, string regionName,
                             int continentId, string continentName, IReadOnlyList<string> skillChallenges,
                             int defaultFloor = 0, bool challengesLoaded = true) {
            Id               = id;
            Name             = name ?? string.Empty;
            RegionId         = regionId;
            RegionName       = regionName ?? string.Empty;
            ContinentId      = continentId;
            ContinentName    = continentName ?? string.Empty;
            SkillChallenges  = skillChallenges ?? Array.Empty<string>();
            DefaultFloor     = defaultFloor;
            ChallengesLoaded = challengesLoaded;
        }

        public int                   Id               { get; }
        public string                Name             { get; }
        public int                   RegionId         { get; }
        public string                RegionName       { get; }
        public int                   ContinentId      { get; }
        public string                ContinentName    { get; }
        public IReadOnlyList<string> SkillChallenges  { get; }
        public int                   DefaultFloor     { get; }
        public bool                  ChallengesLoaded { get; }
    }

    public interface IGw2StaticDataCache
    {
        IReadOnlyList<CachedMasteryTrack> GetMasteries();
        void SaveMasteries(IReadOnlyList<CachedMasteryTrack> masteries);

        CachedMapInfo GetMap(int mapId);
        void SaveMap(CachedMapInfo map);

        IReadOnlyList<CachedMapInfo> GetAllMaps();
        void SaveAllMaps(IReadOnlyList<CachedMapInfo> maps);
        IReadOnlyList<CachedMapInfo> GetMapsInRegion(int regionId);

        bool HasRegion(int regionId);
        void SaveRegion(int regionId, IReadOnlyList<int> mapIds);

        int GetRegionMissingHeroChallenges(int regionId, ISet<string> completedChallengeIds);
    }

    /// <summary>
    /// Caches static GW2 data (masteries and maps) in the module folder so Lookups answer fast
    /// and work without redundant network calls.
    /// </summary>
    public sealed class Gw2StaticDataCache : IGw2StaticDataCache
    {
        private readonly string         _cacheDir;
        private readonly Action<string> _logWarn;
        private readonly object         _lock = new object();

        private List<CachedMasteryTrack> _masteries;
        private List<CachedMapInfo>      _allMaps;
        private readonly Dictionary<int, CachedMapInfo> _maps = new Dictionary<int, CachedMapInfo>();
        private readonly Dictionary<int, List<int>> _knownRegions = new Dictionary<int, List<int>>();

        public Gw2StaticDataCache(string moduleDirectory, Action<string> logWarn = null) {
            _cacheDir = string.IsNullOrWhiteSpace(moduleDirectory)
                            ? null
                            : Path.Combine(moduleDirectory, "cache");
            _logWarn  = logWarn;
            LoadAllMapsFromDisk();
            LoadAllRegionsFromDisk();
            LoadMapsSummaryFromDisk();
        }

        public IReadOnlyList<CachedMasteryTrack> GetMasteries() {
            lock (_lock) {
                if (_masteries != null) return _masteries;
            }

            IReadOnlyList<CachedMasteryTrack> loaded = LoadMasteriesFromDisk();

            if (loaded != null && loaded.Count > 0) {
                lock (_lock) {
                    _masteries = loaded.ToList();
                    return _masteries;
                }
            }

            return Array.Empty<CachedMasteryTrack>();
        }

        public void SaveMasteries(IReadOnlyList<CachedMasteryTrack> masteries) {
            if (masteries == null) return;

            lock (_lock) {
                _masteries = masteries.ToList();
            }

            SaveMasteriesToDisk(masteries);
        }

        public CachedMapInfo GetMap(int mapId) {
            lock (_lock) {
                if (_maps.TryGetValue(mapId, out CachedMapInfo cached) && cached.ChallengesLoaded) return cached;
            }

            CachedMapInfo fromDisk = LoadMapFromDisk(mapId);

            if (fromDisk != null && fromDisk.ChallengesLoaded) {
                lock (_lock) {
                    _maps[mapId] = fromDisk;
                    return fromDisk;
                }
            }

            return null;
        }

        public void SaveMap(CachedMapInfo map) {
            if (map == null) return;

            if (!map.ChallengesLoaded) {
                // Do not cache incomplete challenges to disk or as completed
                return;
            }

            lock (_lock) {
                _maps[map.Id] = map;
            }

            SaveMapToDisk(map);
        }

        public IReadOnlyList<CachedMapInfo> GetAllMaps() {
            lock (_lock) {
                if (_allMaps != null) return _allMaps;
            }

            IReadOnlyList<CachedMapInfo> loaded = LoadMapsSummaryFromDisk();
            if (loaded != null && loaded.Count > 0) {
                lock (_lock) {
                    _allMaps = loaded.ToList();
                    return _allMaps;
                }
            }

            return Array.Empty<CachedMapInfo>();
        }

        public void SaveAllMaps(IReadOnlyList<CachedMapInfo> maps) {
            if (maps == null) return;

            lock (_lock) {
                _allMaps = maps.ToList();
            }

            SaveMapsSummaryToDisk(maps);
        }

        public IReadOnlyList<CachedMapInfo> GetMapsInRegion(int regionId) {
            if (regionId <= 0) return Array.Empty<CachedMapInfo>();

            IReadOnlyList<CachedMapInfo> all = GetAllMaps();
            if (all == null || all.Count == 0) return Array.Empty<CachedMapInfo>();

            lock (_lock) {
                return all.Where(m => m.RegionId == regionId).ToList();
            }
        }

        public bool HasRegion(int regionId) {
            if (regionId <= 0) return false;

            lock (_lock) {
                if (!_knownRegions.TryGetValue(regionId, out List<int> mapIds)) return false;
                if (mapIds == null || mapIds.Count == 0) return false;

                foreach (int mapId in mapIds) {
                    if (!_maps.TryGetValue(mapId, out CachedMapInfo map) || !map.ChallengesLoaded) {
                        return false;
                    }
                }

                return true;
            }
        }

        public void SaveRegion(int regionId, IReadOnlyList<int> mapIds) {
            if (regionId <= 0 || mapIds == null) return;

            lock (_lock) {
                _knownRegions[regionId] = mapIds.ToList();
            }

            SaveRegionToDisk(regionId, mapIds);
        }

        public int GetRegionMissingHeroChallenges(int regionId, ISet<string> completedChallengeIds) {
            if (regionId <= 0 || completedChallengeIds == null) return 0;

            lock (_lock) {
                int totalMissing = 0;

                foreach (CachedMapInfo map in _maps.Values) {
                    if (map.RegionId == regionId) {
                        int completed = map.SkillChallenges.Count(id => completedChallengeIds.Contains(id));
                        totalMissing += Math.Max(0, map.SkillChallenges.Count - completed);
                    }
                }

                return totalMissing;
            }
        }

        private void LoadAllMapsFromDisk() {
            if (string.IsNullOrEmpty(_cacheDir) || !Directory.Exists(_cacheDir)) return;

            try {
                string[] files = Directory.GetFiles(_cacheDir, "map_*.json");
                foreach (string file in files) {
                    try {
                        string json = System.IO.File.ReadAllText(file);
                        CachedMapInfo map = ParseMapJson(json);
                        if (map != null && map.ChallengesLoaded) {
                            lock (_lock) {
                                _maps[map.Id] = map;
                            }
                        }
                    } catch (Exception ex) {
                        _logWarn?.Invoke("Failed to load map cache from " + Path.GetFileName(file) + ": " + ex.GetType().Name);
                    }
                }
            } catch (Exception ex) {
                _logWarn?.Invoke("Failed to enumerate map cache files: " + ex.GetType().Name);
            }
        }

        private void LoadAllRegionsFromDisk() {
            if (string.IsNullOrEmpty(_cacheDir) || !Directory.Exists(_cacheDir)) return;

            try {
                string[] files = Directory.GetFiles(_cacheDir, "region_*.json");
                foreach (string file in files) {
                    try {
                        string json = System.IO.File.ReadAllText(file);
                        JObject obj = JObject.Parse(json);
                        int regionId = obj.Value<int>("region_id");
                        JArray mapIdsArray = obj.Value<JArray>("map_ids");
                        if (regionId > 0 && mapIdsArray != null) {
                            List<int> mapIds = mapIdsArray.Select(t => (int)t).ToList();
                            lock (_lock) {
                                _knownRegions[regionId] = mapIds;
                            }
                        }
                    } catch (Exception ex) {
                        _logWarn?.Invoke("Failed to load region cache from " + Path.GetFileName(file) + ": " + ex.GetType().Name);
                    }
                }
            } catch (Exception ex) {
                _logWarn?.Invoke("Failed to enumerate region cache files: " + ex.GetType().Name);
            }
        }

        private IReadOnlyList<CachedMapInfo> LoadMapsSummaryFromDisk() {
            if (string.IsNullOrEmpty(_cacheDir)) return null;

            string filePath = Path.Combine(_cacheDir, "maps.json");
            if (!System.IO.File.Exists(filePath)) return null;

            try {
                string json = System.IO.File.ReadAllText(filePath);
                JArray array = JArray.Parse(json);
                var list = new List<CachedMapInfo>();

                foreach (JObject item in array.OfType<JObject>()) {
                    int id = item.Value<int>("id");
                    string name = item.Value<string>("name");
                    int regionId = item.Value<int>("region_id");
                    string regionName = item.Value<string>("region_name");
                    int continentId = item.Value<int>("continent_id");
                    string continentName = item.Value<string>("continent_name");
                    int defaultFloor = item.Value<int>("default_floor");

                    list.Add(new CachedMapInfo(id, name, regionId, regionName, continentId, continentName,
                                               Array.Empty<string>(), defaultFloor, challengesLoaded: false));
                }

                lock (_lock) {
                    _allMaps = list.ToList();
                }

                return list;
            } catch (Exception ex) {
                _logWarn?.Invoke("Failed to load maps cache: " + ex.GetType().Name);
                return null;
            }
        }

        private void SaveMapsSummaryToDisk(IReadOnlyList<CachedMapInfo> maps) {
            if (string.IsNullOrEmpty(_cacheDir) || maps == null) return;

            try {
                Directory.CreateDirectory(_cacheDir);
                string filePath = Path.Combine(_cacheDir, "maps.json");

                var array = new JArray(maps.Select(m => new JObject {
                    ["id"]             = m.Id,
                    ["name"]           = m.Name,
                    ["region_id"]      = m.RegionId,
                    ["region_name"]    = m.RegionName,
                    ["continent_id"]   = m.ContinentId,
                    ["continent_name"] = m.ContinentName,
                    ["default_floor"]  = m.DefaultFloor
                }));

                System.IO.File.WriteAllText(filePath, array.ToString(Formatting.None));
            } catch (Exception ex) {
                _logWarn?.Invoke("Failed to save maps cache: " + ex.GetType().Name);
            }
        }

        private static CachedMapInfo ParseMapJson(string json) {
            JObject item = JObject.Parse(json);

            int id = item.Value<int>("id");
            string name = item.Value<string>("name");
            int regionId = item.Value<int>("region_id");
            string regionName = item.Value<string>("region_name");
            int continentId = item.Value<int>("continent_id");
            string continentName = item.Value<string>("continent_name");
            int defaultFloor = item.Value<int>("default_floor");
            bool challengesLoaded = item["challenges_loaded"] != null
                                        ? item.Value<bool>("challenges_loaded")
                                        : true;

            JArray challenges = item.Value<JArray>("skill_challenges");
            List<string> scList = challenges != null ? challenges.Select(c => (string)c).ToList() : new List<string>();

            return new CachedMapInfo(id, name, regionId, regionName, continentId, continentName, scList, defaultFloor, challengesLoaded);
        }

        private IReadOnlyList<CachedMasteryTrack> LoadMasteriesFromDisk() {
            if (string.IsNullOrEmpty(_cacheDir)) return null;

            string filePath = Path.Combine(_cacheDir, "masteries.json");

            if (!System.IO.File.Exists(filePath)) return null;

            try {
                string json = System.IO.File.ReadAllText(filePath);
                JArray array = JArray.Parse(json);
                var list = new List<CachedMasteryTrack>();

                foreach (JObject item in array.OfType<JObject>()) {
                    int id = item.Value<int>("id");
                    string name = item.Value<string>("name");
                    string region = item.Value<string>("region");
                    int totalLevels = item.Value<int>("total_levels");
                    list.Add(new CachedMasteryTrack(id, name, region, totalLevels));
                }

                return list;
            } catch (Exception ex) {
                _logWarn?.Invoke("Failed to load masteries cache: " + ex.GetType().Name);
                return null;
            }
        }

        private void SaveMasteriesToDisk(IReadOnlyList<CachedMasteryTrack> masteries) {
            if (string.IsNullOrEmpty(_cacheDir) || masteries == null) return;

            try {
                Directory.CreateDirectory(_cacheDir);
                string filePath = Path.Combine(_cacheDir, "masteries.json");

                var array = new JArray(masteries.Select(m => new JObject {
                    ["id"]           = m.Id,
                    ["name"]         = m.Name,
                    ["region"]       = m.Region,
                    ["total_levels"] = m.TotalLevels
                }));

                System.IO.File.WriteAllText(filePath, array.ToString(Formatting.None));
            } catch (Exception ex) {
                _logWarn?.Invoke("Failed to save masteries cache: " + ex.GetType().Name);
            }
        }

        private CachedMapInfo LoadMapFromDisk(int mapId) {
            if (string.IsNullOrEmpty(_cacheDir)) return null;

            string filePath = Path.Combine(_cacheDir, "map_" + mapId + ".json");

            if (!System.IO.File.Exists(filePath)) return null;

            try {
                string json = System.IO.File.ReadAllText(filePath);
                return ParseMapJson(json);
            } catch (Exception ex) {
                _logWarn?.Invoke("Failed to load map cache for map " + mapId + ": " + ex.GetType().Name);
                return null;
            }
        }

        private void SaveMapToDisk(CachedMapInfo map) {
            if (string.IsNullOrEmpty(_cacheDir) || map == null) return;

            try {
                Directory.CreateDirectory(_cacheDir);
                string filePath = Path.Combine(_cacheDir, "map_" + map.Id + ".json");

                var obj = new JObject {
                    ["id"]                = map.Id,
                    ["name"]              = map.Name,
                    ["region_id"]         = map.RegionId,
                    ["region_name"]       = map.RegionName,
                    ["continent_id"]      = map.ContinentId,
                    ["continent_name"]    = map.ContinentName,
                    ["default_floor"]     = map.DefaultFloor,
                    ["challenges_loaded"] = map.ChallengesLoaded,
                    ["skill_challenges"]  = new JArray(map.SkillChallenges)
                };

                System.IO.File.WriteAllText(filePath, obj.ToString(Formatting.None));
            } catch (Exception ex) {
                _logWarn?.Invoke("Failed to save map cache for map " + map.Id + ": " + ex.GetType().Name);
            }
        }

        private void SaveRegionToDisk(int regionId, IReadOnlyList<int> mapIds) {
            if (string.IsNullOrEmpty(_cacheDir) || mapIds == null) return;

            try {
                Directory.CreateDirectory(_cacheDir);
                string filePath = Path.Combine(_cacheDir, "region_" + regionId + ".json");

                var obj = new JObject {
                    ["region_id"] = regionId,
                    ["map_ids"]   = new JArray(mapIds)
                };

                System.IO.File.WriteAllText(filePath, obj.ToString(Formatting.None));
            } catch (Exception ex) {
                _logWarn?.Invoke("Failed to save region cache for region " + regionId + ": " + ex.GetType().Name);
            }
        }
    }
}
