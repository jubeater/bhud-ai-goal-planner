using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace jubeater.aigoalplanner.Core
{
    public static class MissingPermissionRefusal
    {
        public static LookupAnswer NoApiAccess() {
            var refusal = new JObject {
                ["status"]      = "unavailable",
                ["reason"]      = "no_api_access",
                ["notice"]      = "Blish HUD hasn't given AI Goal Planner API access yet. Make sure a Guild Wars 2 API key is added in Blish HUD, then restart Blish HUD.",
                ["instruction"] = "Blish HUD has not granted Guild Wars 2 API access yet. Mention this to the Player and continue planning with what is available."
            };

            return new LookupAnswer(refusal.ToString(Formatting.None), "status=no_api_access");
        }

        public static LookupAnswer For(string permission) {
            string perm = string.IsNullOrEmpty(permission) ? "unknown" : permission;

            var refusal = new JObject {
                ["status"]      = "unavailable",
                ["reason"]      = "missing_permission",
                ["permission"]  = perm,
                ["notice"]      = "Your Guild Wars 2 API key lacks the '" + perm + "' permission. Planning continues with available data.",
                ["instruction"] = "Guild Wars 2 API key lacks the '" + perm + "' permission. Mention this to the Player and continue planning with what is available."
            };

            return new LookupAnswer(refusal.ToString(Formatting.None), "status=missing_permission, permission=" + perm);
        }
    }
}
