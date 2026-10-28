using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// The Blish HUD host as the Lookups see it: what Modules the Player has installed and what a
    /// Module's Settings are, the Player's location and Owned Content, and their progress in
    /// Guild Wars 2. This is the port. <c>GameThreadWorld</c> is the decorator every Lookup goes
    /// through, which reads live Blish HUD state on the game thread (ADR-0009);
    /// <c>World.BlishHudWorld</c> is the adapter that asks the real Blish HUD, and tests drive the
    /// Lookups with an in-memory world.
    /// </summary>
    /// <remarks>
    /// Lookups read game state (Mumble Link, settings) on the game thread first, then await GW2 API
    /// calls off it. HTTP and anything after an await must not touch Blish HUD state, and any values
    /// needed after an await are captured before it.
    /// </remarks>
    public interface IBlishHudWorld
    {
        /// <summary>
        /// Every Installed Module, enabled and disabled. Includes this module itself.
        /// </summary>
        Task<IReadOnlyList<InstalledModuleInfo>> ListInstalledModulesAsync(CancellationToken cancellationToken);

        /// <summary>
        /// The Settings of one Installed Module. A disabled Module has none to read until it is
        /// enabled; its result says so.
        /// </summary>
        Task<ModuleSettingsResult> GetModuleSettingsAsync(string @namespace, CancellationToken cancellationToken);

        /// <summary>
        /// The Player's current location from Mumble Link and map data: character name, map ID and name,
        /// and region ID and name.
        /// </summary>
        Task<PlayerLocationResult> GetLocationAsync(CancellationToken cancellationToken);

        /// <summary>
        /// The expansions and content the Player's Guild Wars 2 account owns. Requires the 'account'
        /// API permission.
        /// </summary>
        Task<AccountAccessResult> GetAccountAccessAsync(CancellationToken cancellationToken);

        /// <summary>
        /// The Player's progress in Guild Wars 2: mastery points and unfinished tracks per region (requires
        /// 'progression' permission), or missing hero challenges on the current map and region (requires
        /// 'characters' permission).
        /// </summary>
        Task<ProgressResult> GetProgressAsync(ProgressKind kind, CancellationToken cancellationToken);

        /// <summary>
        /// Enables or disables one Installed Module, the way the Player's own module list does. It
        /// answers whether it happened, the state before, and — when it did not — why.
        /// </summary>
        /// <remarks>
        /// This is the one write a Lookup never makes. Only Apply and Undo call it, and both go
        /// through the same decorator the reads do, so it happens on the game thread (ADR-0009). A
        /// write answers from live state and, like a read, must not wait for anything: everything it
        /// touches happens inside the queued call.
        /// </remarks>
        Task<ModuleChange> SetModuleEnabledAsync(string @namespace, bool enabled, CancellationToken cancellationToken);

        /// <summary>
        /// Changes one Setting of one enabled Module. <paramref name="value"/> is the text form the
        /// Lookups hand out: <c>true</c>/<c>false</c>, an invariant number, or an enum's name. It
        /// answers whether it happened, the value before, and — when it did not — why.
        /// </summary>
        Task<ModuleSettingChange> SetSettingAsync(string @namespace, string key, string value, CancellationToken cancellationToken);

        /// <summary>
        /// Finds Pathing marker categories matching a search query in installed marker packs and reports
        /// their visibility and marker/trail counts on the queried or current map.
        /// </summary>
        Task<MarkerCategoriesResult> FindMarkerCategoriesAsync(string search, int? mapId, CancellationToken cancellationToken);

        /// <summary>
        /// Lists available Marker Packs from Pathing's official repository list and packs installed in the
        /// shared markers directory.
        /// </summary>
        Task<MarkerPacksResult> ListMarkerPacksAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Downloads an official Pathing marker pack, verifies it, writes it to the markers directory,
        /// and restarts Pathing.
        /// </summary>
        Task<MarkerPackChange> DownloadMarkerPackAsync(string pack, CancellationToken cancellationToken);

        /// <summary>
        /// Deletes a marker pack file from the markers directory and restarts Pathing.
        /// </summary>
        Task<MarkerPackChange> DeleteMarkerPackAsync(string fileName, CancellationToken cancellationToken);

        /// <summary>
        /// Retrieves available modules from the official Blish HUD module catalog.
        /// </summary>
        Task<IReadOnlyList<CatalogModuleInfo>> GetCatalogAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Reads package information for one catalog module on the game thread.
        /// </summary>
        Task<CatalogPackageEntry> GetCatalogPackageEntryAsync(string @namespace, CancellationToken cancellationToken);

        /// <summary>
        /// Fetches package details (download, hash check, manifest parse) off the game thread for a captured catalog entry.
        /// </summary>
        Task<CatalogModuleDetails> FetchCatalogModuleDetailsAsync(CatalogPackageEntry entry, CancellationToken cancellationToken);

        /// <summary>
        /// Retrieves detailed information for one candidate module from the official Blish HUD catalog,
        /// including its description and requested GW2 API permissions.
        /// </summary>
        Task<CatalogModuleDetails> GetCatalogModuleDetailsAsync(string @namespace, CancellationToken cancellationToken);

        /// <summary>
        /// Installs a module from the official Blish HUD catalog by its namespace.
        /// </summary>
        Task<ModuleInstallChange> InstallModuleAsync(string @namespace, CancellationToken cancellationToken);
    }

    /// <summary>
    /// One Installed Module as the AI may see it. No Setting values here: those have their own way
    /// in, with secrets removed.
    /// </summary>
    public sealed class InstalledModuleInfo
    {
        public InstalledModuleInfo(string @namespace, string name, string version, string description, bool enabled) {
            if (@namespace == null) throw new ArgumentNullException(nameof(@namespace));
            if (name == null)       throw new ArgumentNullException(nameof(name));

            Namespace   = @namespace;
            Name        = name;
            Version     = version ?? string.Empty;
            Description = description ?? string.Empty;
            Enabled     = enabled;
        }

        /// <summary>
        /// What identifies the Module to the AI: the namespace a Settings Lookup is asked with.
        /// </summary>
        public string Namespace { get; }

        public string Name        { get; }
        public string Version     { get; }
        public string Description { get; }
        public bool   Enabled     { get; }
    }

    /// <summary>
    /// What a Settings Lookup found: the Settings, or the reason there are none.
    /// </summary>
    public enum ModuleSettingsStatus
    {
        /// <summary>The Module is enabled and its Settings were read.</summary>
        Ok,

        /// <summary>No Installed Module has that namespace.</summary>
        ModuleNotInstalled,

        /// <summary>The Module is there but disabled; its Settings cannot be read until it is enabled.</summary>
        ModuleDisabled,

        /// <summary>The Module is AI Goal Planner itself; its Settings are never shared.</summary>
        ThisModule
    }

    /// <summary>
    /// The answer to a Settings Lookup: the Module by the name the Player knows it by, and either
    /// its Settings or the status that says why there are none.
    /// </summary>
    public sealed class ModuleSettingsResult
    {
        public ModuleSettingsResult(ModuleSettingsStatus status, string moduleName, IReadOnlyList<ModuleSettingInfo> settings,
                                    string @namespace = null) {
            Status     = status;
            ModuleName = moduleName ?? string.Empty;
            Settings   = settings ?? throw new ArgumentNullException(nameof(settings));
            Namespace  = @namespace;
        }

        public ModuleSettingsStatus Status { get; }

        public string ModuleName { get; }

        /// <summary>
        /// The Installed Module's own namespace, when one matched; null when none did. This is the
        /// only module name a Lookup may repeat in its log: the namespace it was asked with is the
        /// model's own text, and the model can put anything there, a Setting's value included.
        /// </summary>
        public string Namespace { get; }

        /// <summary>Empty unless <see cref="Status"/> is <see cref="ModuleSettingsStatus.Ok"/>.</summary>
        public IReadOnlyList<ModuleSettingInfo> Settings { get; }
    }

    /// <summary>
    /// One Setting of a Module, as the AI may see it: what it is called, what it is now, and how far
    /// it may go. Everything is text, the way the module would show it in its settings view. A
    /// Module's settings can nest (a subcollection of Settings inside another), so a node carries
    /// either a value or child nodes.
    /// </summary>
    public sealed class ModuleSettingInfo
    {
        private static readonly IReadOnlyList<ModuleSettingInfo> NoChildren = new ModuleSettingInfo[0];
        private static readonly IReadOnlyList<string>            NoAllowed  = new string[0];

        public ModuleSettingInfo(string key, string displayName, string description, string typeName, string value,
                                 string min, string max, IReadOnlyList<string> allowedValues = null) {
            if (key == null) throw new ArgumentNullException(nameof(key));

            Key         = key;
            DisplayName = displayName ?? string.Empty;
            Description = description ?? string.Empty;
            TypeName    = typeName ?? string.Empty;
            Value       = value ?? string.Empty;
            Min         = min;
            Max         = max;
            Children    = NoChildren;
            AllowedValues = allowedValues ?? NoAllowed;
        }

        private ModuleSettingInfo(string key, string displayName, string description, IReadOnlyList<ModuleSettingInfo> children) {
            if (key == null) throw new ArgumentNullException(nameof(key));

            Key            = key;
            DisplayName    = displayName ?? string.Empty;
            Description    = description ?? string.Empty;
            TypeName       = "SettingCollection";
            Value          = string.Empty;
            Min            = null;
            Max            = null;
            Children       = children ?? NoChildren;
            IsSubcollection = true;
            AllowedValues  = NoAllowed;
        }

        /// <summary>
        /// A subcollection of Settings, the way a module groups them in its settings view. It holds
        /// no value of its own; the walk reads its children under a qualified key.
        /// </summary>
        public static ModuleSettingInfo Subcollection(string key, string displayName, string description,
                                                      IReadOnlyList<ModuleSettingInfo> children) {
            return new ModuleSettingInfo(key, displayName, description, children);
        }

        public string Key         { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public string TypeName    { get; }

        /// <summary>The Setting's current value, as text. Numbers are invariant-culture. Empty for a subcollection.</summary>
        public string Value { get; }

        /// <summary>The smallest value a range Setting accepts, or null when it has no range.</summary>
        public string Min { get; }

        /// <summary>The largest value a range Setting accepts, or null when it has no range.</summary>
        public string Max { get; }

        /// <summary>
        /// The values an enumerated Setting accepts, in its own order, or empty for any other kind
        /// of Setting. A Plan can only set one of these, so the AI is told the list instead of
        /// guessing at a name.
        /// </summary>
        public IReadOnlyList<string> AllowedValues { get; }

        /// <summary>The Settings inside a subcollection, empty for a Setting.</summary>
        public IReadOnlyList<ModuleSettingInfo> Children { get; }

        /// <summary>Whether this node is a subcollection that holds other Settings.</summary>
        public bool IsSubcollection { get; }
    }

    /// <summary>
    /// The Player's location in Guild Wars 2: the current map and region, and the active character name.
    /// </summary>
    public sealed class PlayerLocation
    {
        public PlayerLocation(string characterName, int mapId, string mapName, int regionId, string regionName) {
            CharacterName = characterName ?? string.Empty;
            MapId         = mapId;
            MapName       = mapName ?? string.Empty;
            RegionId      = regionId;
            RegionName    = regionName ?? string.Empty;
        }

        public string CharacterName { get; }
        public int    MapId         { get; }
        public string MapName       { get; }
        public int    RegionId      { get; }
        public string RegionName    { get; }
    }

    public enum LocationStatus
    {
        Ok,
        Unavailable
    }

    public sealed class PlayerLocationResult
    {
        public PlayerLocationResult(LocationStatus status, PlayerLocation location, string reason = null) {
            Status   = status;
            Location = location;
            Reason   = reason ?? string.Empty;
        }

        public LocationStatus Status   { get; }
        public PlayerLocation Location { get; }
        public string         Reason   { get; }
    }

    public enum AccountAccessStatus
    {
        Ok,
        MissingPermission,
        Unavailable,
        NoApiAccess
    }

    /// <summary>
    /// The expansions and content the Player's account owns.
    /// </summary>
    public sealed class AccountAccessResult
    {
        public AccountAccessResult(AccountAccessStatus status, IReadOnlyList<string> ownedContent,
                                   string message = null, string missingPermission = null) {
            Status            = status;
            OwnedContent      = ownedContent ?? Array.Empty<string>();
            Message           = message ?? string.Empty;
            MissingPermission = missingPermission ?? string.Empty;
        }

        public AccountAccessStatus    Status            { get; }
        public IReadOnlyList<string>  OwnedContent      { get; }
        public string                 Message           { get; }
        public string                 MissingPermission { get; }
    }

    public enum ProgressKind
    {
        Mastery,
        HeroPoints
    }

    public enum ProgressStatus
    {
        Ok,
        MissingPermission,
        NoCharacterOrMap,
        Unavailable,
        Unknown,
        NoApiAccess
    }

    public sealed class MasteryTrackProgress
    {
        public MasteryTrackProgress(int id, string name, string region, int level, int totalLevels) {
            Id          = id;
            Name        = name ?? string.Empty;
            Region      = region ?? string.Empty;
            Level       = level;
            TotalLevels = totalLevels;
        }

        public int    Id          { get; }
        public string Name        { get; }
        public string Region      { get; }
        public int    Level       { get; }
        public int    TotalLevels { get; }
    }

    public sealed class MasteryPointRegion
    {
        public MasteryPointRegion(string region, int spent, int earned) {
            Region = region ?? string.Empty;
            Spent  = spent;
            Earned = earned;
        }

        public string Region  { get; }
        public int    Spent   { get; }
        public int    Earned  { get; }
        public int    Unspent => Math.Max(0, Earned - Spent);
    }

    public sealed class MasteryTrackRegion
    {
        public MasteryTrackRegion(string region,
                                  IReadOnlyList<MasteryTrackProgress> unfinishedTracks,
                                  bool tracksAvailable = true) {
            Region           = region ?? string.Empty;
            UnfinishedTracks = unfinishedTracks ?? Array.Empty<MasteryTrackProgress>();
            TracksAvailable  = tracksAvailable && unfinishedTracks != null;
        }

        public string                              Region           { get; }
        public IReadOnlyList<MasteryTrackProgress> UnfinishedTracks { get; }
        public bool                                TracksAvailable  { get; }
    }

    public sealed class MasteryRegionProgress
    {
        public MasteryRegionProgress(string region, int spent, int earned,
                                     IReadOnlyList<MasteryTrackProgress> unfinishedTracks = null,
                                     bool tracksAvailable = true) {
            Region           = region ?? string.Empty;
            Spent            = spent;
            Earned           = earned;
            UnfinishedTracks = unfinishedTracks ?? Array.Empty<MasteryTrackProgress>();
            TracksAvailable  = tracksAvailable && unfinishedTracks != null;
        }

        public string                              Region           { get; }
        public int                                 Spent            { get; }
        public int                                 Earned           { get; }
        public int                                 Unspent          => Math.Max(0, Earned - Spent);
        public IReadOnlyList<MasteryTrackProgress> UnfinishedTracks { get; }
        public bool                                TracksAvailable  { get; }
    }

    public sealed class HeroChallengeProgress
    {
        public HeroChallengeProgress(int mapTotal, int mapCompleted, int? regionMissing, bool regionIncomplete = false) {
            MapTotal         = mapTotal;
            MapCompleted     = mapCompleted;
            RegionMissing    = regionMissing;
            RegionIncomplete = regionIncomplete;
        }

        public HeroChallengeProgress(int mapTotal, int mapCompleted, int regionMissing)
            : this(mapTotal, mapCompleted, (int?)regionMissing, false) {
        }

        public int   MapTotal         { get; }
        public int   MapCompleted     { get; }
        public int   MapMissing       => Math.Max(0, MapTotal - MapCompleted);
        public int?  RegionMissing    { get; }
        public bool  RegionIncomplete { get; }
    }

    public sealed class ProgressResult
    {
        public ProgressResult(ProgressStatus status, ProgressKind kind,
                              IReadOnlyList<MasteryPointRegion> masteryPoints = null,
                              IReadOnlyList<MasteryTrackRegion> masteryTracks = null,
                              HeroChallengeProgress heroChallenges = null,
                              string characterName = null, int mapId = 0, string mapName = null, string regionName = null,
                              string message = null, string missingPermission = null,
                              bool tracksAvailable = true,
                              IReadOnlyList<MasteryRegionProgress> masteryRegions = null) {
            Status            = status;
            Kind              = kind;
            TracksAvailable   = tracksAvailable;
            HeroChallenges    = heroChallenges;
            CharacterName     = characterName ?? string.Empty;
            MapId             = mapId;
            MapName           = mapName ?? string.Empty;
            RegionName        = regionName ?? string.Empty;
            Message           = message ?? string.Empty;
            MissingPermission = missingPermission ?? string.Empty;

            if (masteryPoints != null || masteryTracks != null) {
                MasteryPoints = masteryPoints ?? Array.Empty<MasteryPointRegion>();
                MasteryTracks = masteryTracks ?? Array.Empty<MasteryTrackRegion>();
            } else if (masteryRegions != null) {
                var pList = new List<MasteryPointRegion>();
                var tList = new List<MasteryTrackRegion>();
                foreach (var r in masteryRegions) {
                    pList.Add(new MasteryPointRegion(r.Region, r.Spent, r.Earned));
                    tList.Add(new MasteryTrackRegion(r.Region, r.UnfinishedTracks, r.TracksAvailable));
                }
                MasteryPoints = pList;
                MasteryTracks = tList;
            } else {
                MasteryPoints = Array.Empty<MasteryPointRegion>();
                MasteryTracks = Array.Empty<MasteryTrackRegion>();
            }

            MasteryRegions = masteryRegions ?? Array.Empty<MasteryRegionProgress>();
        }

        public ProgressStatus                       Status            { get; }
        public ProgressKind                         Kind              { get; }
        public IReadOnlyList<MasteryPointRegion>    MasteryPoints     { get; }
        public IReadOnlyList<MasteryTrackRegion>    MasteryTracks     { get; }
        public bool                                 TracksAvailable   { get; }
        public IReadOnlyList<MasteryRegionProgress> MasteryRegions    { get; }
        public HeroChallengeProgress                HeroChallenges    { get; }
        public string                               CharacterName     { get; }
        public int                                  MapId             { get; }
        public string                               MapName           { get; }
        public string                               RegionName        { get; }
        public string                               Message           { get; }
        public string                               MissingPermission { get; }
    }
}
