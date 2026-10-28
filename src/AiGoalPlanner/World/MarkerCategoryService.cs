using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using jubeater.aigoalplanner.Core;
using Newtonsoft.Json;

namespace jubeater.aigoalplanner.World
{
    public interface IMarkerCategoryService
    {
        MarkerCategoriesResult FindCategories(string search, int mapId, string mapName = null);
        Task<MarkerCategoriesResult> FindCategoriesAsync(string search, int mapId, string mapName = null, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Reads and caches Pathing marker packs (TacO XML format) from Blish HUD's shared markers directory,
    /// evaluates category visibility from category_preferences.txt, and rolls up POI/trail map counts.
    /// </summary>
    public sealed class MarkerCategoryService : IMarkerCategoryService
    {
        private const int MaxResults = 30;

        private readonly string         _markersDirectory;
        private readonly string         _cacheDirectory;
        private readonly Action<string> _logWarn;
        private readonly object         _lock = new object();

        // In-memory cache of parsed packs: packPath -> ParsedPack
        private readonly Dictionary<string, ParsedPack> _inMemoryPacks =
            new Dictionary<string, ParsedPack>(StringComparer.OrdinalIgnoreCase);

        public MarkerCategoryService(string markersDirectory, string cacheDirectory, Action<string> logWarn = null) {
            _markersDirectory = markersDirectory;
            _cacheDirectory   = string.IsNullOrWhiteSpace(cacheDirectory) ? null : Path.Combine(cacheDirectory, "marker_packs");
            _logWarn          = logWarn;
        }

        public Task<MarkerCategoriesResult> FindCategoriesAsync(string search, int mapId, string mapName = null, CancellationToken cancellationToken = default) {
            // Reading packs runs off the game thread, since it's file I/O and touches no Blish HUD state (ADR-0009).
            return Task.Run(() => FindCategories(search, mapId, mapName), cancellationToken);
        }

        public MarkerCategoriesResult FindCategories(string search, int mapId, string mapName = null) {
            if (string.IsNullOrWhiteSpace(_markersDirectory) || !Directory.Exists(_markersDirectory)) {
                return MarkerCategoriesResult.NoPacks();
            }

            // 1. Locate all marker pack files and unpacked pack directories
            List<PackSource> packSources = DiscoverPacks(_markersDirectory);
            if (packSources.Count == 0) {
                return MarkerCategoriesResult.NoPacks();
            }

            // 2. Read Player's preferences from markers/data/states/category_preferences.txt
            Dictionary<string, bool> preferences = LoadPreferences(_markersDirectory);

            // 3. Parse or load cached packs
            var parsedPacks = new List<ParsedPack>();
            var skippedPacks = new List<string>();

            foreach (PackSource source in packSources) {
                try {
                    ParsedPack pack = LoadOrParsePack(source);
                    if (pack != null) {
                        parsedPacks.Add(pack);
                    }
                } catch (Exception ex) {
                    _logWarn?.Invoke("Marker pack failed to parse: " + ex.GetType().Name);
                    skippedPacks.Add(source.Identifier);
                }
            }

            if (parsedPacks.Count == 0 && skippedPacks.Count > 0) {
                return new MarkerCategoriesResult(
                    MarkerCategoriesStatus.Ok, mapId, mapName, Array.Empty<MarkerCategoryMatch>(),
                    cut: false, totalMatches: 0, skippedPacks: skippedPacks,
                    message: "All discovered marker packs failed to parse.");
            }

            if (parsedPacks.Count == 0) {
                return MarkerCategoriesResult.NoPacks();
            }

            // 4. Search and match categories
            string trimmedSearch = (search ?? string.Empty).Trim();
            string[] searchTokens = trimmedSearch.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            var matches = new List<MarkerCategoryMatch>();

            foreach (ParsedPack pack in parsedPacks) {
                foreach (ParsedCategory cat in pack.Categories) {
                    if (IsMatch(cat, trimmedSearch, searchTokens)) {
                        // Roll up map counts for mapId
                        pack.GetCountsForMap(mapId, cat.Namespace, out int markerCount, out int trailCount);

                        // Determine shown state
                        bool isShown = DetermineShownState(cat, pack, preferences);

                        matches.Add(new MarkerCategoryMatch(
                            pack.PackName,
                            cat.DisplayPath,
                            cat.Namespace,
                            isShown,
                            markerCount,
                            trailCount));
                    }
                }
            }

            int totalMatches = matches.Count;

            // 5. Sort matches: categories with markers on target map come first, then by count descending
            IOrderedEnumerable<MarkerCategoryMatch> sorted = matches
                .OrderBy(m => m.TotalCount > 0 ? 0 : 1)
                .ThenByDescending(m => m.TotalCount)
                .ThenBy(m => m.DisplayPath, StringComparer.OrdinalIgnoreCase);

            bool cut = totalMatches > MaxResults;
            List<MarkerCategoryMatch> resultList = sorted.Take(MaxResults).ToList();
            string message = cut ? "Showing top " + MaxResults + " of " + totalMatches + " matches." : null;

            return new MarkerCategoriesResult(
                MarkerCategoriesStatus.Ok,
                mapId,
                mapName,
                resultList,
                cut,
                totalMatches,
                skippedPacks,
                message);
        }

        private static bool IsMatch(ParsedCategory cat, string search, string[] tokens) {
            if (string.IsNullOrEmpty(search)) return true;

            // Direct substring match
            if (cat.DisplayPath.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                cat.Namespace.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }

            if (tokens.Length <= 1) return false;

            // Token match: all tokens must be present in display path or namespace
            for (int i = 0; i < tokens.Length; i++) {
                string token = tokens[i];
                if (cat.DisplayPath.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0 &&
                    cat.Namespace.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0) {
                    return false;
                }
            }

            return true;
        }

        private static bool DetermineShownState(ParsedCategory cat, ParsedPack pack, Dictionary<string, bool> preferences) {
            // Check ancestors from root down to category itself
            for (int i = 0; i < cat.Ancestors.Count; i++) {
                string ancestorNs = cat.Ancestors[i];
                if (preferences != null && preferences.TryGetValue(ancestorNs, out bool isExplicitShown)) {
                    if (!isExplicitShown) return false;
                } else {
                    if (pack.CategoryLookup.TryGetValue(ancestorNs, out ParsedCategory ancestorCat) && !ancestorCat.DefaultToggle) {
                        return false;
                    }
                }
            }

            // Check category itself
            if (preferences != null && preferences.TryGetValue(cat.Namespace, out bool catExplicitShown)) {
                return catExplicitShown;
            }

            return cat.DefaultToggle;
        }

        private List<PackSource> DiscoverPacks(string markersDir) {
            var list = new List<PackSource>();

            // 1. Files: *.taco and *.zip directly in markers directory
            string[] files = Directory.GetFiles(markersDir, "*.*", SearchOption.TopDirectoryOnly);
            foreach (string file in files) {
                string ext = Path.GetExtension(file);
                if (string.Equals(ext, ".taco", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase)) {
                    var fi = new FileInfo(file);
                    list.Add(new PackSource(file, fi.Name, isDirectory: false, fi.Length, fi.LastWriteTimeUtc.Ticks));
                }
            }

            // 2. Unpacked pack subdirectories (excluding "data" folder)
            string[] dirs = Directory.GetDirectories(markersDir, "*", SearchOption.TopDirectoryOnly);
            foreach (string dir in dirs) {
                var di = new DirectoryInfo(dir);
                if (string.Equals(di.Name, "data", StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                // Check if directory contains any .xml files
                string[] xmlFiles = Directory.GetFiles(dir, "*.xml", SearchOption.AllDirectories);
                if (xmlFiles.Length > 0) {
                    long totalLength = 0;
                    long maxTicks = di.LastWriteTimeUtc.Ticks;
                    foreach (string xml in xmlFiles) {
                        var fi = new FileInfo(xml);
                        totalLength += fi.Length;
                        if (fi.LastWriteTimeUtc.Ticks > maxTicks) {
                            maxTicks = fi.LastWriteTimeUtc.Ticks;
                        }
                    }
                    list.Add(new PackSource(dir, di.Name, isDirectory: true, totalLength, maxTicks));
                }
            }

            return list;
        }

        private static Dictionary<string, bool> LoadPreferences(string markersDir) {
            var prefs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            string prefFile = Path.Combine(markersDir, "data", "states", "category_preferences.txt");

            if (!File.Exists(prefFile)) {
                return prefs;
            }

            try {
                string[] lines = File.ReadAllLines(prefFile);
                foreach (string rawLine in lines) {
                    string line = rawLine?.Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    if (line.StartsWith("+")) {
                        string ns = line.Substring(1).Trim();
                        if (ns.Length > 0) prefs[ns] = true;
                    } else if (line.StartsWith("-")) {
                        string ns = line.Substring(1).Trim();
                        if (ns.Length > 0) prefs[ns] = false;
                    }
                }
            } catch {
                // If preferences file cannot be read, ignore and use pack defaults.
            }

            return prefs;
        }

        private ParsedPack LoadOrParsePack(PackSource source) {
            lock (_lock) {
                // Check memory cache
                if (_inMemoryPacks.TryGetValue(source.Path, out ParsedPack cachedMem) &&
                    cachedMem.FileSize == source.Length &&
                    cachedMem.LastModifiedUtcTicks == source.LastModifiedTicks) {
                    return cachedMem;
                }
            }

            // Check disk cache
            string cacheFilePath = GetCacheFilePath(source);
            if (cacheFilePath != null && File.Exists(cacheFilePath)) {
                try {
                    string json = File.ReadAllText(cacheFilePath);
                    CachedPackData cachedData = JsonConvert.DeserializeObject<CachedPackData>(json);
                    if (cachedData != null &&
                        string.Equals(cachedData.Identifier, source.Identifier, StringComparison.OrdinalIgnoreCase) &&
                        cachedData.FileSize == source.Length &&
                        cachedData.LastModifiedUtcTicks == source.LastModifiedTicks) {
                        ParsedPack packFromCache = BuildParsedPackFromCache(source, cachedData);
                        lock (_lock) {
                            _inMemoryPacks[source.Path] = packFromCache;
                        }
                        return packFromCache;
                    }
                } catch {
                    // Invalid cache file; will re-parse.
                }
            }

            // Parse pack from file or folder
            ParsedPack parsed = source.IsDirectory ? ParseDirectoryPack(source) : ParseZipPack(source);

            if (parsed != null && cacheFilePath != null) {
                try {
                    SaveCache(cacheFilePath, source, parsed);
                } catch {
                    // Caching is best-effort.
                }
            }

            if (parsed != null) {
                lock (_lock) {
                    _inMemoryPacks[source.Path] = parsed;
                }
            }

            return parsed;
        }

        private string GetCacheFilePath(PackSource source) {
            if (_cacheDirectory == null) return null;

            string safeName = Regex.Replace(source.Identifier, @"[^\w\.\-]", "_");
            string hash = ComputeHash(source.Identifier);
            return Path.Combine(_cacheDirectory, safeName + "_" + hash + ".json");
        }

        private static string ComputeHash(string input) {
            using (var sha = System.Security.Cryptography.SHA256.Create()) {
                byte[] bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input ?? string.Empty));
                return BitConverter.ToString(bytes).Replace("-", string.Empty).Substring(0, 16).ToLowerInvariant();
            }
        }

        private static void SaveCache(string cacheFilePath, PackSource source, ParsedPack pack) {
            string dir = Path.GetDirectoryName(cacheFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) {
                Directory.CreateDirectory(dir);
            }

            var cachedData = new CachedPackData {
                Identifier           = source.Identifier,
                PackName             = pack.PackName,
                FileSize             = source.Length,
                LastModifiedUtcTicks = source.LastModifiedTicks,
                Categories           = pack.Categories.Select(c => new CachedCategoryData {
                    Namespace     = c.Namespace,
                    DisplayPath   = c.DisplayPath,
                    DefaultToggle = c.DefaultToggle,
                    Ancestors     = c.Ancestors
                }).ToList(),
                MapCounts = pack.MapCounts
            };

            string json = JsonConvert.SerializeObject(cachedData, Newtonsoft.Json.Formatting.None);
            File.WriteAllText(cacheFilePath, json);
        }

        private static ParsedPack BuildParsedPackFromCache(PackSource source, CachedPackData data) {
            var categories = new List<ParsedCategory>();
            var lookup = new Dictionary<string, ParsedCategory>(StringComparer.OrdinalIgnoreCase);

            foreach (CachedCategoryData c in data.Categories ?? Enumerable.Empty<CachedCategoryData>()) {
                var cat = new ParsedCategory(c.Namespace, c.DisplayPath, c.DefaultToggle, c.Ancestors);
                categories.Add(cat);
                lookup[cat.Namespace] = cat;
            }

            return new ParsedPack(
                source.Identifier,
                data.PackName ?? Path.GetFileNameWithoutExtension(source.Identifier),
                source.Length,
                source.LastModifiedTicks,
                categories,
                lookup,
                data.MapCounts ?? new Dictionary<int, Dictionary<string, int[]>>());
        }

        private static ParsedPack ParseZipPack(PackSource source) {
            using (ZipArchive zip = ZipFile.OpenRead(source.Path)) {
                var categories = new List<ParsedCategory>();
                var lookup = new Dictionary<string, ParsedCategory>(StringComparer.OrdinalIgnoreCase);
                string discoveredPackName = null;

                // 1. Parse categories
                foreach (ZipArchiveEntry entry in zip.Entries) {
                    if (entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) {
                        // Check if entry contains markercategory
                        using (Stream stream = entry.Open()) {
                            ParseCategoriesFromStream(stream, categories, lookup, ref discoveredPackName);
                        }
                    }
                }

                if (string.IsNullOrEmpty(discoveredPackName)) {
                    discoveredPackName = Path.GetFileNameWithoutExtension(source.Identifier);
                }

                // 2. Parse POIs and trails
                var mapCounts = new Dictionary<int, Dictionary<string, int[]>>();
                foreach (ZipArchiveEntry entry in zip.Entries) {
                    if (entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) {
                        int defaultMapId = ExtractMapIdFromFileName(entry.FullName);
                        using (Stream stream = entry.Open()) {
                            ParseMapElementsFromStream(stream, defaultMapId, mapCounts);
                        }
                    }
                }

                return new ParsedPack(source.Identifier, discoveredPackName, source.Length, source.LastModifiedTicks,
                                      categories, lookup, mapCounts);
            }
        }

        private static ParsedPack ParseDirectoryPack(PackSource source) {
            var categories = new List<ParsedCategory>();
            var lookup = new Dictionary<string, ParsedCategory>(StringComparer.OrdinalIgnoreCase);
            string discoveredPackName = null;

            string[] xmlFiles = Directory.GetFiles(source.Path, "*.xml", SearchOption.AllDirectories);

            foreach (string file in xmlFiles) {
                using (var stream = File.OpenRead(file)) {
                    ParseCategoriesFromStream(stream, categories, lookup, ref discoveredPackName);
                }
            }

            if (string.IsNullOrEmpty(discoveredPackName)) {
                discoveredPackName = source.Identifier;
            }

            var mapCounts = new Dictionary<int, Dictionary<string, int[]>>();
            foreach (string file in xmlFiles) {
                int defaultMapId = ExtractMapIdFromFileName(file);
                using (var stream = File.OpenRead(file)) {
                    ParseMapElementsFromStream(stream, defaultMapId, mapCounts);
                }
            }

            return new ParsedPack(source.Identifier, discoveredPackName, source.Length, source.LastModifiedTicks,
                                  categories, lookup, mapCounts);
        }

        private static int ExtractMapIdFromFileName(string path) {
            string name = Path.GetFileNameWithoutExtension(path);
            Match m = Regex.Match(name, @"map(\d+)", RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int id)) {
                return id;
            }
            return 0;
        }

        private static void ParseCategoriesFromStream(Stream stream, List<ParsedCategory> categories,
                                                     Dictionary<string, ParsedCategory> lookup,
                                                     ref string packName) {
            var settings = new XmlReaderSettings {
                IgnoreWhitespace             = true,
                IgnoreComments               = true,
                DtdProcessing                = DtdProcessing.Ignore,
                CheckCharacters              = false,
                XmlResolver                  = null
            };

            using (XmlReader reader = XmlReader.Create(stream, settings)) {
                var stack = new Stack<ParsedCategory>();
                bool advance = true;

                while (advance ? reader.Read() : !reader.EOF) {
                    advance = true;
                    if (reader.NodeType == XmlNodeType.Element &&
                        string.Equals(reader.LocalName, "markercategory", StringComparison.OrdinalIgnoreCase)) {

                        string isSeparator = reader.GetAttribute("isseparator");
                        if (string.Equals(isSeparator, "1", StringComparison.OrdinalIgnoreCase)) {
                            // Menu separator: skip if empty element, or skip subtree if container
                            if (!reader.IsEmptyElement) {
                                reader.Skip();
                                advance = false;
                            }
                            continue;
                        }

                        string name = reader.GetAttribute("name") ?? string.Empty;
                        string displayName = reader.GetAttribute("displayname");
                        if (string.IsNullOrWhiteSpace(displayName)) {
                            displayName = name;
                        }

                        string defaultToggleAttr = reader.GetAttribute("defaulttoggle");
                        bool defaultToggle = defaultToggleAttr == null || !string.Equals(defaultToggleAttr, "0", StringComparison.OrdinalIgnoreCase);

                        ParsedCategory parent = stack.Count > 0 ? stack.Peek() : null;

                        string ns = parent == null ? name : parent.Namespace + "." + name;
                        string displayPath = parent == null ? displayName : parent.DisplayPath + " › " + displayName;

                        if (parent == null && string.IsNullOrEmpty(packName) && !string.IsNullOrWhiteSpace(displayName)) {
                            packName = displayName;
                        }

                        var ancestors = new List<string>(parent != null ? parent.Ancestors : Enumerable.Empty<string>()) {
                            ns
                        };

                        var category = new ParsedCategory(ns, displayPath, defaultToggle, ancestors);

                        if (!lookup.ContainsKey(ns)) {
                            categories.Add(category);
                            lookup[ns] = category;
                        }

                        if (!reader.IsEmptyElement) {
                            stack.Push(category);
                        }
                    } else if (reader.NodeType == XmlNodeType.EndElement &&
                               string.Equals(reader.LocalName, "markercategory", StringComparison.OrdinalIgnoreCase)) {
                        if (stack.Count > 0) {
                            stack.Pop();
                        }
                    }
                }
            }
        }

        private static void ParseMapElementsFromStream(Stream stream, int defaultMapId,
                                                      Dictionary<int, Dictionary<string, int[]>> mapCounts) {
            var settings = new XmlReaderSettings {
                IgnoreWhitespace = true,
                IgnoreComments   = true,
                DtdProcessing    = DtdProcessing.Ignore,
                CheckCharacters  = false,
                XmlResolver      = null
            };

            int currentEnclosingMapId = defaultMapId;

            using (XmlReader reader = XmlReader.Create(stream, settings)) {
                while (reader.Read()) {
                    if (reader.NodeType == XmlNodeType.Element) {
                        string elementName = reader.LocalName.ToLowerInvariant();

                        if (elementName == "pois" || elementName == "overlaydata") {
                            string mapIdAttr = reader.GetAttribute("mapid");
                            if (mapIdAttr != null && int.TryParse(mapIdAttr, out int parsedMapId) && parsedMapId > 0) {
                                currentEnclosingMapId = parsedMapId;
                            }
                        }

                        if (elementName == "poi" || elementName == "trail") {
                            string type = reader.GetAttribute("type");
                            if (string.IsNullOrEmpty(type)) continue;

                            string mapIdAttr = reader.GetAttribute("mapid");
                            int elemMapId = currentEnclosingMapId;
                            if (mapIdAttr != null && int.TryParse(mapIdAttr, out int parsedId) && parsedId > 0) {
                                elemMapId = parsedId;
                            }

                            if (elemMapId <= 0) continue;

                            if (!mapCounts.TryGetValue(elemMapId, out Dictionary<string, int[]> typeDict)) {
                                typeDict = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
                                mapCounts[elemMapId] = typeDict;
                            }

                            if (!typeDict.TryGetValue(type, out int[] counts)) {
                                counts = new int[2]; // [poiCount, trailCount]
                                typeDict[type] = counts;
                            }

                            if (elementName == "poi") {
                                counts[0]++;
                            } else {
                                counts[1]++;
                            }
                        }
                    }
                }
            }
        }

        private sealed class PackSource
        {
            public PackSource(string path, string identifier, bool isDirectory, long length, long lastModifiedTicks) {
                Path              = path;
                Identifier        = identifier;
                IsDirectory       = isDirectory;
                Length            = length;
                LastModifiedTicks = lastModifiedTicks;
            }

            public string Path              { get; }
            public string Identifier        { get; }
            public bool   IsDirectory       { get; }
            public long   Length            { get; }
            public long   LastModifiedTicks { get; }
        }

        private sealed class ParsedCategory
        {
            public ParsedCategory(string @namespace, string displayPath, bool defaultToggle, List<string> ancestors) {
                Namespace     = @namespace;
                DisplayPath   = displayPath;
                DefaultToggle = defaultToggle;
                Ancestors     = ancestors;
            }

            public string       Namespace     { get; }
            public string       DisplayPath   { get; }
            public bool         DefaultToggle { get; }
            public List<string> Ancestors     { get; }
        }

        private sealed class ParsedPack
        {
            public ParsedPack(string identifier, string packName, long fileSize, long lastModifiedUtcTicks,
                              List<ParsedCategory> categories, Dictionary<string, ParsedCategory> categoryLookup,
                              Dictionary<int, Dictionary<string, int[]>> mapCounts) {
                Identifier           = identifier;
                PackName             = packName;
                FileSize             = fileSize;
                LastModifiedUtcTicks = lastModifiedUtcTicks;
                Categories           = categories;
                CategoryLookup       = categoryLookup;
                MapCounts            = mapCounts;
            }

            public string                                    Identifier           { get; }
            public string                                    PackName             { get; }
            public long                                      FileSize             { get; }
            public long                                      LastModifiedUtcTicks { get; }
            public List<ParsedCategory>                      Categories           { get; }
            public Dictionary<string, ParsedCategory>        CategoryLookup       { get; }
            public Dictionary<int, Dictionary<string, int[]>> MapCounts            { get; }

            public void GetCountsForMap(int mapId, string categoryNs, out int markerCount, out int trailCount) {
                markerCount = 0;
                trailCount  = 0;

                if (!MapCounts.TryGetValue(mapId, out Dictionary<string, int[]> typeDict)) {
                    return;
                }

                string prefix = categoryNs + ".";

                foreach (KeyValuePair<string, int[]> entry in typeDict) {
                    if (string.Equals(entry.Key, categoryNs, StringComparison.OrdinalIgnoreCase) ||
                        entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                        markerCount += entry.Value[0];
                        trailCount  += entry.Value[1];
                    }
                }
            }
        }

        private sealed class CachedPackData
        {
            public string                                    Identifier           { get; set; }
            public string                                    PackName             { get; set; }
            public long                                      FileSize             { get; set; }
            public long                                      LastModifiedUtcTicks { get; set; }
            public List<CachedCategoryData>                  Categories           { get; set; }
            public Dictionary<int, Dictionary<string, int[]>> MapCounts            { get; set; }
        }

        private sealed class CachedCategoryData
        {
            public string       Namespace     { get; set; }
            public string       DisplayPath   { get; set; }
            public bool         DefaultToggle { get; set; }
            public List<string> Ancestors     { get; set; }
        }
    }
}
