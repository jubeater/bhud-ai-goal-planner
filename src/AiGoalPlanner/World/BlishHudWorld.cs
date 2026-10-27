using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Modules;
using Blish_HUD.Modules.Managers;
using Blish_HUD.Settings;
using Gw2Sharp.WebApi.V2;
using Gw2Sharp.WebApi.V2.Models;
using jubeater.aigoalplanner.Core;

namespace jubeater.aigoalplanner.World
{
    /// <summary>
    /// The real Blish HUD host, as the Lookups see it: the modules registered with Blish HUD and the
    /// settings of the ones that are enabled, plus Guild Wars 2 location, Owned Content, and progress.
    /// </summary>
    /// <remarks>
    /// The Lookups run while the model thinks, on a thread that is not the game's update thread, so
    /// this is never called from there: <see cref="GameThreadWorld"/> queues each read on the game
    /// thread first, because Blish HUD and the Modules expect their state to be read on it. Reading
    /// touches no control, so it changes nothing the Player can see; it only has to happen on the
    /// right thread.
    /// </remarks>
    public sealed partial class BlishHudWorld : IBlishHudWorld
    {
        private static readonly IReadOnlyList<ModuleSettingInfo> NoSettings = new ModuleSettingInfo[0];

        /// <summary>
        /// The module instance's own parameters. The module infrastructure keeps them protected, and
        /// the settings live behind them: this is the one way in from the outside.
        /// </summary>
        private static readonly FieldInfo ModuleParametersField = typeof(Blish_HUD.Modules.Module).GetField("ModuleParameters",
                                                                                                            BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly Gw2ApiManager       _gw2ApiManager;
        private readonly IGw2StaticDataCache _cache;
        private readonly Action<string>           _logWarn;
        private readonly IGw2HeroChallengeApi     _challengeApi;
        private readonly Gw2HeroChallengeResolver _resolver;

        public BlishHudWorld() : this(null, null, null, null) { }

        public BlishHudWorld(Gw2ApiManager gw2ApiManager, IGw2StaticDataCache cache = null, Action<string> logWarn = null, IGw2HeroChallengeApi challengeApi = null) {
            _gw2ApiManager = gw2ApiManager;
            _cache         = cache;
            _logWarn       = logWarn;
            _challengeApi  = challengeApi ?? (gw2ApiManager != null ? new Gw2SharpHeroChallengeApi(gw2ApiManager) : null);
            if (_cache != null && _challengeApi != null) {
                _resolver = new Gw2HeroChallengeResolver(_cache, _challengeApi, _logWarn);
            }
        }

        public Task<IReadOnlyList<InstalledModuleInfo>> ListInstalledModulesAsync(CancellationToken cancellationToken) {
            var modules = new List<InstalledModuleInfo>();

            foreach (ModuleManager manager in GameService.Module.Modules) {
                modules.Add(new InstalledModuleInfo(manager.Manifest.Namespace,
                                                    manager.Manifest.Name,
                                                    VersionTextOf(manager.Manifest),
                                                    manager.Manifest.Description ?? string.Empty,
                                                    manager.Enabled));
            }

            return Task.FromResult<IReadOnlyList<InstalledModuleInfo>>(modules);
        }

        public Task<ModuleSettingsResult> GetModuleSettingsAsync(string @namespace, CancellationToken cancellationToken) {
            string wanted = (@namespace ?? string.Empty).Trim();

            // The Lookup refuses this module's own settings already; the world refuses them too, so
            // they stay private even if a second reader ever skips the Lookup.
            if (string.Equals(wanted, GetModuleSettingsTool.ThisModuleNamespace, StringComparison.OrdinalIgnoreCase)) {
                return Task.FromResult(new ModuleSettingsResult(ModuleSettingsStatus.ThisModule, "AI Goal Planner", NoSettings,
                                                                GetModuleSettingsTool.ThisModuleNamespace));
            }

            ModuleManager manager = GameService.Module.Modules
                                                    .FirstOrDefault(candidate =>
                                                                        string.Equals(candidate.Manifest.Namespace, wanted, StringComparison.OrdinalIgnoreCase));

            if (manager == null) {
                return Task.FromResult(new ModuleSettingsResult(ModuleSettingsStatus.ModuleNotInstalled, wanted, NoSettings));
            }

            // A disabled module has no running instance: Blish HUD tears ModuleInstance down when a
            // module is disabled, and builds its settings manager only when it is enabled. Both
            // states answer "enable it first" — the Enabled check has to come first, because there
            // is nothing to reflect on for a module that is not running.
            if (!manager.Enabled) {
                return Task.FromResult(new ModuleSettingsResult(ModuleSettingsStatus.ModuleDisabled, manager.Manifest.Name, NoSettings,
                                                                manager.Manifest.Namespace));
            }

            SettingCollection settings = SettingsOf(manager);

            if (settings == null) {
                // Enabled, but its instance is not running yet (still loading), so its settings are
                // not readable either. The same answer applies until it is.
                return Task.FromResult(new ModuleSettingsResult(ModuleSettingsStatus.ModuleDisabled, manager.Manifest.Name, NoSettings,
                                                                manager.Manifest.Namespace));
            }

            var entries = new List<ModuleSettingInfo>();

            foreach (SettingEntry entry in settings.Entries) {
                ModuleSettingInfo info = ToSettingInfo(entry);

                if (info != null) entries.Add(info);
            }

            return Task.FromResult(new ModuleSettingsResult(ModuleSettingsStatus.Ok, manager.Manifest.Name, entries,
                                                            manager.Manifest.Namespace));
        }

        public async Task<PlayerLocationResult> GetLocationAsync(CancellationToken cancellationToken) {
            string characterName = GameService.Gw2Mumble.PlayerCharacter?.Name;
            int mapId = GameService.Gw2Mumble.CurrentMap?.Id ?? 0;
            string mumbleMapName = GameService.Gw2Mumble.CurrentMumbleMapName;

            if (mapId <= 0 || string.IsNullOrEmpty(characterName)) {
                return new PlayerLocationResult(LocationStatus.Unavailable, null, "The Player is not in game or character selection is active.");
            }

            CachedMapInfo mapInfo = await GetMapAsync(mapId, cancellationToken).ConfigureAwait(false);

            string mapName = mapInfo?.Name;
            if (string.IsNullOrEmpty(mapName)) {
                mapName = mumbleMapName;
            }
            if (string.IsNullOrEmpty(mapName)) {
                mapName = "Map " + mapId;
            }

            int regionId = mapInfo?.RegionId ?? 0;
            string regionName = mapInfo?.RegionName ?? string.Empty;

            var location = new PlayerLocation(characterName, mapId, mapName, regionId, regionName);
            return new PlayerLocationResult(LocationStatus.Ok, location);
        }

        public async Task<AccountAccessResult> GetAccountAccessAsync(CancellationToken cancellationToken) {
            if (_gw2ApiManager == null || !_gw2ApiManager.HasSubtoken) {
                _logWarn?.Invoke("Guild Wars 2 API access has not been granted by Blish HUD; account access lookup is unavailable.");
                return new AccountAccessResult(AccountAccessStatus.NoApiAccess, Array.Empty<string>(),
                                               "Blish HUD hasn't given AI Goal Planner API access yet. Make sure a Guild Wars 2 API key is added in Blish HUD, then restart Blish HUD.");
            }

            if (!_gw2ApiManager.HasPermission(TokenPermission.Account)) {
                _logWarn?.Invoke("Guild Wars 2 API key lacks 'account' permission; account access lookup is unavailable.");
                return new AccountAccessResult(AccountAccessStatus.MissingPermission, Array.Empty<string>(),
                                               "Guild Wars 2 API key lacks the 'account' permission.", "account");
            }

            try {
                var account = await _gw2ApiManager.Gw2ApiClient.V2.Account.GetAsync(cancellationToken).ConfigureAwait(false);
                var accessList = new List<string>();

                if (account?.Access?.List != null) {
                    foreach (var item in account.Access.List) {
                        if (!string.IsNullOrEmpty(item.RawValue)) {
                            accessList.Add(item.RawValue);
                        }
                    }
                }

                return new AccountAccessResult(AccountAccessStatus.Ok, accessList.Distinct().ToList());
            } catch (Exception ex) {
                _logWarn?.Invoke("GW2 API account call failed: " + ex.GetType().Name);
                return new AccountAccessResult(AccountAccessStatus.Unavailable, Array.Empty<string>(),
                                               "Could not retrieve account access from Guild Wars 2 API.");
            }
        }

        public async Task<ProgressResult> GetProgressAsync(ProgressKind kind, CancellationToken cancellationToken) {
            if (kind == ProgressKind.Mastery) {
                if (_gw2ApiManager == null || !_gw2ApiManager.HasSubtoken) {
                    _logWarn?.Invoke("Guild Wars 2 API access has not been granted by Blish HUD; mastery progress lookup is unavailable.");
                    return new ProgressResult(ProgressStatus.NoApiAccess, kind,
                                              message: "Blish HUD hasn't given AI Goal Planner API access yet. Make sure a Guild Wars 2 API key is added in Blish HUD, then restart Blish HUD.");
                }

                if (!_gw2ApiManager.HasPermission(TokenPermission.Progression)) {
                    _logWarn?.Invoke("Guild Wars 2 API key lacks 'progression' permission; mastery progress lookup is unavailable.");
                    return new ProgressResult(ProgressStatus.MissingPermission, kind, message: "Guild Wars 2 API key lacks the 'progression' permission.", missingPermission: "progression");
                }

                try {
                    var pointsTask = _gw2ApiManager.Gw2ApiClient.V2.Account.Mastery.Points.GetAsync(cancellationToken);
                    var accountMasteriesTask = _gw2ApiManager.Gw2ApiClient.V2.Account.Masteries.GetAsync(cancellationToken);
                    var masteriesTask = GetMasteriesAsync(cancellationToken);

                    await Task.WhenAll(pointsTask, accountMasteriesTask, masteriesTask).ConfigureAwait(false);

                    var points = await pointsTask.ConfigureAwait(false);
                    var accountMasteries = await accountMasteriesTask.ConfigureAwait(false);
                    var masteries = await masteriesTask.ConfigureAwait(false);

                    bool tracksAvailable = masteries != null;

                    var accountMasteryMap = (accountMasteries ?? Enumerable.Empty<AccountMastery>())
                        .ToDictionary(m => m.Id, m => m.Level);

                    Dictionary<string, List<CachedMasteryTrack>> masteriesByRegion = null;
                    if (tracksAvailable) {
                        masteriesByRegion = masteries
                            .GroupBy(m => MasteryProgressCalculator.NormalizeRegionName(m.Region), StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
                    }

                    var regions = new List<MasteryRegionProgress>();

                    if (points?.Totals != null) {
                        foreach (AccountMasteryPointsRegion regionTotal in points.Totals) {
                            string regionName = regionTotal.Region;
                            string canonicalRegion = MasteryProgressCalculator.NormalizeRegionName(regionName);
                            List<MasteryTrackProgress> unfinished = null;
                            bool regionTracksAvailable = false;

                            if (tracksAvailable && masteriesByRegion != null &&
                                masteriesByRegion.TryGetValue(canonicalRegion, out List<CachedMasteryTrack> tracks) &&
                                tracks != null && tracks.Count > 0) {
                                regionTracksAvailable = true;
                                unfinished = new List<MasteryTrackProgress>(
                                    MasteryProgressCalculator.CalculateUnfinishedTracks(tracks, accountMasteryMap));
                            }

                            regions.Add(new MasteryRegionProgress(regionName, regionTotal.Spent, regionTotal.Earned, unfinished, regionTracksAvailable));
                        }
                    }

                    return new ProgressResult(ProgressStatus.Ok, kind, masteryRegions: regions);
                } catch (Exception ex) {
                    _logWarn?.Invoke("GW2 API mastery progress call failed: " + ex.GetType().Name);
                    return new ProgressResult(ProgressStatus.Unavailable, kind, message: "Could not retrieve mastery progress from Guild Wars 2 API.");
                }
            }

            if (kind == ProgressKind.HeroPoints) {
                if (_gw2ApiManager == null || !_gw2ApiManager.HasSubtoken) {
                    _logWarn?.Invoke("Guild Wars 2 API access has not been granted by Blish HUD; hero points progress lookup is unavailable.");
                    return new ProgressResult(ProgressStatus.NoApiAccess, kind,
                                              message: "Blish HUD hasn't given AI Goal Planner API access yet. Make sure a Guild Wars 2 API key is added in Blish HUD, then restart Blish HUD.");
                }

                if (!_gw2ApiManager.HasPermission(TokenPermission.Characters)) {
                    _logWarn?.Invoke("Guild Wars 2 API key lacks 'characters' permission; hero points progress lookup is unavailable.");
                    return new ProgressResult(ProgressStatus.MissingPermission, kind, message: "Guild Wars 2 API key lacks the 'characters' permission.", missingPermission: "characters");
                }

                if (!_gw2ApiManager.HasPermission(TokenPermission.Progression)) {
                    _logWarn?.Invoke("Guild Wars 2 API key lacks 'progression' permission; hero points progress lookup is unavailable.");
                    return new ProgressResult(ProgressStatus.MissingPermission, kind, message: "Guild Wars 2 API key lacks the 'progression' permission.", missingPermission: "progression");
                }

                string characterName = GameService.Gw2Mumble.PlayerCharacter?.Name;
                int mapId = GameService.Gw2Mumble.CurrentMap?.Id ?? 0;
                string mumbleMapName = GameService.Gw2Mumble.CurrentMumbleMapName;

                if (string.IsNullOrEmpty(characterName) || mapId <= 0) {
                    return new ProgressResult(ProgressStatus.NoCharacterOrMap, kind, message: "The Player is not currently on a map in Guild Wars 2.");
                }

                try {
                    var pointsTask = _gw2ApiManager.Gw2ApiClient.V2.Characters[characterName].HeroPoints.GetAsync(cancellationToken);
                    var mapTask = GetMapAsync(mapId, cancellationToken);

                    await Task.WhenAll(pointsTask, mapTask).ConfigureAwait(false);

                    var completedPoints = await pointsTask.ConfigureAwait(false);
                    CachedMapInfo mapInfo = await mapTask.ConfigureAwait(false);

                    string mapName = mapInfo?.Name ?? mumbleMapName ?? ("Map " + mapId);
                    string regionName = mapInfo?.RegionName ?? string.Empty;

                    if (completedPoints == null || completedPoints.Count == 0) {
                        return new ProgressResult(
                            ProgressStatus.Unknown,
                            kind,
                            characterName: characterName,
                            mapId: mapId,
                            mapName: mapName,
                            regionName: regionName,
                            message: "This is a known Guild Wars 2 API issue where hero-point data is missing for some characters. The Player can check the in-game Hero panel.");
                    }

                    var completedSet = new HashSet<string>(completedPoints, StringComparer.OrdinalIgnoreCase);

                    int continentId = mapInfo?.ContinentId ?? 0;
                    int regionId = mapInfo?.RegionId ?? 0;

                    if (regionId <= 0 && _cache != null) {
                        var allMaps = await GetAllMapsAsync(cancellationToken).ConfigureAwait(false);
                        var summary = allMaps?.FirstOrDefault(m => m.Id == mapId);
                        if (summary != null) {
                            continentId = summary.ContinentId;
                            regionId = summary.RegionId;
                            if (string.IsNullOrEmpty(regionName)) regionName = summary.RegionName;
                            if (string.IsNullOrEmpty(mapName) || mapName == "Map " + mapId) mapName = summary.Name;
                        }
                    }

                    bool regionIncomplete = false;
                    int regionMissing = 0;

                    if (_cache != null && regionId > 0 && continentId > 0) {
                        if (!_cache.HasRegion(regionId)) {
                            bool regionLoaded = await EnsureRegionMapsAsync(continentId, regionId, cancellationToken).ConfigureAwait(false);
                            if (!regionLoaded) {
                                regionIncomplete = true;
                            }
                        }

                        if (!regionIncomplete) {
                            regionMissing = _cache.GetRegionMissingHeroChallenges(regionId, completedSet);
                        }
                    } else {
                        regionIncomplete = true;
                    }

                    CachedMapInfo reloaded = _cache?.GetMap(mapId);
                    if (reloaded != null && reloaded.ChallengesLoaded) {
                        mapInfo = reloaded;
                        if (string.IsNullOrEmpty(mapName) || mapName == "Map " + mapId) mapName = mapInfo.Name;
                        if (string.IsNullOrEmpty(regionName)) regionName = mapInfo.RegionName;
                    }

                    if (mapInfo == null || !mapInfo.ChallengesLoaded) {
                        return new ProgressResult(
                            ProgressStatus.Unknown,
                            kind,
                            characterName: characterName,
                            mapId: mapId,
                            mapName: mapName,
                            regionName: regionName,
                            message: "Hero challenge data for this map could not be loaded.");
                    }

                    int mapTotal = mapInfo.SkillChallenges.Count;
                    int mapCompleted = mapInfo.SkillChallenges.Count(id => completedSet.Contains(id));

                    var heroChallenges = new HeroChallengeProgress(mapTotal, mapCompleted, regionIncomplete ? (int?)null : regionMissing, regionIncomplete);

                    return new ProgressResult(ProgressStatus.Ok, kind, heroChallenges: heroChallenges,
                                              characterName: characterName, mapId: mapId, mapName: mapName, regionName: regionName);
                } catch (Exception ex) {
                    _logWarn?.Invoke("GW2 API hero points progress call failed: " + ex.GetType().Name);
                    return new ProgressResult(ProgressStatus.Unavailable, kind, message: "Could not retrieve hero points progress from Guild Wars 2 API.");
                }
            }

            return new ProgressResult(ProgressStatus.Unavailable, kind, message: "Unknown progress kind.");
        }

        private async Task<IReadOnlyList<CachedMasteryTrack>> GetMasteriesAsync(CancellationToken cancellationToken) {
            IReadOnlyList<CachedMasteryTrack> cached = _cache?.GetMasteries();
            if (cached != null && cached.Count > 0) return cached;

            if (_gw2ApiManager?.Gw2ApiClient == null) return null;

            try {
                IApiV2ObjectList<Mastery> all = await _gw2ApiManager.Gw2ApiClient.V2.Masteries.AllAsync(cancellationToken).ConfigureAwait(false);
                var list = new List<CachedMasteryTrack>();
                foreach (Mastery m in all) {
                    list.Add(new CachedMasteryTrack(m.Id, m.Name, m.Region, m.Levels?.Count ?? 0));
                }
                _cache?.SaveMasteries(list);
                return list;
            } catch (Exception ex) {
                _logWarn?.Invoke("Could not fetch masteries from GW2 API: " + ex.GetType().Name);
                return null;
            }
        }

        private async Task<IReadOnlyList<CachedMapInfo>> GetAllMapsAsync(CancellationToken cancellationToken) {
            IReadOnlyList<CachedMapInfo> cached = _cache?.GetAllMaps();
            if (cached != null && cached.Count > 0) return cached;

            if (_gw2ApiManager?.Gw2ApiClient == null) return Array.Empty<CachedMapInfo>();

            try {
                IApiV2ObjectList<Gw2Sharp.WebApi.V2.Models.Map> all = await _gw2ApiManager.Gw2ApiClient.V2.Maps.AllAsync(cancellationToken).ConfigureAwait(false);
                var list = new List<CachedMapInfo>();
                foreach (Gw2Sharp.WebApi.V2.Models.Map m in all) {
                    string mapType = m.Type?.RawValue ?? m.Type?.Value.ToString() ?? string.Empty;
                    list.Add(new CachedMapInfo(m.Id, m.Name, m.RegionId, m.RegionName, m.ContinentId, m.ContinentName, Array.Empty<string>(), m.DefaultFloor, challengesLoaded: false, type: mapType, floors: m.Floors));
                }
                _cache?.SaveAllMaps(list);
                return list;
            } catch (Exception ex) {
                _logWarn?.Invoke("Could not fetch maps summary from GW2 API: " + ex.GetType().Name);
                return Array.Empty<CachedMapInfo>();
            }
        }

        private async Task<CachedMapInfo> GetMapAsync(int mapId, CancellationToken cancellationToken) {
            CachedMapInfo cached = _cache?.GetMap(mapId);
            if (cached != null && cached.ChallengesLoaded) return cached;

            if (_gw2ApiManager?.Gw2ApiClient == null) return null;

            try {
                Gw2Sharp.WebApi.V2.Models.Map map = await _gw2ApiManager.Gw2ApiClient.V2.Maps.GetAsync(mapId, cancellationToken).ConfigureAwait(false);
                if (map == null) return null;

                string mapType = map.Type?.RawValue ?? map.Type?.Value.ToString() ?? string.Empty;
                var initial = new CachedMapInfo(
                    map.Id,
                    map.Name,
                    map.RegionId,
                    map.RegionName,
                    map.ContinentId,
                    map.ContinentName,
                    Array.Empty<string>(),
                    map.DefaultFloor,
                    challengesLoaded: false,
                    type: mapType,
                    floors: map.Floors,
                    workingFloor: cached?.WorkingFloor);

                if (_resolver != null) {
                    return await _resolver.ResolveMapAsync(initial, cancellationToken).ConfigureAwait(false);
                }

                return initial;
            } catch (Exception ex) {
                _logWarn?.Invoke("Could not fetch map " + mapId + " from GW2 API: " + ex.GetType().Name);
                return null;
            }
        }

        private async Task<bool> EnsureRegionMapsAsync(int continentId, int regionId, CancellationToken cancellationToken) {
            if (_gw2ApiManager?.Gw2ApiClient == null || continentId <= 0 || regionId <= 0) return false;

            try {
                await GetAllMapsAsync(cancellationToken).ConfigureAwait(false);
                if (_resolver != null) {
                    return await _resolver.EnsureRegionMapsAsync(continentId, regionId, cancellationToken).ConfigureAwait(false);
                }
                return false;
            } catch (Exception ex) {
                _logWarn?.Invoke("Could not ensure region maps for region " + regionId + " from GW2 API: " + ex.GetType().Name);
                return false;
            }
        }

        private sealed class Gw2SharpHeroChallengeApi : IGw2HeroChallengeApi
        {
            private readonly Gw2ApiManager _gw2ApiManager;

            public Gw2SharpHeroChallengeApi(Gw2ApiManager gw2ApiManager) {
                _gw2ApiManager = gw2ApiManager;
            }

            public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> FetchFloorRegionChallengesAsync(int continentId, int floor, int regionId, CancellationToken cancellationToken) {
                if (_gw2ApiManager?.Gw2ApiClient == null) return new Dictionary<int, IReadOnlyList<string>>();

                ContinentFloorRegion region = await _gw2ApiManager.Gw2ApiClient.V2.Continents[continentId]
                    .Floors[floor]
                    .Regions[regionId]
                    .GetAsync(cancellationToken)
                    .ConfigureAwait(false);

                var dict = new Dictionary<int, IReadOnlyList<string>>();
                if (region?.Maps != null) {
                    foreach (var kvp in region.Maps) {
                        IReadOnlyList<string> sc = kvp.Value.SkillChallenges != null
                            ? kvp.Value.SkillChallenges.Select(s => s.Id).Where(id => !string.IsNullOrEmpty(id)).ToList()
                            : (IReadOnlyList<string>)Array.Empty<string>();
                        dict[kvp.Key] = sc;
                    }
                }

                return dict;
            }

            public async Task<IReadOnlyList<string>> FetchMapChallengesAsync(int continentId, int floor, int regionId, int mapId, CancellationToken cancellationToken) {
                if (_gw2ApiManager?.Gw2ApiClient == null) return Array.Empty<string>();

                ContinentFloorRegionMap floorMap = await _gw2ApiManager.Gw2ApiClient.V2.Continents[continentId]
                    .Floors[floor]
                    .Regions[regionId]
                    .Maps[mapId]
                    .GetAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (floorMap?.SkillChallenges != null) {
                    return floorMap.SkillChallenges.Select(sc => sc.Id).Where(id => !string.IsNullOrEmpty(id)).ToList();
                }

                return Array.Empty<string>();
            }
        }

        internal static bool IsNotFound(Exception ex) => Gw2ApiExceptionHelper.IsNotFound(ex);

        /// <summary>
        /// A running module's settings. Null when the instance is not running, or is still loading
        /// and has not defined its settings yet.
        /// </summary>
        private static SettingCollection SettingsOf(ModuleManager manager) {
            ModuleParameters parameters = manager.ModuleInstance == null
                                              ? null
                                              : ModuleParametersField?.GetValue(manager.ModuleInstance) as ModuleParameters;

            return parameters?.SettingsManager?.ModuleSettings;
        }

        /// <summary>
        /// The module's version, as text. <see cref="Manifest.Version"/> has the type of a SemVer
        /// assembly that lives inside Blish HUD itself and that the module does not reference, so
        /// the value is read by reflection instead of adding that reference.
        /// </summary>
        private static string VersionTextOf(Manifest manifest) {
            object version = typeof(Manifest).GetProperty("Version")?.GetValue(manifest, null);

            return version?.ToString() ?? string.Empty;
        }

        /// <summary>
        /// One setting as the AI may see it. A subcollection becomes a node that holds its children,
        /// which <see cref="SettingWalk"/> reads under a qualified key; a setting that cannot be
        /// read is left out rather than breaking the whole answer.
        /// </summary>
        private static ModuleSettingInfo ToSettingInfo(SettingEntry entry) {
            try {
                if (ValueOf(entry) is SettingCollection nested) {
                    return ModuleSettingInfo.Subcollection(entry.EntryKey, entry.DisplayName, entry.Description,
                                                           ChildrenOf(nested));
                }

                return new ModuleSettingInfo(entry.EntryKey,
                                             entry.DisplayName,
                                             entry.Description,
                                             TypeNameOf(entry),
                                             ValueTextOf(entry),
                                             RangeBoundOf(entry, "MinValue"),
                                             RangeBoundOf(entry, "MaxValue"),
                                             AllowedValuesOf(entry));
            } catch (Exception) {
                return null;
            }
        }

        private static List<ModuleSettingInfo> ChildrenOf(SettingCollection nested) {
            var children = new List<ModuleSettingInfo>();

            foreach (SettingEntry child in nested.Entries) {
                ModuleSettingInfo info = ToSettingInfo(child);

                if (info != null) children.Add(info);
            }

            return children;
        }

        private static string TypeNameOf(SettingEntry entry) {
            return entry.SettingType?.Name ?? "unknown";
        }

        /// <summary>
        /// The setting's current value, as the text a player would read in the settings view.
        /// </summary>
        private static string ValueTextOf(SettingEntry entry) {
            object value = ValueOf(entry);

            switch (value) {
                case null:      return string.Empty;
                case bool flag: return flag ? "true" : "false";
                case Enum choice:   return choice.ToString();
                default:            return InvariantText(value);
            }
        }

        /// <summary>
        /// The value, hidden behind <see cref="SettingEntry{T}.Value">the generic entry's Value
        /// property</see>: the type is only known at runtime, so it is read by reflection.
        /// </summary>
        private static object ValueOf(SettingEntry entry) {
            PropertyInfo value = entry.GetType().GetProperty("Value");

            return value?.GetValue(entry, null);
        }

        /// <summary>
        /// One end of a numeric range, when the module gave its setting one: modules declare ranges
        /// as compliance requisites, whose numeric type is only known at runtime. Null when the
        /// setting has no range.
        /// </summary>
        private static string RangeBoundOf(SettingEntry entry, string boundProperty) {
            foreach (IComplianceRequisite requisite in entry.GetComplianceRequisite()) {
                TypeInfo rangeInterface = requisite.GetType().GetTypeInfo()
                                                   .ImplementedInterfaces
                                                   .FirstOrDefault(candidate => candidate.IsGenericType
                                                                             && candidate.GetGenericTypeDefinition() == typeof(INumericRangeComplianceRequisite<>))
                                                   ?.GetTypeInfo();

                if (rangeInterface == null) continue;

                object bound = rangeInterface.GetDeclaredProperty(boundProperty)?.GetValue(requisite, null);

                if (bound != null) return InvariantText(bound);
            }

            return null;
        }

        /// <summary>
        /// A number as text the way it is written in the settings view, so a value read back in
        /// another language's formatting stays the same value.
        /// </summary>
        private static string InvariantText(object value) {
            switch (value) {
                case float number:  return number.ToString(CultureInfo.InvariantCulture);
                case double number: return number.ToString(CultureInfo.InvariantCulture);
                default:            return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }
    }
}
