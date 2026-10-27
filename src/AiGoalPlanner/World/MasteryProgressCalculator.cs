using System;
using System.Collections.Generic;
using jubeater.aigoalplanner.Core;

namespace jubeater.aigoalplanner.World
{
    public static class MasteryProgressCalculator
    {
        private static readonly Dictionary<string, string> ApiRegionNameToMasteryRegion =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Central Tyria"] = "Tyria",
                ["Heart of Thorns"] = "Maguuma",
                ["Path of Fire"] = "Desert",
                ["Icebrood Saga"] = "Tundra",
                ["End of Dragons"] = "Jade",
                ["Secrets of the Obscure"] = "Sky",
                ["Janthir Wilds"] = "Wild"
            };

        public static string NormalizeRegionName(string region) {
            if (string.IsNullOrWhiteSpace(region)) return string.Empty;
            string trimmed = region.Trim();
            if (ApiRegionNameToMasteryRegion.TryGetValue(trimmed, out string mapped)) {
                return mapped;
            }
            return trimmed;
        }

        public static IReadOnlyList<MasteryTrackProgress> CalculateUnfinishedTracks(
            IEnumerable<CachedMasteryTrack> tracks,
            IReadOnlyDictionary<int, int> accountMasteryRawLevels)
        {
            var unfinished = new List<MasteryTrackProgress>();
            if (tracks == null) return unfinished;

            foreach (CachedMasteryTrack track in tracks) {
                int unlocked = (accountMasteryRawLevels != null && accountMasteryRawLevels.TryGetValue(track.Id, out int rawLevel))
                    ? rawLevel + 1
                    : 0;

                if (unlocked < track.TotalLevels) {
                    unfinished.Add(new MasteryTrackProgress(track.Id, track.Name, track.Region, unlocked, track.TotalLevels));
                }
            }

            return unfinished;
        }
    }
}
