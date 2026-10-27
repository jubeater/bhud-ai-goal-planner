using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;

namespace jubeater.aigoalplanner.Tests.Fakes
{
    /// <summary>
    /// An in-memory world for tests: the Player's modules and their settings are whatever the test
    /// puts in, and a change made through it really changes them. A test can also take over
    /// answering a Lookup or a write itself, to hold it open, fail it or count it.
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

        public int ListCalls      { get; private set; }
        public int SettingsCalls  { get; private set; }
        public int ModuleWrites   { get; private set; }
        public int SettingWrites  { get; private set; }

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
    }
}
