using System;
using System.Collections.Generic;
using System.IO;
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

        [Fact]
        public void In_game_mastery_data_shows_totals_under_path_of_fire_and_all_six_tracks_under_desert() {
            // /v2/account/mastery/points totals gives region "Path of Fire", earned 3, spent 1
            var pointTotals = new[] {
                new MasteryPointRegion("Path of Fire", spent: 1, earned: 3)
            };

            // /v2/masteries gives tracks with region "Desert"
            var tracks = new[] {
                new CachedMasteryTrack(14, "Raptor Mount", "Desert", 4),
                new CachedMasteryTrack(17, "Springer Mount", "Desert", 4),
                new CachedMasteryTrack(15, "Skimmer Mount", "Desert", 5),
                new CachedMasteryTrack(18, "Jackal Mount", "Desert", 4),
                new CachedMasteryTrack(20, "Roller Beetle Mount", "Desert", 3),
                new CachedMasteryTrack(19, "Crystal Champion", "Desert", 4)
            };

            // /v2/account/masteries: Raptor Mount level 0 (0 + 1 = 1/4 unlocked)
            var accountLevels = new Dictionary<int, int> {
                [14] = 0
            };

            // Tracks are grouped by the "region" field of /v2/masteries
            var tracksByRegion = tracks
                .GroupBy(m => m.Region, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            Assert.True(tracksByRegion.TryGetValue("Desert", out var desertTracks));

            var unfinished = MasteryProgressCalculator.CalculateUnfinishedTracks(desertTracks, accountLevels);

            var trackRegion = new MasteryTrackRegion("Desert", unfinished, tracksAvailable: true);

            Assert.Equal("Path of Fire", pointTotals[0].Region);
            Assert.Equal(3, pointTotals[0].Earned);
            Assert.Equal(1, pointTotals[0].Spent);
            Assert.Equal(2, pointTotals[0].Unspent);

            Assert.Equal("Desert", trackRegion.Region);
            Assert.True(trackRegion.TracksAvailable);
            Assert.Equal(6, trackRegion.UnfinishedTracks.Count);

            Assert.Equal("Raptor Mount", trackRegion.UnfinishedTracks[0].Name);
            Assert.Equal(1, trackRegion.UnfinishedTracks[0].Level);
            Assert.Equal(4, trackRegion.UnfinishedTracks[0].TotalLevels);

            Assert.Equal("Springer Mount", trackRegion.UnfinishedTracks[1].Name);
            Assert.Equal(0, trackRegion.UnfinishedTracks[1].Level);
            Assert.Equal(4, trackRegion.UnfinishedTracks[1].TotalLevels);

            Assert.Equal("Skimmer Mount", trackRegion.UnfinishedTracks[2].Name);
            Assert.Equal(0, trackRegion.UnfinishedTracks[2].Level);
            Assert.Equal(5, trackRegion.UnfinishedTracks[2].TotalLevels);

            Assert.Equal("Jackal Mount", trackRegion.UnfinishedTracks[3].Name);
            Assert.Equal(0, trackRegion.UnfinishedTracks[3].Level);
            Assert.Equal(4, trackRegion.UnfinishedTracks[3].TotalLevels);

            Assert.Equal("Roller Beetle Mount", trackRegion.UnfinishedTracks[4].Name);
            Assert.Equal(0, trackRegion.UnfinishedTracks[4].Level);
            Assert.Equal(3, trackRegion.UnfinishedTracks[4].TotalLevels);

            Assert.Equal("Crystal Champion", trackRegion.UnfinishedTracks[5].Name);
            Assert.Equal(0, trackRegion.UnfinishedTracks[5].Level);
            Assert.Equal(4, trackRegion.UnfinishedTracks[5].TotalLevels);
        }

        [Fact]
        public void Track_region_never_seen_by_code_such_as_magic_is_listed_like_any_other() {
            var tracks = new[] {
                new CachedMasteryTrack(45, "Castoran Survivalist", "Magic", 3),
                new CachedMasteryTrack(46, "Wild Castoran Magic", "Magic", 4)
            };

            var accountLevels = new Dictionary<int, int> {
                [45] = 0 // raw level 0 = 1/3 unlocked
            };

            var tracksByRegion = tracks
                .GroupBy(m => m.Region, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            Assert.True(tracksByRegion.TryGetValue("Magic", out var magicTracks));

            var unfinished = MasteryProgressCalculator.CalculateUnfinishedTracks(magicTracks, accountLevels);
            var trackRegion = new MasteryTrackRegion("Magic", unfinished, tracksAvailable: true);

            Assert.Equal("Magic", trackRegion.Region);
            Assert.Equal(2, trackRegion.UnfinishedTracks.Count);
            Assert.Equal("Castoran Survivalist", trackRegion.UnfinishedTracks[0].Name);
            Assert.Equal(1, trackRegion.UnfinishedTracks[0].Level);
            Assert.Equal(3, trackRegion.UnfinishedTracks[0].TotalLevels);
            Assert.Equal("Wild Castoran Magic", trackRegion.UnfinishedTracks[1].Name);
            Assert.Equal(0, trackRegion.UnfinishedTracks[1].Level);
            Assert.Equal(4, trackRegion.UnfinishedTracks[1].TotalLevels);
        }

        [Fact]
        public void No_region_name_literals_in_src() {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            while (!string.IsNullOrEmpty(dir) && !Directory.Exists(Path.Combine(dir, "src", "AiGoalPlanner"))) {
                dir = Path.GetDirectoryName(dir);
            }
            Assert.False(string.IsNullOrEmpty(dir), "Could not locate repo root containing src/AiGoalPlanner");

            string srcDir = Path.Combine(dir, "src");
            string[] forbiddenLiterals = new[] {
                "\"Central Tyria\"",
                "\"Heart of Thorns\"",
                "\"Path of Fire\"",
                "\"Icebrood Saga\"",
                "\"End of Dragons\"",
                "\"Secrets of the Obscure\"",
                "\"Janthir Wilds\"",
                "\"Maguuma\"",
                "\"Desert\"",
                "\"Tundra\"",
                "\"Jade\"",
                "\"Sky\"",
                "\"Wild\"",
                "\"Magic\""
            };

            var csFiles = Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories);
            var violations = new List<string>();

            foreach (string file in csFiles) {
                string content = File.ReadAllText(file);
                foreach (string literal in forbiddenLiterals) {
                    if (content.Contains(literal)) {
                        violations.Add(Path.GetFileName(file) + " contains " + literal);
                    }
                }
            }

            Assert.Empty(violations);
        }
    }
}
