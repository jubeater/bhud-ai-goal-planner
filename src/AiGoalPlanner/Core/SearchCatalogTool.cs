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
    /// The <c>search_catalog</c> Lookup: searches or lists modules available in the official Blish HUD
    /// module catalog that are supported by this Blish HUD version.
    /// </summary>
    public static class SearchCatalogTool
    {
        public const string Name = "search_catalog";

        public const string QueryArgument = "query";
        public const string SearchArgument = "search";

        private const string Description =
            "Searches the official Blish HUD module catalog for modules that can be installed when no "
          + "installed module covers the Player's goal. Returns each module's name, namespace, latest "
          + "supported version, dependencies and preview flag.";

        private const string SchemaJson =
            "{\"type\":\"object\",\"properties\":{"
          + "\"query\":{\"type\":\"string\",\"description\":\"Optional search text to filter catalog modules by name or namespace.\"}"
          + "}}";

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
            string query = ExtractQuery(arguments);

            if (!string.IsNullOrWhiteSpace(query)) {
                return "Searching module catalog for '" + query.Trim() + "'…";
            }

            return "Checking module catalog…";
        }

        private static async Task<LookupAnswer> RunAsync(IBlishHudWorld world, string query, CancellationToken cancellationToken) {
            IReadOnlyList<CatalogModuleInfo> modules = await world.GetCatalogAsync(cancellationToken).ConfigureAwait(false);

            IEnumerable<CatalogModuleInfo> filtered = modules ?? Array.Empty<CatalogModuleInfo>();
            string trimmed = (query ?? string.Empty).Trim();

            if (trimmed.Length > 0) {
                filtered = filtered.Where(m =>
                    m.Name.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.Namespace.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var moduleList = filtered.ToList();

            var json = new JObject {
                ["status"]        = "ok",
                ["total_modules"] = moduleList.Count,
                ["modules"]       = new JArray(moduleList.Select(ToJson))
            };

            return new LookupAnswer(json.ToString(Formatting.None), "modules=" + moduleList.Count);
        }

        private static JObject ToJson(CatalogModuleInfo module) {
            var obj = new JObject {
                ["namespace"]    = module.Namespace,
                ["name"]         = module.Name,
                ["version"]      = module.Version,
                ["dependencies"] = new JArray(module.Dependencies),
                ["is_preview"]   = module.IsPreview
            };

            return obj;
        }

        private static string ExtractQuery(IDictionary<string, object> arguments) {
            if (arguments == null) return null;

            if (arguments.TryGetValue(QueryArgument, out object qVal) && qVal != null) {
                return qVal.ToString();
            }

            if (arguments.TryGetValue(SearchArgument, out object sVal) && sVal != null) {
                return sVal.ToString();
            }

            return null;
        }

        private sealed class Function : LookupFunction
        {
            private readonly IBlishHudWorld _world;

            public Function(IBlishHudWorld world, Action<string> logDebug)
                : base(SearchCatalogTool.Name, SearchCatalogTool.Description, Schema, logDebug) {
                _world = world;
            }

            protected override async Task<LookupAnswer> InvokeLookupAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) {
                string query = ExtractQuery(arguments);

                return await RunAsync(_world, query, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
