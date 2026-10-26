using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// The <c>list_installed_modules</c> Lookup: what Modules the Player has installed, enabled and
    /// disabled. It is the first Lookup the AI should run for anything about the Player's setup, and
    /// it is where a namespace for <see cref="GetModuleSettingsTool"/> comes from.
    /// </summary>
    public static class ListInstalledModulesTool
    {
        public const string Name = "list_installed_modules";

        private const string Description =
            "Lists every Blish HUD module the Player has installed, including disabled ones, with its "
          + "namespace, name, version, short description and whether it is enabled. Call this before "
          + "you propose anything about the Player's setup, and to find the namespace to read a "
          + "module's settings with.";

        /// <summary>
        /// Builds the tool against <paramref name="world"/>, the host that answers the Lookup.
        /// </summary>
        public static AIFunction Create(IBlishHudWorld world) {
            return AIFunctionFactory.Create((Func<CancellationToken, Task<string>>)(
                                               cancellationToken => RunAsync(world, cancellationToken)),
                                           Name, Description);
        }

        /// <summary>
        /// The progress line the Chat shows while this Lookup runs.
        /// </summary>
        public static string Progress(IDictionary<string, object> arguments) {
            return "Checking installed modules…";
        }

        private static async Task<string> RunAsync(IBlishHudWorld world, CancellationToken cancellationToken) {
            IReadOnlyList<InstalledModuleInfo> modules = await world.ListInstalledModulesAsync(cancellationToken)
                                                                   .ConfigureAwait(false);

            var json = new JObject {
                ["status"] = "ok",
                ["modules"] = new JArray(modules.Select(ToJson))
            };

            return json.ToString(Formatting.None);
        }

        private static JObject ToJson(InstalledModuleInfo module) {
            return new JObject {
                ["namespace"]   = module.Namespace,
                ["name"]        = module.Name,
                ["version"]     = module.Version,
                ["enabled"]     = module.Enabled,
                ["description"] = module.Description
            };
        }
    }
}
