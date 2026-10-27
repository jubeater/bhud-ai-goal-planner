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

        [Theory]
        [InlineData("Central Tyria", "Tyria")]
        [InlineData("Heart of Thorns", "Maguuma")]
        [InlineData("Path of Fire", "Desert")]
        [InlineData("Icebrood Saga", "Tundra")]
        [InlineData("End of Dragons", "Jade")]
        [InlineData("Secrets of the Obscure", "Sky")]
        [InlineData("Janthir Wilds", "Wild")]
        [InlineData("Desert", "Desert")]
        [InlineData("Tyria", "Tyria")]
        [InlineData("Magic", "Magic")]
        public void NormalizeRegionName_maps_account_totals_regions_to_masteries_regions(string input, string expected) {
            Assert.Equal(expected, MasteryProgressCalculator.NormalizeRegionName(input));
        }

        [Fact]
        public void Path_of_fire_region_with_desert_masteries_maps_correctly_and_lists_expected_tracks() {
            var tracks = new[] {
                new CachedMasteryTrack(14, "Raptor Mount", "Desert", 4),
                new CachedMasteryTrack(17, "Springer Mount", "Desert", 4),
                new CachedMasteryTrack(15, "Skimmer Mount", "Desert", 5),
                new CachedMasteryTrack(18, "Jackal Mount", "Desert", 4),
                new CachedMasteryTrack(20, "Roller Beetle Mount", "Desert", 3),
                new CachedMasteryTrack(19, "Crystal Champion", "Desert", 4)
            };

            var accountLevels = new Dictionary<int, int> {
                [14] = 0
            };

            string pointsRegion = "Path of Fire";
            string canonicalRegion = MasteryProgressCalculator.NormalizeRegionName(pointsRegion);
            Assert.Equal("Desert", canonicalRegion);

            var masteriesByRegion = tracks
                .GroupBy(m => MasteryProgressCalculator.NormalizeRegionName(m.Region), System.StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), System.StringComparer.OrdinalIgnoreCase);

            Assert.True(masteriesByRegion.TryGetValue(canonicalRegion, out var regionTracks));

            var unfinished = MasteryProgressCalculator.CalculateUnfinishedTracks(regionTracks, accountLevels);

            var regionProgress = new MasteryRegionProgress(pointsRegion, spent: 1, earned: 3, unfinished, tracksAvailable: true);

            Assert.Equal(2, regionProgress.Unspent);
            Assert.True(regionProgress.TracksAvailable);
            Assert.Equal(6, regionProgress.UnfinishedTracks.Count);

            Assert.Equal("Raptor Mount", regionProgress.UnfinishedTracks[0].Name);
            Assert.Equal(1, regionProgress.UnfinishedTracks[0].Level);
            Assert.Equal(4, regionProgress.UnfinishedTracks[0].TotalLevels);

            Assert.Equal("Springer Mount", regionProgress.UnfinishedTracks[1].Name);
            Assert.Equal(0, regionProgress.UnfinishedTracks[1].Level);
            Assert.Equal(4, regionProgress.UnfinishedTracks[1].TotalLevels);

            Assert.Equal("Skimmer Mount", regionProgress.UnfinishedTracks[2].Name);
            Assert.Equal(0, regionProgress.UnfinishedTracks[2].Level);
            Assert.Equal(5, regionProgress.UnfinishedTracks[2].TotalLevels);

            Assert.Equal("Jackal Mount", regionProgress.UnfinishedTracks[3].Name);
            Assert.Equal(0, regionProgress.UnfinishedTracks[3].Level);
            Assert.Equal(4, regionProgress.UnfinishedTracks[3].TotalLevels);

            Assert.Equal("Roller Beetle Mount", regionProgress.UnfinishedTracks[4].Name);
            Assert.Equal(0, regionProgress.UnfinishedTracks[4].Level);
            Assert.Equal(3, regionProgress.UnfinishedTracks[4].TotalLevels);

            Assert.Equal("Crystal Champion", regionProgress.UnfinishedTracks[5].Name);
            Assert.Equal(0, regionProgress.UnfinishedTracks[5].Level);
            Assert.Equal(4, regionProgress.UnfinishedTracks[5].TotalLevels);
        }

        [Fact]
        public void Region_with_no_matching_mastery_tracks_marks_tracks_unavailable_not_empty_list() {
            var regionProgress = new MasteryRegionProgress("Unknown Region", spent: 0, earned: 2, unfinishedTracks: null, tracksAvailable: false);

            Assert.False(regionProgress.TracksAvailable);
            Assert.Empty(regionProgress.UnfinishedTracks);
        }
    }
}
