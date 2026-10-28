using System;
using System.Collections.Generic;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// Information about one Pathing marker pack, either from Pathing's official repository
    /// (https://mp-repo.blishhud.com/repo.json) or installed locally in Blish HUD's markers directory.
    /// </summary>
    public sealed class MarkerPackInfo
    {
        public MarkerPackInfo(string name, string fileName, string description, string categories,
                              double sizeMb, string downloadUrl, string version, bool installed,
                              IReadOnlyList<int> mapIds = null) {
            Name        = name ?? string.Empty;
            FileName    = fileName ?? string.Empty;
            Description = description ?? string.Empty;
            Categories  = categories ?? string.Empty;
            SizeMb      = sizeMb;
            DownloadUrl = downloadUrl ?? string.Empty;
            Version     = version ?? string.Empty;
            Installed   = installed;
            MapIds      = mapIds ?? Array.Empty<int>();
        }

        public string             Name        { get; }
        public string             FileName    { get; }
        public string             Description { get; }
        public string             Categories  { get; }
        public double             SizeMb      { get; }
        public string             DownloadUrl { get; }
        public string             Version     { get; }
        public bool               Installed   { get; }
        public IReadOnlyList<int> MapIds      { get; }
    }

    public enum MarkerPacksStatus
    {
        Ok,
        NoPathing,
        Unavailable
    }

    /// <summary>
    /// The result of querying marker packs: available official packs and locally installed packs.
    /// </summary>
    public sealed class MarkerPacksResult
    {
        public MarkerPacksResult(MarkerPacksStatus status, IReadOnlyList<MarkerPackInfo> packs,
                                 bool pathingInstalled = true, string message = null) {
            Status           = status;
            Packs            = packs ?? Array.Empty<MarkerPackInfo>();
            PathingInstalled = pathingInstalled;
            Message          = message ?? string.Empty;
        }

        public MarkerPacksStatus             Status           { get; }
        public IReadOnlyList<MarkerPackInfo> Packs            { get; }
        public bool                          PathingInstalled { get; }
        public string                        Message          { get; }
    }

    /// <summary>
    /// What came of a marker pack download or delete operation.
    /// </summary>
    public sealed class MarkerPackChange
    {
        public MarkerPackChange(bool applied, bool changed, string packName, string fileName,
                                double sizeMb, string problem = null) {
            Applied  = applied;
            Changed  = changed;
            PackName = packName ?? string.Empty;
            FileName = fileName ?? string.Empty;
            SizeMb   = sizeMb;
            Problem  = problem ?? string.Empty;
        }

        public bool   Applied  { get; }
        public bool   Changed  { get; }
        public string PackName { get; }
        public string FileName { get; }
        public double SizeMb   { get; }
        public string Problem  { get; }
    }
}
