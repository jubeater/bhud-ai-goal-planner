using System;
using System.Linq;
using System.Net;
using System.Reflection;

namespace jubeater.aigoalplanner.World
{
    /// <summary>
    /// Distinguishes expected API responses (such as 404 for maps/floors without hero challenges)
    /// from transient failures (timeouts, 5xx, network drops).
    /// </summary>
    public static class Gw2ApiExceptionHelper
    {
        public static bool IsNotFound(Exception ex) {
            if (ex == null) return false;

            if (ex is AggregateException agg) {
                return agg.InnerExceptions.Any(IsNotFound);
            }

            Type type = ex.GetType();
            if (type.FullName == "Gw2Sharp.WebApi.Exceptions.NotFoundException") {
                return true;
            }

            if (ex is WebException webEx &&
                webEx.Response is HttpWebResponse httpWebResp &&
                httpWebResp.StatusCode == HttpStatusCode.NotFound) {
                return true;
            }

            // Gw2Sharp UnexpectedStatusException has a Response property with HttpStatusCode
            PropertyInfo respProp = type.GetProperty("Response");
            if (respProp != null) {
                try {
                    object resp = respProp.GetValue(ex, null);
                    if (resp != null) {
                        PropertyInfo statusProp = resp.GetType().GetProperty("StatusCode");
                        if (statusProp != null) {
                            object codeObj = statusProp.GetValue(resp, null);
                            if (codeObj is HttpStatusCode code && code == HttpStatusCode.NotFound) {
                                return true;
                            }
                            if (codeObj is int intCode && intCode == 404) {
                                return true;
                            }
                        }
                    }
                } catch {
                    // Ignore reflection read errors
                }
            }

            if (!string.IsNullOrEmpty(ex.Message) &&
                (ex.Message.Contains("404") || ex.Message.IndexOf("Not Found", StringComparison.OrdinalIgnoreCase) >= 0)) {
                return true;
            }

            return false;
        }
    }
}
