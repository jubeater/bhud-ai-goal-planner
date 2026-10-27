using System.Collections.Generic;
using System.Linq;
using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.World;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    public class MasteryProgressTests
    {
        [Fact]
        public void Finished_five_level_track_with_raw_level_four_is_not_listed_as_unfinished() {
            var tracks = new[] {
                new CachedMasteryTrack(1, "Pact Commander", "Tyria", 5)
            };
            // In GW2 API /v2/account/masteries, level 4 means index 4 is unlocked (5 levels unlocked = finished)
            var accountLevels = new Dictionary<int, int> {
                [1] = 4
            };

            var unfinished = MasteryProgressCalculator.CalculateUnfinishedTracks(tracks, accountLevels);

            Assert.Empty(unfinished);
        }

        [Fact]
        public void Track_at_raw_level_two_shows_three_of_five_unlocked() {
            var tracks = new[] {
                new CachedMasteryTrack(1, "Pact Commander", "Tyria", 5)
            };
            // In GW2 API /v2/account/masteries, level 2 means index 2 is unlocked (2 + 1 = 3 levels unlocked)
            var accountLevels = new Dictionary<int, int> {
                [1] = 2
            };

            var unfinished = MasteryProgressCalculator.CalculateUnfinishedTracks(tracks, accountLevels);

            MasteryTrackProgress track = Assert.Single(unfinished);
            Assert.Equal(3, track.Level);
            Assert.Equal(5, track.TotalLevels);
        }

        [Fact]
        public void Missing_track_shows_zero_of_N_unlocked() {
            var tracks = new[] {
                new CachedMasteryTrack(2, "Gliding", "Heart of Maguuma", 6)
            };
            // Track is missing from /v2/account/masteries response because player hasn't started it
            var accountLevels = new Dictionary<int, int>();

            var unfinished = MasteryProgressCalculator.CalculateUnfinishedTracks(tracks, accountLevels);

            MasteryTrackProgress track = Assert.Single(unfinished);
            Assert.Equal(0, track.Level);
            Assert.Equal(6, track.TotalLevels);
        }
    }
}
