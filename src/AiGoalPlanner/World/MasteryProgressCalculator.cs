using System;
using System.Collections.Generic;
using jubeater.aigoalplanner.Core;

namespace jubeater.aigoalplanner.World
{
    public static class MasteryProgressCalculator
    {
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
