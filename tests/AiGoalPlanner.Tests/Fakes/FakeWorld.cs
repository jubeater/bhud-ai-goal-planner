using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;

namespace jubeater.aigoalplanner.Tests.Fakes
{
    /// <summary>
    /// An in-memory world for tests: the Player's modules and their settings, location, owned
    /// content and progress are whatever the test puts in, and a change made through it really
    /// changes them. A test can also take over answering a Lookup or a write itself, to hold it open,
    /// fail it or count it.
    /// </summary>
    internal sealed partial class FakeWorld : IBlishHudWorld
    {
        public List<InstalledModuleInfo> Modules { get; } = new List<InstalledModuleInfo>();

        /// <summary>
        /// The answer per module namespace for <see cref="GetModuleSettingsAsync"/>. A namespace
        /// that is missing here is answered as not installed.
        /// </summary>
        public Dictionary<string, ModuleSettingsResult> SettingsByNamespace { get; } =
            new Dictionary<string, ModuleSettingsResult>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// When set, answers every settings Lookup instead of the dictionary, so a test can hold
        /// the Lookup open, cancel it or fail it.
        /// </summary>
        public Func<string, CancellationToken, Task<ModuleSettingsResult>> AnswerGetModuleSettings;

        public PlayerLocationResult LocationResult { get; set; } =
            new PlayerLocationResult(LocationStatus.Ok, new PlayerLocation("Test Character", 1205, "Domain of Vabbi", 10, "Crystal Desert"));

        public AccountAccessResult AccountAccessResult { get; set; } =
            new AccountAccessResult(AccountAccessStatus.Ok, new[] { "GuildWars2", "HeartOfThorns", "PathOfFire" });

        public ProgressResult MasteryProgressResult { get; set; } =
            new ProgressResult(ProgressStatus.Ok, ProgressKind.Mastery,
                               masteryRegions: new[] {
                                   new MasteryRegionProgress("Tyria", 49, 54, new[] {
                                       new MasteryTrackProgress(1, "Pact Commander", "Tyria", 4, 5)
                                   }),
                                   new MasteryRegionProgress("Maguuma", 150, 163, new[] {
                                       new MasteryTrackProgress(2, "Gliding", "Maguuma", 5, 6)
                                   })
                               });

        public ProgressResult HeroPointsProgressResult { get; set; } =
            new ProgressResult(ProgressStatus.Ok, ProgressKind.HeroPoints,
                               heroChallenges: new HeroChallengeProgress(mapTotal: 14, mapCompleted: 7, regionMissing: 42),
                               characterName: "Test Character", mapId: 1205, mapName: "Domain of Vabbi", regionName: "Crystal Desert");

        public Func<ProgressKind, CancellationToken, Task<ProgressResult>> AnswerGetProgress { get; set; }
        public MarkerCategoriesResult MarkerCategoriesResult { get; set; } = MarkerCategoriesResult.NoPacks();
        public Func<string, int?, CancellationToken, Task<MarkerCategoriesResult>> AnswerFindMarkerCategories { get; set; }

        /// <summary>
        /// When set, answers every module state change instead of changing one, so a test can make
        /// a write fail without breaking anything else.
        /// </summary>
        public Func<string, bool, ModuleChange> AnswerSetModuleEnabled;

        /// <summary>When set, answers every Setting change instead of making it.</summary>
        public Func<string, string, string, ModuleSettingChange> AnswerSetSetting;

        /// <summary>
        /// Every change that was made through this world, in order, as "enable ns", "disable ns" or
        /// "set ns/key=value": what the Player's configuration would show afterwards.
        /// </summary>
        public List<string> Writes { get; } = new List<string>();

        public int ListCalls          { get; private set; }
        public int SettingsCalls      { get; private set; }
        public int LocationCalls      { get; private set; }
        public int AccountAccessCalls { get; private set; }
        public int ProgressCalls      { get; private set; }
        public int MarkerCategoriesCalls { get; private set; }
        public int ModuleWrites       { get; private set; }
        public int SettingWrites      { get; private set; }

