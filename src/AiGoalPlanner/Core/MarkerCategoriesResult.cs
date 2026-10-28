using System;
using System.Collections.Generic;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// The outcome of looking up marker categories in Pathing and its installed packs.
    /// </summary>
    public enum MarkerCategoriesStatus
    {
        /// <summary>Packs were searched and matches returned.</summary>
        Ok,

        /// <summary>The Pathing module is not installed in Blish HUD.</summary>
        NoPathing,

        /// <summary>Pathing is installed, but no marker packs exist in the markers directory.</summary>
        NoPacks
    }

    /// <summary>
    /// One marker category match from an installed pack.
    /// </summary>
    public sealed class MarkerCategoryMatch
    {
        public MarkerCategoryMatch(string packName, string displayPath, string @namespace, bool shown,
                                   int markerCount, int trailCount) {
            PackName    = packName ?? string.Empty;
            DisplayPath = displayPath ?? string.Empty;
            Namespace   = @namespace ?? string.Empty;
            Shown       = shown;
            MarkerCount = markerCount;
            TrailCount  = trailCount;
        }

        /// <summary>The marker pack that defines this category.</summary>
        public string PackName { get; }

        /// <summary>The hierarchical display path, e.g. "Tekkit's Guides › Path of Fire › Routes - Hero Points Only".</summary>
        public string DisplayPath { get; }

        /// <summary>The full category namespace, e.g. "tw_guides.tw_mc_pof.tw_mc_pof_trails_hp".</summary>
        public string Namespace { get; }

        /// <summary>Whether this category is currently shown in Pathing.</summary>
        public bool Shown { get; }

        /// <summary>How many POIs (markers) exist under this category on the queried map.</summary>
        public int MarkerCount { get; }

        /// <summary>How many trails exist under this category on the queried map.</summary>
        public int TrailCount { get; }

        /// <summary>The total number of markers and trails on the queried map.</summary>
        public int TotalCount => MarkerCount + TrailCount;
    }

    /// <summary>
    /// The full result of a marker categories search.
    /// </summary>
    public sealed class MarkerCategoriesResult
    {
        public MarkerCategoriesResult(MarkerCategoriesStatus status, int mapId, string mapName,
                                      IReadOnlyList<MarkerCategoryMatch> categories,
                                      bool cut = false, int totalMatches = 0,
                                      IReadOnlyList<string> skippedPacks = null,
                                      string message = null) {
            Status       = status;
            MapId        = mapId;
            MapName      = mapName ?? string.Empty;
            Categories   = categories ?? Array.Empty<MarkerCategoryMatch>();
            Cut          = cut;
            TotalMatches = totalMatches > 0 ? totalMatches : (categories?.Count ?? 0);
            SkippedPacks = skippedPacks ?? Array.Empty<string>();
            Message      = message ?? string.Empty;
        }

        public MarkerCategoriesStatus             Status       { get; }
        public int                                MapId        { get; }
        public string                             MapName      { get; }
        public IReadOnlyList<MarkerCategoryMatch> Categories   { get; }
        public bool                               Cut          { get; }
        public int                                TotalMatches { get; }
        public IReadOnlyList<string>              SkippedPacks { get; }
        public string                             Message      { get; }

        public static MarkerCategoriesResult NoPathing() {
            return new MarkerCategoriesResult(
                MarkerCategoriesStatus.NoPathing, 0, string.Empty, Array.Empty<MarkerCategoryMatch>(),
                message: "Pathing is not installed. Suggest installing the Pathing module from the Blish HUD module catalog.");
        }

        public static MarkerCategoriesResult NoPacks() {
            return new MarkerCategoriesResult(
                MarkerCategoriesStatus.NoPacks, 0, string.Empty, Array.Empty<MarkerCategoryMatch>(),
                message: "No marker packs are installed. Suggest installing a marker pack such as Tekkit's Guides.");
        }
    }
}
