using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace jubeater.aigoalplanner.Core
{
    public static class MissingPermissionRefusal
    {
        public static LookupAnswer For(string permission) {
            string perm = string.IsNullOrEmpty(permission) ? "unknown" : permission;

            var refusal = new JObject {
                ["status"]      = "unavailable",
                ["reason"]      = "missing_permission",
                ["permission"]  = perm,
                ["notice"]      = "Your Guild Wars 2 API key is missing or lacks the '" + perm + "' permission. Planning continues with available data.",
                ["instruction"] = "Guild Wars 2 API key or '" + perm + "' permission is missing. Mention this to the Player and continue planning with what is available."
            };

            return new LookupAnswer(refusal.ToString(Formatting.None), "status=missing_permission, permission=" + perm);
        }
    }
}