        public Task<IReadOnlyList<InstalledModuleInfo>> ListInstalledModulesAsync(CancellationToken cancellationToken) {
            ListCalls++;

            IReadOnlyList<InstalledModuleInfo> modules = Modules.ToList();

            return Task.FromResult(modules);
        }

        public Task<ModuleSettingsResult> GetModuleSettingsAsync(string @namespace, CancellationToken cancellationToken) {
            SettingsCalls++;

            Func<string, CancellationToken, Task<ModuleSettingsResult>> answer = AnswerGetModuleSettings;

            if (answer != null) return answer(@namespace, cancellationToken);

            ModuleSettingsResult result = SettingsByNamespace.TryGetValue(@namespace ?? string.Empty, out ModuleSettingsResult found)
                                              ? found
                                              : NotInstalled(@namespace ?? string.Empty);

            return Task.FromResult(WithTheModulesOwnNamespace(result, @namespace));
        }

        public Task<PlayerLocationResult> GetLocationAsync(CancellationToken cancellationToken) {
            LocationCalls++;

            return Task.FromResult(LocationResult);
        }

        public Task<AccountAccessResult> GetAccountAccessAsync(CancellationToken cancellationToken) {
            AccountAccessCalls++;

            return Task.FromResult(AccountAccessResult);
        }

        public Task<ProgressResult> GetProgressAsync(ProgressKind kind, CancellationToken cancellationToken) {
            ProgressCalls++;

            if (AnswerGetProgress != null) return AnswerGetProgress(kind, cancellationToken);

            ProgressResult result = kind == ProgressKind.Mastery ? MasteryProgressResult : HeroPointsProgressResult;

            return Task.FromResult(result);
        }

        public Task<MarkerCategoriesResult> FindMarkerCategoriesAsync(string search, int? mapId, CancellationToken cancellationToken) {
            MarkerCategoriesCalls++;

            if (AnswerFindMarkerCategories != null) return AnswerFindMarkerCategories(search, mapId, cancellationToken);

            return Task.FromResult(MarkerCategoriesResult);
        }

        /// <summary>
        /// Names the Installed Module that matched, the way the real world does, so a test can tell
        /// that the log never repeats the spelling the model asked with.
        /// </summary>
        private ModuleSettingsResult WithTheModulesOwnNamespace(ModuleSettingsResult result, string @namespace) {
            InstalledModuleInfo matched =
                Modules.FirstOrDefault(module => string.Equals(module.Namespace, @namespace, StringComparison.OrdinalIgnoreCase));

            return matched == null
                       ? result
                       : new ModuleSettingsResult(result.Status, result.ModuleName, result.Settings, matched.Namespace);
        }

        public static ModuleSettingsResult NotInstalled(string @namespace) {
            return new ModuleSettingsResult(ModuleSettingsStatus.ModuleNotInstalled, @namespace,
                                             Array.Empty<ModuleSettingInfo>());
        }

        public static ModuleSettingsResult Disabled(string moduleName) {
            return new ModuleSettingsResult(ModuleSettingsStatus.ModuleDisabled, moduleName,
                                             Array.Empty<ModuleSettingInfo>());
        }

        public static ModuleSettingsResult Ok(string moduleName, params ModuleSettingInfo[] settings) {
            return new ModuleSettingsResult(ModuleSettingsStatus.Ok, moduleName, settings);
        }

        public int MarkerPacksCalls { get; private set; }
        public int DownloadMarkerPackCalls { get; private set; }
        public int DeleteMarkerPackCalls { get; private set; }
        public MarkerPacksResult MarkerPacksResult { get; set; }
        public List<MarkerPackInfo> MarkerPacks { get; set; } = new List<MarkerPackInfo>();
        public List<string> DownloadedPacks { get; } = new List<string>();
        public Func<CancellationToken, Task<MarkerPacksResult>> AnswerListMarkerPacks { get; set; }
        public Func<string, CancellationToken, Task<MarkerPackChange>> AnswerDownloadMarkerPack { get; set; }
        public Func<string, CancellationToken, Task<MarkerPackChange>> AnswerDeleteMarkerPack { get; set; }

