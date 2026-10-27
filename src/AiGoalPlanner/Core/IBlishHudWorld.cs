using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// The Blish HUD host as the Lookups see it: what Modules the Player has installed and what a
    /// Module's Settings are. This is the port. <c>GameThreadWorld</c> is the decorator every
    /// Lookup goes through, which reads on the game thread (ADR-0009); <c>World.BlishHudWorld</c> is
    /// the adapter that asks the real Blish HUD, and tests drive the Lookups with an in-memory world.
    /// </summary>
    /// <remarks>
    /// A read answers from live state: it takes the state as Blish HUD holds it right now and never
    /// waits for anything else, which is what lets the decorator run the whole read on the game
    /// thread. A read that awaited something would finish somewhere else, and Blish HUD would be
    /// read off its own update thread again.
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

        public ModuleSettingInfo(string key, string displayName, string description, string typeName, string value,
                                 string min, string max) {
            if (key == null) throw new ArgumentNullException(nameof(key));

            Key         = key;
            DisplayName = displayName ?? string.Empty;
            Description = description ?? string.Empty;
            TypeName    = typeName ?? string.Empty;
            Value       = value ?? string.Empty;
            Min         = min;
            Max         = max;
            Children    = NoChildren;
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

        /// <summary>The Settings inside a subcollection, empty for a Setting.</summary>
        public IReadOnlyList<ModuleSettingInfo> Children { get; }

        /// <summary>Whether this node is a subcollection that holds other Settings.</summary>
        public bool IsSubcollection { get; }
    }
}
