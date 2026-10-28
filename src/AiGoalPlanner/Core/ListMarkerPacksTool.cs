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
    /// The <c>list_marker_packs</c> Lookup: lists Pathing marker packs available from Pathing's official
    /// repository and installed locally in Blish HUD's markers directory, including installation status,
    /// categories, and download sizes.
    /// </summary>
    public static class ListMarkerPacksTool
    {
        public const string Name = "list_marker_packs";

        public const string SearchArgument = "search";

        private const string Description =
            "Lists available Pathing marker packs from the official repository and installed packs in the markers "
          + "directory. Returns each pack's name, file name, categories, download size in MB, and whether it is "
          + "currently installed.";

        private const string SchemaJson =
            "{\"type\":\"object\",\"properties\":{"
          + "\"search\":{\"type\":\"string\",\"description\":\"Optional search text to filter marker packs by name, category or description.\"}"
          + "}}";

        private static readonly JsonDocument Schema = JsonDocument.Parse(SchemaJson);

        public static AIFunction Create(IBlishHudWorld world, Action<string> logDebug = null) {
            return new Function(world, logDebug);
        }

        public static string Progress(IDictionary<string, object> arguments) {
            if (arguments != null && arguments.TryGetValue(SearchArgument, out object val)) {
                string searchStr = ExtractStringValue(val);
                if (!string.IsNullOrWhiteSpace(searchStr)) {
                    return "Checking marker packs for '" + searchStr.Trim() + "'…";
                }
            }

            return "Checking marker packs…";
        }

        private static async Task<LookupAnswer> RunAsync(IBlishHudWorld world, string search, CancellationToken cancellationToken) {
            MarkerPacksResult result = await world.ListMarkerPacksAsync(cancellationToken).ConfigureAwait(false);

            if (result.Status == MarkerPacksStatus.Unavailable) {
                var unavailable = new JObject {
                    ["status"]  = "unavailable",
                    ["message"] = string.IsNullOrEmpty(result.Message) ? "Unable to retrieve marker packs." : result.Message
                };

                return new LookupAnswer(unavailable.ToString(Formatting.None), "status=unavailable");
            }

            string trimmedSearch = (search ?? string.Empty).Trim();
            IEnumerable<MarkerPackInfo> packs = result.Packs;

            if (trimmedSearch.Length > 0) {
                packs = packs.Where(p =>
                    p.Name.IndexOf(trimmedSearch, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.Categories.IndexOf(trimmedSearch, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.Description.IndexOf(trimmedSearch, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.FileName.IndexOf(trimmedSearch, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var packList = packs.ToList();
            var packsArray = new JArray();
            int installedCount = 0;

            foreach (MarkerPackInfo pack in packList) {
                if (pack.Installed) installedCount++;

                packsArray.Add(new JObject {
                    ["name"]        = pack.Name,
                    ["file_name"]   = pack.FileName,
                    ["categories"]  = pack.Categories,
                    ["size_mb"]     = pack.SizeMb,
                    ["installed"]   = pack.Installed,
                    ["description"] = pack.Description
                });
            }

            var json = new JObject {
                ["status"]            = "ok",
                ["pathing_installed"] = result.PathingInstalled,
                ["total_packs"]       = packList.Count,
                ["installed_count"]   = installedCount,
                ["packs"]             = packsArray
            };

            if (!result.PathingInstalled) {
                json["message"] = string.IsNullOrEmpty(result.Message)
                    ? "Pathing is not installed. You can install it from the module catalog."
                    : result.Message;
            }

            string logDetail = "packs=" + packList.Count + ", installed=" + installedCount;

            return new LookupAnswer(json.ToString(Formatting.None), logDetail);
        }

        private static string ExtractStringValue(object val) {
            if (val == null) return null;
            if (val is string s) return s;
            if (val is JsonElement je && je.ValueKind == JsonValueKind.String) return je.GetString();
            return val.ToString();
        }

        private sealed class Function : LookupFunction
        {
            private readonly IBlishHudWorld _world;

            public Function(IBlishHudWorld world, Action<string> logDebug)
                : base(ListMarkerPacksTool.Name, ListMarkerPacksTool.Description, Schema, logDebug) {
                _world = world;
            }

            protected override async Task<LookupAnswer> InvokeLookupAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) {
                string search = null;

                if (arguments != null) {
                    if (arguments.TryGetValue(SearchArgument, out object sVal)) {
                        search = ExtractStringValue(sVal);
                    } else if (arguments.TryGetValue("query", out object qVal)) {
                        search = ExtractStringValue(qVal);
                    }
                }

                return await RunAsync(_world, search, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
