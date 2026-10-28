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
    /// The half of the real Blish HUD host that writes: enabling or disabling a Module, changing a
    /// Setting, and the small lookups a write needs to find the thing it is asked to change.
    /// </summary>
    /// <remarks>
    /// It is the same class in another file, so that the reads and the writes of the Player's
    /// configuration can be read on their own. Like the reads, none of this runs on the Chat's thread:
    /// <see cref="GameThreadWorld"/> hands each call to the game thread first, because enabling a
    /// Module and writing a Setting run that Module's own code (ADR-0009).
    /// </remarks>
    public sealed partial class BlishHudWorld
    {
        public Task<ModuleChange> DeleteModuleAsync(string @namespace, CancellationToken cancellationToken) {
            string wanted = (@namespace ?? string.Empty).Trim();

            if (string.Equals(wanted, GetModuleSettingsTool.ThisModuleNamespace, StringComparison.OrdinalIgnoreCase)) {
                return Task.FromResult(new ModuleChange(false, true, "AI Goal Planner cannot delete itself.", changed: false));
            }

            ModuleManager manager = Find(wanted);

            if (manager == null) {
                return Task.FromResult(new ModuleChange(false, false, "The module is not installed.", changed: false));
            }

            try {
                bool wasEnabled = manager.Enabled;
                manager.DeleteModule();
                return Task.FromResult(new ModuleChange(true, wasEnabled, null, changed: true));
            } catch (Exception ex) {
                _logWarn?.Invoke("DeleteModule failed: " + ex.GetType().Name);
                return Task.FromResult(new ModuleChange(false, manager.Enabled, "Failed to delete module: " + ex.GetType().Name, changed: false));
            }
        }

        public Task<ModuleChange> SetModuleEnabledAsync(string @namespace, bool enabled, CancellationToken cancellationToken) {
            string        wanted  = (@namespace ?? string.Empty).Trim();
            ModuleManager manager = Find(wanted);

            if (manager == null) {
                return Task.FromResult(new ModuleChange(false, false, "No installed module has that namespace."));
            }

            // Switching itself off would take the Chat with it, and its own settings are not the
            // Player's to configure through it either.
            if (string.Equals(manager.Manifest.Namespace, GetModuleSettingsTool.ThisModuleNamespace, StringComparison.OrdinalIgnoreCase)) {
                return Task.FromResult(new ModuleChange(false, manager.Enabled,
                                                        "AI Goal Planner does not enable or disable itself."));
            }

            bool wasEnabled = manager.Enabled;

            // Already in the state the Action asks for: nothing to do, and nothing to undo.
            if (wasEnabled == enabled) return Task.FromResult(new ModuleChange(true, wasEnabled, changed: false));

            try {
                if (enabled) {
                    // Blish HUD answers false when the module cannot load: a dependency is missing,
                    // or it threw while starting. Either way it is not running now.
                    if (!manager.TryEnable()) {
                        return Task.FromResult(new ModuleChange(false, wasEnabled,
                                                                "Blish HUD could not load " + manager.Manifest.Name + "."));
                    }
                } else {
                    manager.Disable();
                }
            } catch (Exception exception) {
                return Task.FromResult(new ModuleChange(false, wasEnabled,
                                                        "Blish HUD reported an error (" + exception.GetType().Name + ")."));
            }

            return Task.FromResult(new ModuleChange(true, wasEnabled));
        }

        public Task<ModuleSettingChange> SetSettingAsync(string @namespace, string key, string value,
                                                         CancellationToken cancellationToken) {
            string wanted = (@namespace ?? string.Empty).Trim();

            if (string.Equals(wanted, GetModuleSettingsTool.ThisModuleNamespace, StringComparison.OrdinalIgnoreCase)) {
                return NotApplied("AI Goal Planner's own settings are never changed for the Player.");
            }

            ModuleManager manager = Find(wanted);

            if (manager == null) return NotApplied("No installed module has that namespace.");

            if (!manager.Enabled) return NotApplied(manager.Manifest.Name + " is disabled, so its settings cannot be changed.");

            SettingCollection settings = SettingsOf(manager);

            if (settings == null) return NotApplied("The settings of " + manager.Manifest.Name + " cannot be read right now.");

            SettingEntry entry = FindEntry(settings, (key ?? string.Empty).Trim(), string.Empty);

            if (entry == null) return NotApplied(manager.Manifest.Name + " has no setting with that key.");

            string previous = ValueTextOf(entry);

            PropertyInfo valueProperty = entry.GetType().GetProperty("Value");

            if (valueProperty == null) return NotApplied("That setting cannot be written.");

            object newValue;

            try {
                newValue = AsValueOf(entry.SettingType, value);
            } catch (Exception exception) {
                return NotApplied("The module did not accept the value (" + exception.GetType().Name + ").");
            }

            // A Setting that already holds that value is not a change: nothing is written, and
            // nothing goes into the Undo journal (ADR-0010).
            if (Equals(ValueOf(entry), newValue)) {
                return Task.FromResult(new ModuleSettingChange(true, previous, changed: false));
            }

            try {
                valueProperty.SetValue(entry, newValue, null);
            } catch (Exception exception) {
                // The type, not the message: a message can carry a Setting's value back out.
                return NotApplied("The module did not accept the value (" + exception.GetType().Name + ").");
            }

            return Task.FromResult(new ModuleSettingChange(true, previous));
        }

        /// <summary>
        /// The installed Module with that namespace, or null.
        /// </summary>
        private static ModuleManager Find(string @namespace) {
            return GameService.Module.Modules
                               .FirstOrDefault(candidate => string.Equals(candidate.Manifest.Namespace, @namespace,
                                                                          StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The Setting a qualified key names: "Markers.Trail size" is the "Trail size" entry inside
        /// the "Markers" subcollection. Null when the Module has no such Setting.
        /// </summary>
        private static SettingEntry FindEntry(SettingCollection settings, string key, string prefix) {
            if (key.Length == 0) return null;

            foreach (SettingEntry entry in settings.Entries) {
                string qualified = prefix.Length == 0 ? entry.EntryKey : prefix + "." + entry.EntryKey;

                if (ValueOf(entry) is SettingCollection nested) {
                    SettingEntry found = FindEntry(nested, key, qualified);

                    if (found != null) return found;

                    continue;
                }

                if (string.Equals(qualified, key, StringComparison.OrdinalIgnoreCase)) return entry;
            }

            return null;
        }

        /// <summary>
        /// Text back into the type a Setting holds: the reverse of <see cref="ValueTextOf"/>, so a
        /// value read from a Lookup can be written back unchanged.
        /// </summary>
        private static object AsValueOf(Type settingType, string text) {
            if (settingType == null || settingType == typeof(string)) return text ?? string.Empty;
            if (settingType == typeof(bool)) return bool.Parse((text ?? string.Empty).Trim());

            if (settingType.IsEnum) return Enum.Parse(settingType, (text ?? string.Empty).Trim(), ignoreCase: true);

            return Convert.ChangeType((text ?? string.Empty).Trim(), settingType, CultureInfo.InvariantCulture);
        }

        private static Task<ModuleSettingChange> NotApplied(string problem) {
            return Task.FromResult(new ModuleSettingChange(false, null, problem));
        }

        /// <summary>
        /// The values an enumerated Setting accepts. A module's own enum is a type inside that
        /// module's assembly, so its names are read by reflection through the Setting's type.
        /// </summary>
        private static IReadOnlyList<string> AllowedValuesOf(SettingEntry entry) {
            Type settingType = entry.SettingType;

            return settingType != null && settingType.IsEnum ? Enum.GetNames(settingType) : null;
        }
    }
}
