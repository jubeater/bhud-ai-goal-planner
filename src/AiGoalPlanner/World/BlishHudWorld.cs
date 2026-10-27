using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Modules;
using Blish_HUD.Settings;
using jubeater.aigoalplanner.Core;

namespace jubeater.aigoalplanner.World
{
    /// <summary>
    /// The real Blish HUD host, as the Lookups see it: the modules registered with Blish HUD and the
    /// settings of the ones that are enabled.
    /// </summary>
    /// <remarks>
    /// The Lookups run while the model thinks, on a thread that is not the game's update thread, so
    /// this is never called from there: <see cref="GameThreadWorld"/> queues each read on the game
    /// thread first, because Blish HUD and the Modules expect their state to be read on it. Reading
    /// touches no control, so it changes nothing the Player can see; it only has to happen on the
    /// right thread.
    /// </remarks>
    public sealed class BlishHudWorld : IBlishHudWorld
    {
        private static readonly IReadOnlyList<ModuleSettingInfo> NoSettings = new ModuleSettingInfo[0];

        /// <summary>
        /// The module instance's own parameters. The module infrastructure keeps them protected, and
        /// the settings live behind them: this is the one way in from the outside.
        /// </summary>
        private static readonly FieldInfo ModuleParametersField = typeof(Blish_HUD.Modules.Module).GetField("ModuleParameters",
                                                                                                            BindingFlags.NonPublic | BindingFlags.Instance);

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
                return Task.FromResult(new ModuleSettingsResult(ModuleSettingsStatus.ThisModule, "AI Goal Planner", NoSettings));
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
                return Task.FromResult(new ModuleSettingsResult(ModuleSettingsStatus.ModuleDisabled, manager.Manifest.Name, NoSettings));
            }

            SettingCollection settings = SettingsOf(manager);

            if (settings == null) {
                // Enabled, but its instance is not running yet (still loading), so its settings are
                // not readable either. The same answer applies until it is.
                return Task.FromResult(new ModuleSettingsResult(ModuleSettingsStatus.ModuleDisabled, manager.Manifest.Name, NoSettings));
            }

            var entries = new List<ModuleSettingInfo>();

            foreach (SettingEntry entry in settings.Entries) {
                ModuleSettingInfo info = ToSettingInfo(entry);

                if (info != null) entries.Add(info);
            }

            return Task.FromResult(new ModuleSettingsResult(ModuleSettingsStatus.Ok, manager.Manifest.Name, entries));
        }

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
                                             RangeBoundOf(entry, "MaxValue"));
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
