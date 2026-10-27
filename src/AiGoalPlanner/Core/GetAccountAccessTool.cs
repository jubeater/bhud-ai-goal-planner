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
    /// The <c>get_account_access</c> Lookup: the expansions and content the Player's Guild Wars 2 account
    /// owns (e.g. GuildWars2, HeartOfThorns, PathOfFire).
    /// </summary>
    public static class GetAccountAccessTool
    {
        public const string Name = "get_account_access";

        private const string Description =
            "Reads the expansions and content the Player's Guild Wars 2 account owns (e.g. GuildWars2, "
          + "HeartOfThorns, PathOfFire). Do not suggest content the account lacks, and mention when the best "
          + "option requires an expansion the Player does not own.";

        private const string SchemaJson = "{\"type\":\"object\",\"properties\":{}}";

        private static readonly JsonDocument Schema = JsonDocument.Parse(SchemaJson);

        public static AIFunction Create(IBlishHudWorld world, Action<string> logDebug = null) {
            return new Function(world, logDebug);
        }

        public static string Progress(IDictionary<string, object> arguments) {
            return "Checking owned content…";
        }

        private static async Task<LookupAnswer> RunAsync(IBlishHudWorld world, CancellationToken cancellationToken) {
            AccountAccessResult result = await world.GetAccountAccessAsync(cancellationToken).ConfigureAwait(false);

            if (result.Status == AccountAccessStatus.MissingPermission) {
                string perm = string.IsNullOrEmpty(result.MissingPermission) ? "account" : result.MissingPermission;
                return MissingPermissionRefusal.For(perm);
            }

            if (result.Status != AccountAccessStatus.Ok) {
                var unavailable = new JObject {
                    ["status"]  = "unavailable",
                    ["message"] = string.IsNullOrEmpty(result.Message) ? "Account access data is unavailable." : result.Message
                };

                return new LookupAnswer(unavailable.ToString(Formatting.None), "status=unavailable");
            }

            var json = new JObject {
                ["status"]        = "ok",
                ["owned_content"] = new JArray(result.OwnedContent)
            };

            return new LookupAnswer(json.ToString(Formatting.None), "content_count=" + result.OwnedContent.Count);
        }

        private sealed class Function : LookupFunction
        {
            private readonly IBlishHudWorld _world;

            public Function(IBlishHudWorld world, Action<string> logDebug)
                : base(GetAccountAccessTool.Name, GetAccountAccessTool.Description, Schema, logDebug) {
                _world = world;
            }

            protected override async Task<LookupAnswer> InvokeLookupAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) {
                return await RunAsync(_world, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
