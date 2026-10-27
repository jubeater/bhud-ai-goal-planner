using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// The <c>get_module_settings</c> Lookup: one Module's Settings, with their labels, values and
    /// ranges, nested ones included, under their qualified keys. What may reach the model is decided
    /// by <see cref="SettingWalk"/>: Settings that look like secrets are removed there, and the
    /// Settings of AI Goal Planner itself are never shared.
    /// </summary>
    public static class GetModuleSettingsTool
    {
        public const string Name = "get_module_settings";

        /// <summary>
        /// This module's own namespace. Its Settings configure the Chat and the Provider; they are
        /// no business of the model that runs inside them.
        /// </summary>
        public const string ThisModuleNamespace = "jubeater.aigoalplanner";

        /// <summary>
        /// The one argument the tool takes. The schema offers it under this name, and the function
        /// reads it back out of the call's arguments, so both share the spelling here.
        /// </summary>
        private const string NamespaceArgument = "namespace";

        private const string Description =
            "Reads the settings of one installed Blish HUD module: key, label, type, current value and "
          + "the allowed range. Pass the module's namespace from list_installed_modules. A disabled "
          + "module answers that it has to be enabled first; the settings of AI Goal Planner itself "
          + "are never shared.";

        private const string SchemaJson =
            "{\"type\":\"object\",\"properties\":{\"namespace\":{\"type\":\"string\","
          + "\"description\":\"The namespace of the Module, from list_installed_modules.\"}},"
          + "\"required\":[\"namespace\"]}";

        private static readonly JsonDocument Schema = JsonDocument.Parse(SchemaJson);

        /// <summary>
        /// Builds the tool against <paramref name="world"/>, the host that answers the Lookup. It is
        /// a hand-written <see cref="AIFunction"/> with a literal schema, per ADR-0008: the
        /// serializer-driven factory needs a System.Text.Json newer than the one Blish HUD runs.
        /// </summary>
        public static AIFunction Create(IBlishHudWorld world) {
            return new Function(world);
        }

        /// <summary>
        /// The progress line the Chat shows while this Lookup runs, naming the Module when the call
        /// already says which one it wants.
        /// </summary>
        public static string Progress(IDictionary<string, object> arguments) {
            if (arguments != null && arguments.TryGetValue(NamespaceArgument, out object value)
                                  && value is string moduleNamespace
                                  && moduleNamespace.Length > 0) {
                return "Reading the settings of " + moduleNamespace + "…";
            }

            return "Reading module settings…";
        }

        private static async Task<string> RunAsync(IBlishHudWorld world, string @namespace, CancellationToken cancellationToken) {
            if (string.Equals(@namespace?.Trim(), ThisModuleNamespace, StringComparison.OrdinalIgnoreCase)) {
                return Answer(ModuleSettingsStatus.ThisModule, "AI Goal Planner", Array.Empty<ModuleSettingInfo>()).ToString(Formatting.None);
            }

            ModuleSettingsResult result = await world.GetModuleSettingsAsync(@namespace, cancellationToken)
                                                     .ConfigureAwait(false);

            IReadOnlyList<ModuleSettingInfo> settings = result.Status == ModuleSettingsStatus.Ok
                                                            ? SettingWalk.Flatten(result.Settings)
                                                            : Array.Empty<ModuleSettingInfo>();

            return Answer(result.Status, result.ModuleName, settings).ToString(Formatting.None);
        }

        /// <summary>
        /// The tool's answer in one shape: what was found, or the status that says why there is
        /// nothing to read.
        /// </summary>
        private static JObject Answer(ModuleSettingsStatus status, string moduleName, IReadOnlyList<ModuleSettingInfo> settings) {
            var json = new JObject {
                ["module"] = moduleName
            };

            switch (status) {
                case ModuleSettingsStatus.Ok:
                    json["status"]     = "ok";
                    json["settings"]   = new JArray(settings.Select(ToJson));
                    break;

                case ModuleSettingsStatus.ModuleNotInstalled:
                    json["status"]      = "not_installed";
                    json["instruction"] =
                        "No installed module has that namespace. Call " + ListInstalledModulesTool.Name +
                        " to see what the Player has installed.";
                    break;

                case ModuleSettingsStatus.ModuleDisabled:
                    json["status"]      = "disabled";
                    json["instruction"] =
                        "This module is disabled. The Player has to enable it in Blish HUD before its "
                      + "settings can be read; suggest enabling it if the request needs it.";
                    break;

                default:
                    json["status"]      = "private";
                    json["instruction"] =
                        "These are the settings of AI Goal Planner itself. They are never shared with "
                      + "the model, and they are not part of what can be configured for the Player.";
                    break;
            }

            return json;
        }

        private static JObject ToJson(ModuleSettingInfo setting) {
            var json = new JObject {
                ["key"]         = setting.Key,
                ["label"]       = setting.DisplayName,
                ["description"] = setting.Description,
                ["type"]        = setting.TypeName,
                ["value"]       = setting.Value
            };

            if (setting.Min != null) json["min"] = setting.Min;
            if (setting.Max != null) json["max"] = setting.Max;

            return json;
        }

        private sealed class Function : LookupFunction
        {
            private readonly IBlishHudWorld _world;

            public Function(IBlishHudWorld world) : base(GetModuleSettingsTool.Name, GetModuleSettingsTool.Description, Schema) {
                _world = world;
            }

            protected override async Task<object> InvokeLookupAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) {
                string @namespace = arguments != null && arguments.TryGetValue(NamespaceArgument, out object value)
                                        ? value as string
                                        : null;

                return await RunAsync(_world, @namespace, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
