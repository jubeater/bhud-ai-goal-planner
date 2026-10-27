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
    /// The <c>get_progress</c> Lookup: the Player's progress in Guild Wars 2: mastery points and
    /// unfinished tracks per region, or missing hero challenges on the current map and region.
    /// </summary>
    public static class GetProgressTool
    {
        public const string Name = "get_progress";

        public const string KindArgument = "kind";

        private const string Description =
            "Reads the Player's progress in Guild Wars 2. Pass kind='mastery' to see point totals per region "
          + "and unfinished tracks grouped by region (the two lists use different region names; track names "
          + "show which expansion they belong to), or kind='hero_points' to see missing hero challenges "
          + "on the current map and region.";

        private const string SchemaJson =
            "{\"type\":\"object\",\"properties\":{\"kind\":{\"type\":\"string\","
          + "\"description\":\"The kind of progress to check: 'mastery' or 'hero_points'.\","
          + "\"enum\":[\"mastery\",\"hero_points\"]}},"
          + "\"required\":[\"kind\"]}";

        private static readonly JsonDocument Schema = JsonDocument.Parse(SchemaJson);

        public static AIFunction Create(IBlishHudWorld world, Action<string> logDebug = null) {
            return new Function(world, logDebug);
        }

        public static string Progress(IDictionary<string, object> arguments) {
            if (arguments != null && arguments.TryGetValue(KindArgument, out object val) && val is string kindStr) {
                if (string.Equals(kindStr, "hero_points", StringComparison.OrdinalIgnoreCase)) {
                    return "Checking hero challenge progress…";
                }
            }

            return "Checking mastery progress…";
        }

        private static async Task<LookupAnswer> RunAsync(IBlishHudWorld world, string kindArgument, CancellationToken cancellationToken) {
            string kindStr = (kindArgument ?? string.Empty).Trim().ToLowerInvariant();

            ProgressKind kind;

            if (kindStr == "mastery") {
                kind = ProgressKind.Mastery;
            } else if (kindStr == "hero_points" || kindStr == "hero_point" || kindStr == "heropoints") {
                kind = ProgressKind.HeroPoints;
            } else {
                var badKind = new JObject {
                    ["status"]      = "unavailable",
                    ["instruction"] = "Specify kind='mastery' or kind='hero_points'."
                };

                return new LookupAnswer(badKind.ToString(Formatting.None), "status=bad_kind");
            }

            ProgressResult result = await world.GetProgressAsync(kind, cancellationToken).ConfigureAwait(false);

            if (result.Status == ProgressStatus.NoApiAccess) {
                return MissingPermissionRefusal.NoApiAccess();
            }

            if (result.Status == ProgressStatus.MissingPermission) {
                string perm = string.IsNullOrEmpty(result.MissingPermission)
                                  ? (kind == ProgressKind.Mastery ? "progression" : "characters")
                                  : result.MissingPermission;

                return MissingPermissionRefusal.For(perm);
            }

            if (result.Status == ProgressStatus.Unknown) {
                var unknownJson = new JObject {
                    ["status"]      = "unknown",
                    ["kind"]        = "hero_points",
                    ["character"]   = result.CharacterName,
                    ["map"]         = result.MapName,
                    ["map_id"]      = result.MapId,
                    ["region"]      = result.RegionName,
                    ["message"]     = string.IsNullOrEmpty(result.Message)
                                        ? "This is a known Guild Wars 2 API issue where hero-point data is missing for some characters. The Player can check the in-game Hero panel."
                                        : result.Message,
                    ["instruction"] = "This is a known Guild Wars 2 API issue where hero-point data is missing for some characters. Tell the Player to check the in-game Hero panel. Do not say the API key lacks access or that the character hasn't earned enough data."
                };

                return new LookupAnswer(unknownJson.ToString(Formatting.None), "status=unknown");
            }

            if (result.Status == ProgressStatus.NoCharacterOrMap) {
                var noMap = new JObject {
                    ["status"]  = "unavailable",
                    ["reason"]  = "no_character_or_map",
                    ["message"] = string.IsNullOrEmpty(result.Message) ? "The Player is not currently on a map in Guild Wars 2." : result.Message
                };

                return new LookupAnswer(noMap.ToString(Formatting.None), "status=no_character_or_map");
            }

            if (result.Status != ProgressStatus.Ok) {
                var unavailable = new JObject {
                    ["status"]  = "unavailable",
                    ["message"] = string.IsNullOrEmpty(result.Message) ? "Progress data is unavailable." : result.Message
                };

                return new LookupAnswer(unavailable.ToString(Formatting.None), "status=unavailable");
            }

            if (kind == ProgressKind.Mastery) {
                var pointsArray = new JArray();
                foreach (MasteryPointRegion p in result.MasteryPoints) {
                    pointsArray.Add(new JObject {
                        ["region"]         = p.Region,
                        ["earned"]         = p.Earned,
                        ["spent"]          = p.Spent,
                        ["unspent"]        = p.Unspent,
                        ["points_earned"]  = p.Earned,
                        ["points_spent"]   = p.Spent,
                        ["points_unspent"] = p.Unspent
                    });
                }

                var tracksArray = new JArray();
                foreach (MasteryTrackRegion tr in result.MasteryTracks) {
                    var trObj = new JObject {
                        ["region"] = tr.Region
                    };

                    if (!tr.TracksAvailable) {
                        trObj["tracks_status"] = "unavailable";
                    } else {
                        var trackItems = new JArray(tr.UnfinishedTracks.Select(t => new JObject {
                            ["name"]         = t.Name,
                            ["unlocked"]     = t.Level,
                            ["total_levels"] = t.TotalLevels
                        }));
                        trObj["unfinished_tracks"] = trackItems;
                    }

                    tracksArray.Add(trObj);
                }

                var json = new JObject {
                    ["status"]        = "ok",
                    ["kind"]          = "mastery",
                    ["points"]        = pointsArray,
                    ["track_regions"] = tracksArray
                };

                if (!result.TracksAvailable) {
                    json["tracks_status"] = "unavailable";
                }

                return new LookupAnswer(json.ToString(Formatting.None),
                                        "points=" + result.MasteryPoints.Count + ", track_regions=" + result.MasteryTracks.Count);
            }

            // Hero points
            HeroChallengeProgress hc = result.HeroChallenges ?? new HeroChallengeProgress(0, 0, 0);

            var hcObj = new JObject {
                ["map_total"]     = hc.MapTotal,
                ["map_completed"] = hc.MapCompleted,
                ["map_missing"]   = hc.MapMissing
            };

            if (hc.RegionIncomplete) {
                hcObj["region_missing"]    = null;
                hcObj["region_incomplete"] = true;
                hcObj["region_note"]       = "Region hero challenges count is incomplete because some maps could not be loaded.";
            } else {
                hcObj["region_missing"] = hc.RegionMissing;
            }

            var heroJson = new JObject {
                ["status"]          = "ok",
                ["kind"]            = "hero_points",
                ["character"]       = result.CharacterName,
                ["map"]             = result.MapName,
                ["map_id"]          = result.MapId,
                ["region"]          = result.RegionName,
                ["hero_challenges"] = hcObj
            };

            return new LookupAnswer(heroJson.ToString(Formatting.None), "map=" + result.MapName + ", missing=" + hc.MapMissing);
        }

        private sealed class Function : LookupFunction
        {
            private readonly IBlishHudWorld _world;

            public Function(IBlishHudWorld world, Action<string> logDebug)
                : base(GetProgressTool.Name, GetProgressTool.Description, Schema, logDebug) {
                _world = world;
            }

            protected override async Task<LookupAnswer> InvokeLookupAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) {
                string kind = arguments != null && arguments.TryGetValue(KindArgument, out object value)
                                  ? value as string
                                  : null;

                return await RunAsync(_world, kind, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
