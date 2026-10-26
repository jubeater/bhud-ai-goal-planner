using System;
using System.Collections.Generic;
using System.Linq;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// Flattens a Module's Settings, which can nest, into the plain list the AI gets to read: every
    /// child key is qualified with its parent ("Markers.Trail size"), so a child is findable by its
    /// key and two subcollections with the same child key stay distinct.
    /// </summary>
    /// <remarks>
    /// This is the last gate between the Player's Settings and the model, so the secret rules live
    /// here. Each Setting is judged by its own key and display name, never by the qualified key its
    /// position gives it: a parent's name is not the child's accusation. A Setting that reads as a
    /// key, token or password is left out, and a subcollection whose own name reads as one is left
    /// out with everything in it — a drawer of credentials must not survive because each drawer
    /// item is innocently named.
    /// </remarks>
    public static class SettingWalk
    {
        /// <summary>
        /// The Settings of one Module, nested or not, as one flat list with qualified keys.
        /// </summary>
        public static IReadOnlyList<ModuleSettingInfo> Flatten(IReadOnlyList<ModuleSettingInfo> settings) {
            var flat = new List<ModuleSettingInfo>();

            Walk(string.Empty, settings ?? new ModuleSettingInfo[0], flat);

            return flat;
        }

        private static void Walk(string prefix, IReadOnlyList<ModuleSettingInfo> settings, List<ModuleSettingInfo> into) {
            foreach (ModuleSettingInfo setting in settings) {
                if (setting.IsSubcollection) {
                    if (!SecretFilter.LooksLikeSecret(setting.Key, setting.DisplayName)) {
                        Walk(Qualify(prefix, setting.Key), setting.Children, into);
                    }

                    continue;
                }

                if (SecretFilter.LooksLikeSecret(setting.Key, setting.DisplayName)) continue;

                into.Add(new ModuleSettingInfo(Qualify(prefix, setting.Key), setting.DisplayName, setting.Description,
                                               setting.TypeName, setting.Value, setting.Min, setting.Max));
            }
        }

        private static string Qualify(string prefix, string key) {
            if (prefix.Length == 0) return key;

            return key.Length == 0 ? prefix : prefix + "." + key;
        }
    }
}
