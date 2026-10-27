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
    /// The <c>get_location</c> Lookup: the Player's current map, region and active character name
    /// from Mumble Link and cached map data.
    /// </summary>
    public static class GetLocationTool
    {
        public const string Name = "get_location";

        private const string Description =
            "Reads the Player's current location from Guild Wars 2: the active character name, current map ID "
          + "and name, and region ID and name. Call this to know which map or region the Player is on.";

        private const string SchemaJson = "{\"type\":\"object\",\"properties\":{}}";

        private static readonly JsonDocument Schema = JsonDocument.Parse(SchemaJson);

        public static AIFunction Create(IBlishHudWorld world, Action<string> logDebug = null) {
            return new Function(world, logDebug);
        }

        public static string Progress(IDictionary<string, object> arguments) {
            return "Checking current location…";
        }

        private static async Task<LookupAnswer> RunAsync(IBlishHudWorld world, CancellationToken cancellationToken) {
            PlayerLocationResult result = await world.GetLocationAsync(cancellationToken).ConfigureAwait(false);

            if (result.Status != LocationStatus.Ok || result.Location == null) {
                var error = new JObject {
                    ["status"] = "unavailable",
                    ["reason"] = string.IsNullOrEmpty(result.Reason) ? "Location is unavailable." : result.Reason
                };

                return new LookupAnswer(error.ToString(Formatting.None), "status=unavailable");
            }

            PlayerLocation loc = result.Location;

            var json = new JObject {
                ["status"]    = "ok",
                ["character"] = loc.CharacterName,
                ["map_id"]    = loc.MapId,
                ["map"]       = loc.MapName,
                ["region_id"] = loc.RegionId,
                ["region"]    = loc.RegionName
            };

            return new LookupAnswer(json.ToString(Formatting.None), "map=" + loc.MapName + ", character=" + loc.CharacterName);
        }

        private sealed class Function : LookupFunction
        {
            private readonly IBlishHudWorld _world;

            public Function(IBlishHudWorld world, Action<string> logDebug)
                : base(GetLocationTool.Name, GetLocationTool.Description, Schema, logDebug) {
                _world = world;
            }

            protected override async Task<LookupAnswer> InvokeLookupAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) {
                return await RunAsync(_world, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
