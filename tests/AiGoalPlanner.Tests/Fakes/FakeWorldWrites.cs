using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;

namespace jubeater.aigoalplanner.Tests.Fakes
{
    /// <summary>
    /// The writing half of the in-memory world: a change made through it really changes what the
    /// next read answers, and what it did is kept in <see cref="Writes"/> for a test to check.
    /// </summary>
    internal sealed partial class FakeWorld
    {
        public Task<ModuleChange> DeleteModuleAsync(string @namespace, CancellationToken cancellationToken) {
            DeleteModuleCalls++;
            Writes.Add("delete " + @namespace);

            Func<string, ModuleChange> answer = AnswerDeleteModule;
            if (answer != null) return Task.FromResult(answer(@namespace));

            int index = Modules.FindIndex(candidate => Same(candidate.Namespace, @namespace));
            if (index < 0) {
                return Task.FromResult(new ModuleChange(false, false, "No installed module has that namespace."));
            }

            InstalledModuleInfo module = Modules[index];
            Modules.RemoveAt(index);
            return Task.FromResult(new ModuleChange(true, module.Enabled, changed: true));
        }

        public Task<ModuleChange> SetModuleEnabledAsync(string @namespace, bool enabled, CancellationToken cancellationToken) {
            ModuleWrites++;

            Writes.Add((enabled ? "enable " : "disable ") + @namespace);

            Func<string, bool, ModuleChange> answer = AnswerSetModuleEnabled;

            if (answer != null) return Task.FromResult(answer(@namespace, enabled));

            int index = Modules.FindIndex(candidate => Same(candidate.Namespace, @namespace));

            if (index < 0) {
                return Task.FromResult(new ModuleChange(false, false, "No installed module has that namespace."));
            }

            InstalledModuleInfo module     = Modules[index];
            bool                wasEnabled = module.Enabled;

            if (wasEnabled == enabled) return Task.FromResult(new ModuleChange(true, wasEnabled, changed: false));

            Modules[index] = new InstalledModuleInfo(module.Namespace, module.Name, module.Version, module.Description, enabled);

            return Task.FromResult(new ModuleChange(true, wasEnabled));
        }

        public Task<ModuleSettingChange> SetSettingAsync(string @namespace, string key, string value,
                                                         CancellationToken cancellationToken) {
            SettingWrites++;

            Writes.Add("set " + @namespace + "/" + key + "=" + value);

            Func<string, string, string, ModuleSettingChange> answer = AnswerSetSetting;

            if (answer != null) return Task.FromResult(answer(@namespace, key, value));

            if (!SettingsByNamespace.TryGetValue(@namespace ?? string.Empty, out ModuleSettingsResult result)
             || result.Status != ModuleSettingsStatus.Ok) {
                return Task.FromResult(new ModuleSettingChange(false, null, "The settings of that module cannot be read."));
            }

            if (!TryWrite(result.Settings, string.Empty, key, value, out string previous, out IReadOnlyList<ModuleSettingInfo> updated)) {
                return Task.FromResult(new ModuleSettingChange(false, null, "That module has no such setting."));
            }

            // The same no-op the real world makes: a Setting that already holds the value is not a
            // change, so it leaves no record for Undo.
            if (string.Equals(previous, value, StringComparison.Ordinal)) {
                return Task.FromResult(new ModuleSettingChange(true, previous, changed: false));
            }

            SettingsByNamespace[@namespace] = new ModuleSettingsResult(result.Status, result.ModuleName, updated,
                                                                       result.Namespace);

            return Task.FromResult(new ModuleSettingChange(true, previous));
        }

        /// <summary>
        /// Writes <paramref name="value"/> into the Setting at <paramref name="key"/>, nested ones
        /// included under their qualified key, the way the real world writes back what a Lookup
        /// read. Says whether there was such a Setting, what it held, and what the Settings are now.
        /// </summary>
        private static bool TryWrite(IReadOnlyList<ModuleSettingInfo> settings, string prefix, string key, string value,
                                     out string previous, out IReadOnlyList<ModuleSettingInfo> updated) {
            var replaced = new List<ModuleSettingInfo>(settings.Count);

            previous = null;
            updated  = null;

            bool   found  = false;
            string before = null;

            foreach (ModuleSettingInfo setting in settings) {
                string qualified = prefix.Length == 0 ? setting.Key : prefix + "." + setting.Key;

                if (!found && setting.IsSubcollection) {
                    if (TryWrite(setting.Children, qualified, key, value, out string childBefore, out IReadOnlyList<ModuleSettingInfo> children)) {
                        found  = true;
                        before = childBefore;

                        replaced.Add(ModuleSettingInfo.Subcollection(setting.Key, setting.DisplayName, setting.Description, children));
                    } else {
                        replaced.Add(setting);
                    }

                    continue;
                }

                if (!found && Same(qualified, key)) {
                    found  = true;
                    before = setting.Value;

                    replaced.Add(new ModuleSettingInfo(setting.Key, setting.DisplayName, setting.Description, setting.TypeName,
                                                       value, setting.Min, setting.Max, setting.AllowedValues));
                    continue;
                }

                replaced.Add(setting);
            }

            if (!found) return false;

            previous = before;
            updated  = replaced;

            return true;
        }

        private static bool Same(string left, string right) {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
