using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// The <c>get_module_details</c> Lookup: retrieves detailed information for one candidate module
    /// from the official Blish HUD catalog, including its description and requested GW2 API permissions.
    /// </summary>
    public static class GetModuleDetailsTool
    {
        public const string Name = "get_module_details";

        public const string ModuleArgument = "module";
        public const string NamespaceArgument = "namespace";

        private const string Description =
            "Gets detailed information for one candidate module from the official Blish HUD catalog, "
          + "including its description and requested GW2 API permissions.";

        private const string SchemaJson =
            "{\"type\":\"object\",\"properties\":{"
          + "\"module\":{\"type\":\"string\",\"description\":\"The namespace of the module or its display name from the catalog.\"}"
          + "},\"required\":[\"module\"]}";

        private static readonly JsonDocument Schema = JsonDocument.Parse(SchemaJson);

        /// <summary>
        /// Builds the tool against <paramref name="world"/>, the host that answers the Lookup. It is
        /// a hand-written <see cref="AIFunction"/> with a literal schema, per ADR-0008.
        /// </summary>
        public static AIFunction Create(IBlishHudWorld world, Action<string> logDebug = null) {
            return new Function(world, logDebug);
        }

        /// <summary>
        /// The progress line the Chat shows while this Lookup runs.
        /// </summary>
        public static string Progress(IDictionary<string, object> arguments) {
            string module = ExtractModule(arguments);

            if (!string.IsNullOrWhiteSpace(module)) {
                return "Checking details for " + module.Trim() + "…";
            }

            return "Checking module details…";
        }

        private static async Task<LookupAnswer> RunAsync(IBlishHudWorld world, string module, CancellationToken cancellationToken) {
            string wanted = (module ?? string.Empty).Trim();
            if (wanted.Length == 0) {
                var missingJson = new JObject {
                    ["status"]  = "not_found",
                    ["message"] = "No module was specified."
                };
                return new LookupAnswer(missingJson.ToString(Formatting.None), "status=not_found");
            }

            CatalogModuleDetails details = await world.GetCatalogModuleDetailsAsync(wanted, cancellationToken).ConfigureAwait(false);

            if (details == null || !details.Success) {
                string status = (details?.Problem != null && details.Problem.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0)
                    ? "not_found"
                    : "error";

                var errJson = new JObject {
                    ["status"]  = status,
                    ["module"]  = wanted,
                    ["message"] = details?.Problem ?? "Could not retrieve module details."
                };

                return new LookupAnswer(errJson.ToString(Formatting.None), "status=" + status + ", module=" + wanted);
            }

            var json = new JObject {
                ["status"]          = "ok",
                ["namespace"]       = details.Namespace,
                ["name"]            = details.Name,
                ["version"]         = details.Version,
                ["description"]     = details.Description,
                ["api_permissions"] = new JArray(details.ApiPermissions)
            };

            return new LookupAnswer(json.ToString(Formatting.None), "status=ok, module=" + details.Namespace + ", perms=" + details.ApiPermissions.Count);
        }

        private static string ExtractModule(IDictionary<string, object> arguments) {
            if (arguments == null) return null;

            if (arguments.TryGetValue(ModuleArgument, out object mVal) && mVal != null) {
                return mVal.ToString();
            }

            if (arguments.TryGetValue(NamespaceArgument, out object nsVal) && nsVal != null) {
                return nsVal.ToString();
            }

            return null;
        }

        private sealed class Function : LookupFunction
        {
            private readonly IBlishHudWorld _world;

            public Function(IBlishHudWorld world, Action<string> logDebug)
                : base(GetModuleDetailsTool.Name, GetModuleDetailsTool.Description, Schema, logDebug) {
                _world = world;
            }

            protected override async Task<LookupAnswer> InvokeLookupAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) {
                string module = ExtractModule(arguments);

                return await RunAsync(_world, module, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
