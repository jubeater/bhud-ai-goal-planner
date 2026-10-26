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
        /// answers them.
        /// </summary>
        public static ToolRegistry For(IBlishHudWorld world) {
            var tools = new ToolRegistry();

            tools.Register(ToolAccess.ReadOnly, ListInstalledModulesTool.Create(world), ListInstalledModulesTool.Progress);
            tools.Register(ToolAccess.ReadOnly, GetModuleSettingsTool.Create(world), GetModuleSettingsTool.Progress);

            return tools;
        }
    }
}