        public Task<MarkerPacksResult> ListMarkerPacksAsync(CancellationToken cancellationToken) {
            MarkerPacksCalls++;
            if (AnswerListMarkerPacks != null) return AnswerListMarkerPacks(cancellationToken);
            if (MarkerPacksResult != null) return Task.FromResult(MarkerPacksResult);
            return Task.FromResult(new MarkerPacksResult(MarkerPacksStatus.Ok, MarkerPacks.ToArray()));
        }

        public Task<MarkerPackChange> DownloadMarkerPackAsync(string pack, CancellationToken cancellationToken) {
            DownloadMarkerPackCalls++;
            if (AnswerDownloadMarkerPack != null) return AnswerDownloadMarkerPack(pack, cancellationToken);
            DownloadedPacks.Add(pack);
            Writes.Add("download " + pack);

            MarkerPackInfo existing = MarkerPacks.FirstOrDefault(p => string.Equals(p.Name, pack, StringComparison.OrdinalIgnoreCase) || string.Equals(p.FileName, pack, StringComparison.OrdinalIgnoreCase));
            string packName = existing?.Name ?? pack;
            string fileName = existing?.FileName ?? (pack + ".taco");
            double sizeMb = existing?.SizeMb ?? 10.0;
            return Task.FromResult(new MarkerPackChange(applied: true, changed: true, packName: packName, fileName: fileName, sizeMb: sizeMb));
        }

        public Task<MarkerPackChange> DeleteMarkerPackAsync(string fileName, CancellationToken cancellationToken) {
            DeleteMarkerPackCalls++;
            if (AnswerDeleteMarkerPack != null) return AnswerDeleteMarkerPack(fileName, cancellationToken);
            MarkerPackInfo existing = MarkerPacks.FirstOrDefault(p => string.Equals(p.FileName, fileName, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, fileName, StringComparison.OrdinalIgnoreCase));
            string packName = existing?.Name ?? fileName;
            DownloadedPacks.Remove(fileName);
            DownloadedPacks.Remove(packName);
            Writes.Add("delete " + fileName);

            return Task.FromResult(new MarkerPackChange(applied: true, changed: true, packName: fileName, fileName: fileName, sizeMb: 0));
        }

        public List<CatalogModuleInfo> CatalogModules { get; } = new List<CatalogModuleInfo>();
        public int CatalogCalls { get; private set; }
        public int InstallCalls { get; private set; }
        public Func<CancellationToken, Task<IReadOnlyList<CatalogModuleInfo>>> AnswerGetCatalog { get; set; }
        public Func<string, CancellationToken, Task<ModuleInstallChange>> AnswerInstallModule { get; set; }

        public Task<IReadOnlyList<CatalogModuleInfo>> GetCatalogAsync(CancellationToken cancellationToken) {
            CatalogCalls++;
            if (AnswerGetCatalog != null) return AnswerGetCatalog(cancellationToken);
            return Task.FromResult<IReadOnlyList<CatalogModuleInfo>>(CatalogModules.ToList());
        }

        public Dictionary<string, CatalogModuleDetails> CatalogDetails { get; } =
            new Dictionary<string, CatalogModuleDetails>(StringComparer.OrdinalIgnoreCase);

        public Func<string, CancellationToken, Task<CatalogPackageEntry>> AnswerGetCatalogPackageEntry { get; set; }
        public Func<CatalogPackageEntry, CancellationToken, Task<CatalogModuleDetails>> AnswerFetchCatalogModuleDetails { get; set; }
        public Func<string, CancellationToken, Task<CatalogModuleDetails>> AnswerGetCatalogModuleDetails { get; set; }
        public int CatalogDetailsCalls { get; private set; }

