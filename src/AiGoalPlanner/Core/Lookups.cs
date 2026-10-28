using System;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// The Lookups this module offers the AI: one registry with every Lookup of this version,
    /// wired to one world. The module and the tests build their Chat the same way through this.
    /// </summary>
    public static class Lookups
    {
        /// <summary>
        /// A registry of this version's Lookups against <paramref name="world"/>, the host that
        /// answers them. <paramref name="logDebug"/> is where each Lookup's one line of diagnostics
        /// goes — its name, how long it took, what it read and how big the answer is — or null when
        /// nobody is listening.
        /// </summary>
        public static ToolRegistry For(IBlishHudWorld world, Action<string> logDebug = null) {
            var tools = new ToolRegistry();

            tools.Register(ToolAccess.ReadOnly, ListInstalledModulesTool.Create(world, logDebug), ListInstalledModulesTool.Progress);
            tools.Register(ToolAccess.ReadOnly, GetModuleSettingsTool.Create(world, logDebug), GetModuleSettingsTool.Progress);
            tools.Register(ToolAccess.ReadOnly, GetLocationTool.Create(world, logDebug), GetLocationTool.Progress);
            tools.Register(ToolAccess.ReadOnly, GetAccountAccessTool.Create(world, logDebug), GetAccountAccessTool.Progress);
            tools.Register(ToolAccess.ReadOnly, GetProgressTool.Create(world, logDebug), GetProgressTool.Progress);
            tools.Register(ToolAccess.ReadOnly, FindMarkerCategoriesTool.Create(world, logDebug), FindMarkerCategoriesTool.Progress);
            tools.Register(ToolAccess.ReadOnly, ListMarkerPacksTool.Create(world, logDebug), ListMarkerPacksTool.Progress);
            tools.Register(ToolAccess.ReadOnly, SearchCatalogTool.Create(world, logDebug), SearchCatalogTool.Progress);
            tools.Register(ToolAccess.ReadOnly, GetModuleDetailsTool.Create(world, logDebug), GetModuleDetailsTool.Progress);

            return tools;
        }
    }
}
