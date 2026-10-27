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
    /// puts in. A test can also take over answering a Lookup itself, to hold it open or to make it
    /// fail, and can count the Lookups it was asked.
    /// </summary>
    internal sealed class FakeWorld : IBlishHudWorld
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

        public int ListCalls      { get; private set; }
        public int SettingsCalls  { get; private set; }

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

            return Task.FromResult(result);
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