        public Task<CatalogPackageEntry> GetCatalogPackageEntryAsync(string @namespace, CancellationToken cancellationToken) {
            if (AnswerGetCatalogPackageEntry != null) return AnswerGetCatalogPackageEntry(@namespace, cancellationToken);

            string wanted = (@namespace ?? string.Empty).Trim();
            CatalogModuleInfo existing = CatalogModules.FirstOrDefault(m =>
                string.Equals(m.Namespace, wanted, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m.Name, wanted, StringComparison.OrdinalIgnoreCase));

            if (existing != null) {
                return Task.FromResult(new CatalogPackageEntry(existing.Namespace, existing.Name, existing.Version, "http://pkg.test/" + existing.Namespace, "FAKEHASH"));
            }

            return Task.FromResult<CatalogPackageEntry>(null);
        }

        public Task<CatalogModuleDetails> FetchCatalogModuleDetailsAsync(CatalogPackageEntry entry, CancellationToken cancellationToken) {
            if (AnswerFetchCatalogModuleDetails != null) return AnswerFetchCatalogModuleDetails(entry, cancellationToken);
            return GetCatalogModuleDetailsAsync(entry.Namespace, cancellationToken);
        }

        public Task<CatalogModuleDetails> GetCatalogModuleDetailsAsync(string @namespace, CancellationToken cancellationToken) {
            CatalogDetailsCalls++;
            if (AnswerGetCatalogModuleDetails != null) return AnswerGetCatalogModuleDetails(@namespace, cancellationToken);

            string wanted = (@namespace ?? string.Empty).Trim();
            if (CatalogDetails.TryGetValue(wanted, out CatalogModuleDetails found)) {
                return Task.FromResult(found);
            }

            CatalogModuleDetails byName = CatalogDetails.Values.FirstOrDefault(d => string.Equals(d.Name, wanted, StringComparison.OrdinalIgnoreCase));
            if (byName != null) return Task.FromResult(byName);

            CatalogModuleInfo existing = CatalogModules.FirstOrDefault(m =>
                string.Equals(m.Namespace, wanted, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m.Name, wanted, StringComparison.OrdinalIgnoreCase));

            if (existing != null) {
                return Task.FromResult(new CatalogModuleDetails(
                    success: true,
                    @namespace: existing.Namespace,
                    name: existing.Name,
                    version: existing.Version,
                    description: existing.Description,
                    apiPermissions: existing.ApiPermissions
                ));
            }

            return Task.FromResult(new CatalogModuleDetails(false, wanted, problem: "Module not found in the catalog."));
        }

        public Task<ModuleInstallChange> InstallModuleAsync(string @namespace, CancellationToken cancellationToken) {
            InstallCalls++;
            if (AnswerInstallModule != null) return AnswerInstallModule(@namespace, cancellationToken);
            CatalogModuleInfo existing = CatalogModules.FirstOrDefault(m =>
                string.Equals(m.Namespace, @namespace, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m.Name, @namespace, StringComparison.OrdinalIgnoreCase));
            string name = existing?.Name ?? @namespace;
            string ns = existing?.Namespace ?? @namespace;
            string version = existing?.Version ?? "1.0.0";
            string desc = existing?.Description ?? string.Empty;
            if (Modules.Any(m => string.Equals(m.Namespace, ns, StringComparison.OrdinalIgnoreCase))) {
                return Task.FromResult(new ModuleInstallChange(false, false, name, ns, version, name + " is already installed."));
            }
            Modules.Add(new InstalledModuleInfo(ns, name, version, desc, enabled: false));
            Writes.Add("install " + ns);
            return Task.FromResult(new ModuleInstallChange(true, true, name, ns, version));
        }

        private void RestartPathingIfEnabled() {
            InstalledModuleInfo pathing = Modules.FirstOrDefault(m =>
                (Same(m.Namespace, "bh.community.pathing") || Same(m.Namespace, "bh.pathing") || Same(m.Name, "Pathing")) && m.Enabled);
            if (pathing != null) {
                Writes.Add("disable " + pathing.Namespace);
                Writes.Add("enable " + pathing.Namespace);
            }
        }
    }
}
