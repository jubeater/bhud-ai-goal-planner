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
    /// The <c>find_marker_categories</c> Lookup: finds Pathing marker categories matching a goal
    /// (e.g. hero points or routes) from installed marker packs, reporting their display path,
    /// current visibility in Pathing, and marker/trail counts on the target or current map.
    /// </summary>
    public static class FindMarkerCategoriesTool
    {
        public const string Name = "find_marker_categories";

        public const string SearchArgument = "search";
        public const string MapArgument    = "map";

        private const string Description =
            "Searches installed Pathing marker packs for categories matching a goal (e.g. 'hero points' or routes). "
          + "Returns each matching category's pack name, full display path to turn on in Pathing, namespace, "
          + "whether it is currently shown, and marker/trail counts on the specified or current map.";

        private const string SchemaJson =
            "{\"type\":\"object\",\"properties\":{"
          + "\"search\":{\"type\":\"string\",\"description\":\"The search text to match against category display paths or namespaces (e.g. 'hero points', 'tekkit').\"},"
          + "\"map\":{\"type\":\"integer\",\"description\":\"Optional map ID to count markers and trails for. Defaults to the Player's current map.\"}"
          + "},\"required\":[\"search\"]}";

        private static readonly JsonDocument Schema = JsonDocument.Parse(SchemaJson);

        public static AIFunction Create(IBlishHudWorld world, Action<string> logDebug = null) {
            return new Function(world, logDebug);
        }

        public static string Progress(IDictionary<string, object> arguments) {
            if (arguments != null && arguments.TryGetValue(SearchArgument, out object val)) {
                string searchStr = ExtractStringValue(val);
                if (!string.IsNullOrWhiteSpace(searchStr)) {
                    return "Searching marker categories for '" + searchStr.Trim() + "'…";
                }
            }

            return "Searching marker categories…";
        }

        private static async Task<LookupAnswer> RunAsync(IBlishHudWorld world, string search, int? mapId, CancellationToken cancellationToken) {
            MarkerCategoriesResult result = await world.FindMarkerCategoriesAsync(search, mapId, cancellationToken).ConfigureAwait(false);

            if (result.Status == MarkerCategoriesStatus.NoPathing) {
                var noPathing = new JObject {
                    ["status"]  = "no_pathing",
                    ["message"] = result.Message
                };

                return new LookupAnswer(noPathing.ToString(Formatting.None), "status=no_pathing");
            }

            if (result.Status == MarkerCategoriesStatus.NoPacks) {
                var noPacks = new JObject {
                    ["status"]  = "no_packs",
                    ["message"] = result.Message
                };

                return new LookupAnswer(noPacks.ToString(Formatting.None), "status=no_packs");
            }

            var categoriesArray = new JArray();
            foreach (MarkerCategoryMatch match in result.Categories) {
                categoriesArray.Add(new JObject {
                    ["pack"]         = match.PackName,
                    ["display_path"] = match.DisplayPath,
                    ["namespace"]    = match.Namespace,
                    ["shown"]        = match.Shown,
                    ["markers"]      = match.MarkerCount,
                    ["trails"]       = match.TrailCount,
                    ["count"]        = match.TotalCount
                });
            }

            var json = new JObject {
                ["status"]        = "ok",
                ["map_id"]        = result.MapId,
                ["map_name"]      = result.MapName,
                ["total_matches"] = result.TotalMatches,
                ["cut"]           = result.Cut,
                ["categories"]    = categoriesArray
            };

            if (result.Cut && !string.IsNullOrEmpty(result.Message)) {
                json["message"] = result.Message;
            }

            if (result.SkippedPacks != null && result.SkippedPacks.Count > 0) {
                json["skipped_packs"] = new JArray(result.SkippedPacks);
            }

            string logDetail = "matches=" + result.Categories.Count + ", total=" + result.TotalMatches;

            return new LookupAnswer(json.ToString(Formatting.None), logDetail);
        }

        private static string ExtractStringValue(object val) {
            if (val == null) return null;
            if (val is string s) return s;
            if (val is JsonElement je && je.ValueKind == JsonValueKind.String) return je.GetString();
            return val.ToString();
        }

        private static int? ExtractIntValue(object val) {
            if (val == null) return null;
            if (val is int i) return i;
            if (val is long l) return (int)l;
            if (val is string s && int.TryParse(s, out int parsed)) return parsed;
            if (val is JsonElement je) {
                if (je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out int jeInt)) return jeInt;
                if (je.ValueKind == JsonValueKind.String && int.TryParse(je.GetString(), out int jeStrInt)) return jeStrInt;
            }
            return null;
        }

        private sealed class Function : LookupFunction
        {
            private readonly IBlishHudWorld _world;

            public Function(IBlishHudWorld world, Action<string> logDebug)
                : base(FindMarkerCategoriesTool.Name, FindMarkerCategoriesTool.Description, Schema, logDebug) {
                _world = world;
            }

            protected override async Task<LookupAnswer> InvokeLookupAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) {
                string search = null;
                int? mapId = null;

                if (arguments != null) {
                    if (arguments.TryGetValue(SearchArgument, out object sVal)) {
                        search = ExtractStringValue(sVal);
                    } else if (arguments.TryGetValue("query", out object qVal)) {
                        search = ExtractStringValue(qVal);
                    }

                    if (arguments.TryGetValue(MapArgument, out object mVal)) {
                        mapId = ExtractIntValue(mVal);
                    } else if (arguments.TryGetValue("map_id", out object mIdVal)) {
                        mapId = ExtractIntValue(mIdVal);
                    }
                }

                return await RunAsync(_world, search, mapId, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
