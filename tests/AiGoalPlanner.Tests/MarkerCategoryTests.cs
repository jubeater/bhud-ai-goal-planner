using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.World;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    public class MarkerCategoryTests : IDisposable
    {
        private readonly string _testRoot;
        private readonly string _markersDir;
        private readonly string _cacheDir;

        public MarkerCategoryTests() {
            _testRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestMarkers_" + Guid.NewGuid().ToString("N"));
            _markersDir = Path.Combine(_testRoot, "markers");
            _cacheDir = Path.Combine(_testRoot, "cache");
            Directory.CreateDirectory(_markersDir);
            Directory.CreateDirectory(_cacheDir);
        }

        public void Dispose() {
            try {
                if (Directory.Exists(_testRoot)) {
                    Directory.Delete(_testRoot, recursive: true);
                }
            } catch {
                // Best-effort cleanup of test files.
            }
        }

        [Fact]
        public void No_packs_installed_returns_no_packs_status() {
            var service = new MarkerCategoryService(_markersDir, _cacheDir);

            MarkerCategoriesResult result = service.FindCategories("hero points", 1210);

            Assert.Equal(MarkerCategoriesStatus.NoPacks, result.Status);
            Assert.Contains("No marker packs", result.Message);
        }

        [Fact]
        public void Nested_categories_namespaces_and_separators() {
            // Hand-made pack zip with nested categories and separators
            string packZip = Path.Combine(_markersDir, "test_pack.taco");
            using (var zip = ZipFile.Open(packZip, ZipArchiveMode.Create)) {
                var catEntry = zip.CreateEntry("_markerCategories.xml");
                using (var writer = new StreamWriter(catEntry.Open(), Encoding.UTF8)) {
                    writer.Write(@"<?xml version=""1.0"" encoding=""utf-8""?>
<overlaydata>
  <markercategory name=""guide"" displayname=""Tekkit's Guides"">
    <markercategory name=""sep01"" isseparator=""1"" displayname=""--- SEPARATOR ---"" />
    <markercategory name=""pof"" displayname=""Path of Fire"">
      <markercategory name=""hp"" displayname=""Routes - Hero Points Only"" defaulttoggle=""1"">
        <markercategory name=""crystal"" displayname=""Crystal Oasis"">
          <markercategory name=""finish"" displayname=""Finish Sign"" />
        </markercategory>
      </markercategory>
    </markercategory>
  </markercategory>
</overlaydata>");
                }
            }

            var service = new MarkerCategoryService(_markersDir, _cacheDir);
            MarkerCategoriesResult result = service.FindCategories("hero points", 1210);

            Assert.Equal(MarkerCategoriesStatus.Ok, result.Status);
            // Separator should not be in the results
            Assert.DoesNotContain(result.Categories, c => c.Namespace.Contains("sep01"));

            MarkerCategoryMatch hpCategory = result.Categories.FirstOrDefault(c => c.Namespace == "guide.pof.hp");
            Assert.NotNull(hpCategory);
            Assert.Equal("Tekkit's Guides › Path of Fire › Routes - Hero Points Only", hpCategory.DisplayPath);
            Assert.Equal("guide.pof.hp", hpCategory.Namespace);
            Assert.Equal("Tekkit's Guides", hpCategory.PackName);
            Assert.True(hpCategory.Shown);
        }

        [Fact]
        public void Missing_defaulttoggle_defaults_to_shown() {
            string packZip = Path.Combine(_markersDir, "pack.zip");
            using (var zip = ZipFile.Open(packZip, ZipArchiveMode.Create)) {
                var catEntry = zip.CreateEntry("_markerCategories.xml");
                using (var writer = new StreamWriter(catEntry.Open(), Encoding.UTF8)) {
                    writer.Write(@"<?xml version=""1.0"" encoding=""utf-8""?>
<overlaydata>
  <markercategory name=""root"" displayname=""Pack Root"">
    <markercategory name=""no_toggle"" displayname=""No Toggle Category"" />
    <markercategory name=""off_toggle"" displayname=""Off Category"" defaulttoggle=""0"" />
  </markercategory>
</overlaydata>");
                }
            }

            var service = new MarkerCategoryService(_markersDir, _cacheDir);
            MarkerCategoriesResult result = service.FindCategories("Category", 1210);

            MarkerCategoryMatch noToggle = result.Categories.First(c => c.Namespace == "root.no_toggle");
            MarkerCategoryMatch offToggle = result.Categories.First(c => c.Namespace == "root.off_toggle");

            Assert.True(noToggle.Shown);
            Assert.False(offToggle.Shown);
        }

        [Fact]
        public void Hidden_parent_in_preferences_hides_all_children_even_with_plus_child() {
            string packZip = Path.Combine(_markersDir, "pack.zip");
            using (var zip = ZipFile.Open(packZip, ZipArchiveMode.Create)) {
                var catEntry = zip.CreateEntry("_markerCategories.xml");
                using (var writer = new StreamWriter(catEntry.Open(), Encoding.UTF8)) {
                    writer.Write(@"<?xml version=""1.0"" encoding=""utf-8""?>
<overlaydata>
  <markercategory name=""parent"" displayname=""Parent Category"" defaulttoggle=""1"">
    <markercategory name=""child"" displayname=""Child Category"" defaulttoggle=""1"" />
  </markercategory>
</overlaydata>");
                }
            }

            // Write category_preferences.txt: parent is - but child is +
            string statesDir = Path.Combine(_markersDir, "data", "states");
            Directory.CreateDirectory(statesDir);
            File.WriteAllLines(Path.Combine(statesDir, "category_preferences.txt"), new[] {
                "-parent",
                "+parent.child"
            });

            var service = new MarkerCategoryService(_markersDir, _cacheDir);
            MarkerCategoriesResult result = service.FindCategories("Category", 1210);

            MarkerCategoryMatch parent = result.Categories.First(c => c.Namespace == "parent");
            MarkerCategoryMatch child = result.Categories.First(c => c.Namespace == "parent.child");

            Assert.False(parent.Shown);
            // Hidden parent hides everything under it!
            Assert.False(child.Shown);
        }

        [Fact]
        public void Explicit_preference_turns_category_on_and_off() {
            string packZip = Path.Combine(_markersDir, "pack.zip");
            using (var zip = ZipFile.Open(packZip, ZipArchiveMode.Create)) {
                var catEntry = zip.CreateEntry("_markerCategories.xml");
                using (var writer = new StreamWriter(catEntry.Open(), Encoding.UTF8)) {
                    writer.Write(@"<?xml version=""1.0"" encoding=""utf-8""?>
<overlaydata>
  <markercategory name=""root"" displayname=""Root"">
    <markercategory name=""cat_a"" displayname=""Category A"" defaulttoggle=""1"" />
    <markercategory name=""cat_b"" displayname=""Category B"" defaulttoggle=""0"" />
  </markercategory>
</overlaydata>");
                }
            }

            string statesDir = Path.Combine(_markersDir, "data", "states");
            Directory.CreateDirectory(statesDir);
            File.WriteAllLines(Path.Combine(statesDir, "category_preferences.txt"), new[] {
                "-root.cat_a",
                "+root.cat_b"
            });

            var service = new MarkerCategoryService(_markersDir, _cacheDir);
            MarkerCategoriesResult result = service.FindCategories("Category", 1210);

            MarkerCategoryMatch catA = result.Categories.First(c => c.Namespace == "root.cat_a");
            MarkerCategoryMatch catB = result.Categories.First(c => c.Namespace == "root.cat_b");

            Assert.False(catA.Shown); // was default 1, explicitly disabled with -
            Assert.True(catB.Shown);  // was default 0, explicitly enabled with +
        }

        [Fact]
        public void Map_counts_from_poi_and_trail() {
            string packZip = Path.Combine(_markersDir, "pack.taco");
            using (var zip = ZipFile.Open(packZip, ZipArchiveMode.Create)) {
                var catEntry = zip.CreateEntry("_markerCategories.xml");
                using (var writer = new StreamWriter(catEntry.Open(), Encoding.UTF8)) {
                    writer.Write(@"<?xml version=""1.0"" encoding=""utf-8""?>
<overlaydata>
  <markercategory name=""guide"" displayname=""Tekkit's Guides"">
    <markercategory name=""hp"" displayname=""Routes - Hero Points Only"">
      <markercategory name=""crystaloasis"" displayname=""Crystal Oasis"">
        <markercategory name=""route"" displayname=""Route"" />
        <markercategory name=""start"" displayname=""Start"" />
        <markercategory name=""finish"" displayname=""Finish"" />
      </markercategory>
    </markercategory>
  </markercategory>
</overlaydata>");
                }

                // Map 1210: 2 POIs and 1 Trail under hp category
                var map1210 = zip.CreateEntry("map1210.xml");
                using (var writer = new StreamWriter(map1210.Open(), Encoding.UTF8)) {
                    writer.Write(@"<?xml version=""1.0"" encoding=""utf-8""?>
<overlaydata>
  <pois>
    <poi type=""guide.hp.crystaloasis.start"" mapid=""1210"" xpos=""1"" ypos=""2"" zpos=""3"" />
    <poi type=""guide.hp.crystaloasis.finish"" mapid=""1210"" xpos=""4"" ypos=""5"" zpos=""6"" />
    <trail type=""guide.hp.crystaloasis.route"" mapid=""1210"" traildata=""Data/test.trl"" />
  </pois>
</overlaydata>");
                }

                // Map 1000: 1 POI under hp category
                var map1000 = zip.CreateEntry("map1000.xml");
                using (var writer = new StreamWriter(map1000.Open(), Encoding.UTF8)) {
                    writer.Write(@"<?xml version=""1.0"" encoding=""utf-8""?>
<overlaydata>
  <pois>
    <poi type=""guide.hp.crystaloasis.start"" mapid=""1000"" xpos=""1"" ypos=""2"" zpos=""3"" />
  </pois>
</overlaydata>");
                }
            }

            var service = new MarkerCategoryService(_markersDir, _cacheDir);
            MarkerCategoriesResult result1210 = service.FindCategories("Hero Points", 1210);

            MarkerCategoryMatch match1210 = result1210.Categories.First(c => c.Namespace == "guide.hp");
            Assert.Equal(2, match1210.MarkerCount);
            Assert.Equal(1, match1210.TrailCount);
            Assert.Equal(3, match1210.TotalCount);

            MarkerCategoriesResult result1000 = service.FindCategories("Hero Points", 1000);
            MarkerCategoryMatch match1000 = result1000.Categories.First(c => c.Namespace == "guide.hp");
            Assert.Equal(1, match1000.MarkerCount);
            Assert.Equal(0, match1000.TrailCount);
            Assert.Equal(1, match1000.TotalCount);
        }

        [Fact]
        public void Unpacked_pack_folder_is_loaded_and_counted() {
            string folderPack = Path.Combine(_markersDir, "UnpackedPack");
            Directory.CreateDirectory(folderPack);

            File.WriteAllText(Path.Combine(folderPack, "_markerCategories.xml"), @"<?xml version=""1.0"" encoding=""utf-8""?>
<overlaydata>
  <markercategory name=""folder_pack"" displayname=""Folder Pack"">
    <markercategory name=""vistas"" displayname=""Vista Routes"" />
  </markercategory>
</overlaydata>");

            File.WriteAllText(Path.Combine(folderPack, "map1210.xml"), @"<?xml version=""1.0"" encoding=""utf-8""?>
<overlaydata>
  <pois>
    <poi type=""folder_pack.vistas"" mapid=""1210"" />
  </pois>
</overlaydata>");

            var service = new MarkerCategoryService(_markersDir, _cacheDir);
            MarkerCategoriesResult result = service.FindCategories("Vista", 1210);

            Assert.Single(result.Categories);
            MarkerCategoryMatch match = result.Categories[0];
            Assert.Equal("Folder Pack › Vista Routes", match.DisplayPath);
            Assert.Equal(1, match.MarkerCount);
        }

        [Fact]
        public void Broken_zip_is_skipped_and_named_while_valid_packs_work() {
            // Write a valid pack
            string validPack = Path.Combine(_markersDir, "valid.zip");
            using (var zip = ZipFile.Open(validPack, ZipArchiveMode.Create)) {
                var catEntry = zip.CreateEntry("_markerCategories.xml");
                using (var writer = new StreamWriter(catEntry.Open(), Encoding.UTF8)) {
                    writer.Write(@"<?xml version=""1.0"" encoding=""utf-8""?>
<overlaydata>
  <markercategory name=""valid"" displayname=""Valid Pack"">
    <markercategory name=""route"" displayname=""Valid Route"" />
  </markercategory>
</overlaydata>");
                }
            }

            // Write a corrupt zip
            string corruptPack = Path.Combine(_markersDir, "broken_pack.zip");
            File.WriteAllBytes(corruptPack, new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x99, 0x88, 0x77 });

            var service = new MarkerCategoryService(_markersDir, _cacheDir);
            MarkerCategoriesResult result = service.FindCategories("Route", 1210);

            Assert.Equal(MarkerCategoriesStatus.Ok, result.Status);
            Assert.Single(result.Categories);
            Assert.Equal("Valid Pack › Valid Route", result.Categories[0].DisplayPath);
            Assert.Contains("broken_pack.zip", result.SkippedPacks);
        }

        [Fact]
        public void Result_cap_at_30_and_sorting_places_matches_on_current_map_first() {
            string packZip = Path.Combine(_markersDir, "large_pack.zip");
            using (var zip = ZipFile.Open(packZip, ZipArchiveMode.Create)) {
                var catEntry = zip.CreateEntry("_markerCategories.xml");
                using (var writer = new StreamWriter(catEntry.Open(), Encoding.UTF8)) {
                    var sb = new StringBuilder();
                    sb.AppendLine(@"<?xml version=""1.0"" encoding=""utf-8""?><overlaydata><markercategory name=""root"" displayname=""Big Pack"">");
                    // Create 40 categories matching "Route"
                    for (int i = 1; i <= 40; i++) {
                        sb.AppendLine($@"<markercategory name=""route_{i}"" displayname=""Route {i}"" />");
                    }
                    sb.AppendLine(@"</markercategory></overlaydata>");
                    writer.Write(sb.ToString());
                }

                // Put markers on map 1210 for route_35 only
                var map1210 = zip.CreateEntry("map1210.xml");
                using (var writer = new StreamWriter(map1210.Open(), Encoding.UTF8)) {
                    writer.Write(@"<?xml version=""1.0"" encoding=""utf-8""?>
<overlaydata>
  <pois>
    <poi type=""root.route_35"" mapid=""1210"" />
  </pois>
</overlaydata>");
                }
            }

            var service = new MarkerCategoryService(_markersDir, _cacheDir);
            MarkerCategoriesResult result = service.FindCategories("Route", 1210);

            Assert.Equal(MarkerCategoriesStatus.Ok, result.Status);
            Assert.True(result.Cut);
            Assert.Equal(40, result.TotalMatches);
            Assert.Equal(30, result.Categories.Count);

            // Matches on the current map come first!
            Assert.Equal("Big Pack › Route 35", result.Categories[0].DisplayPath);
            Assert.Equal(1, result.Categories[0].MarkerCount);
        }

        [Fact]
        public void Cache_is_written_and_reused_on_subsequent_calls() {
            string packZip = Path.Combine(_markersDir, "cached_pack.zip");
            using (var zip = ZipFile.Open(packZip, ZipArchiveMode.Create)) {
                var catEntry = zip.CreateEntry("_markerCategories.xml");
                using (var writer = new StreamWriter(catEntry.Open(), Encoding.UTF8)) {
                    writer.Write(@"<?xml version=""1.0"" encoding=""utf-8""?>
<overlaydata>
  <markercategory name=""cached"" displayname=""Cached Pack"">
    <markercategory name=""hp"" displayname=""Hero Points"" />
  </markercategory>
</overlaydata>");
                }
            }

            var service1 = new MarkerCategoryService(_markersDir, _cacheDir);
            MarkerCategoriesResult result1 = service1.FindCategories("Hero Points", 1210);
            Assert.Single(result1.Categories);

            // Cache files should exist in _cacheDir
            string[] cacheFiles = Directory.GetFiles(_cacheDir, "*.json", SearchOption.AllDirectories);
            Assert.NotEmpty(cacheFiles);

            // Create a second service pointing to same cache
            var service2 = new MarkerCategoryService(_markersDir, _cacheDir);
            MarkerCategoriesResult result2 = service2.FindCategories("Hero Points", 1210);
            Assert.Single(result2.Categories);
            Assert.Equal(result1.Categories[0].DisplayPath, result2.Categories[0].DisplayPath);
        }

        [Fact]
        public void Review_failed_pack_does_not_log_local_filename() {
            var logged = new System.Collections.Generic.List<string>();
            string corruptPack = Path.Combine(_markersDir, "PRIVATE-route.zip");
            File.WriteAllBytes(corruptPack, new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x99, 0x88, 0x77 });

            var service = new MarkerCategoryService(_markersDir, _cacheDir, msg => logged.Add(msg));
            MarkerCategoriesResult result = service.FindCategories("route", 1210);

            Assert.Contains("PRIVATE-route.zip", result.SkippedPacks);
            Assert.NotEmpty(logged);
            foreach (string line in logged) {
                Assert.DoesNotContain("PRIVATE-route.zip", line);
            }
        }

        [Fact]
        public void Review_module_declares_the_markers_directory_it_requests() {
            // Find manifest.json in src/AiGoalPlanner/manifest.json
            DirectoryInfo dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "AiGoalPlanner", "manifest.json"))) {
                dir = dir.Parent;
            }
            Assert.NotNull(dir);
            string manifestPath = Path.Combine(dir.FullName, "src", "AiGoalPlanner", "manifest.json");
            string json = File.ReadAllText(manifestPath);
            var manifest = Newtonsoft.Json.Linq.JObject.Parse(json);
            var directories = manifest["directories"]?.Select(d => (string)d).ToList();
            Assert.NotNull(directories);
            Assert.Contains("markers", directories);
        }

        [Fact]
        public void Review_nonempty_separator_preserves_following_category() {
            string packZip = Path.Combine(_markersDir, "paired_sep.zip");
            using (var zip = ZipFile.Open(packZip, ZipArchiveMode.Create)) {
                var catEntry = zip.CreateEntry("_markerCategories.xml");
                using (var writer = new StreamWriter(catEntry.Open(), Encoding.UTF8)) {
                    writer.Write(@"<?xml version=""1.0"" encoding=""utf-8""?>
<overlaydata>
  <markercategory name=""root"" displayname=""Root"">
    <markercategory name=""sep"" isseparator=""1""></markercategory>
    <markercategory name=""hp"" displayname=""Hero Points"" />
  </markercategory>
</overlaydata>");
                }
            }

            var service = new MarkerCategoryService(_markersDir, _cacheDir);
            MarkerCategoriesResult result = service.FindCategories("Hero Points", 1210);

            Assert.Single(result.Categories);
            Assert.Equal("root.hp", result.Categories[0].Namespace);
        }

        [Fact]
        public void Review_distinct_pack_names_keep_distinct_cached_categories() {
            string xmlAlpha = @"<?xml version=""1.0"" encoding=""utf-8""?>
<overlaydata>
  <markercategory name=""root"" displayname=""Root"">
    <markercategory name=""alpha"" displayname=""Alpha Route"" />
  </markercategory>
</overlaydata>";

            string xmlBravo = @"<?xml version=""1.0"" encoding=""utf-8""?>
<overlaydata>
  <markercategory name=""root"" displayname=""Root"">
    <markercategory name=""bravo"" displayname=""Bravo Route"" />
  </markercategory>
</overlaydata>";

            // Ensure identical byte length by padding spaces
            int maxLen = Math.Max(xmlAlpha.Length, xmlBravo.Length);
            xmlAlpha = xmlAlpha.PadRight(maxLen);
            xmlBravo = xmlBravo.PadRight(maxLen);

            string pack1 = Path.Combine(_markersDir, "route pack.taco");
            string pack2 = Path.Combine(_markersDir, "route_pack.taco");

            using (var zip = ZipFile.Open(pack1, ZipArchiveMode.Create)) {
                var entry = zip.CreateEntry("_markerCategories.xml");
                using (var w = new StreamWriter(entry.Open(), Encoding.UTF8)) w.Write(xmlAlpha);
            }
            using (var zip = ZipFile.Open(pack2, ZipArchiveMode.Create)) {
                var entry = zip.CreateEntry("_markerCategories.xml");
                using (var w = new StreamWriter(entry.Open(), Encoding.UTF8)) w.Write(xmlBravo);
            }

            DateTime fixedTime = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(pack1, fixedTime);
            File.SetLastWriteTimeUtc(pack2, fixedTime);

            var service = new MarkerCategoryService(_markersDir, _cacheDir);
            MarkerCategoriesResult result = service.FindCategories("Route", 1210);

            var foundNamespaces = result.Categories.Select(c => c.Namespace).OrderBy(x => x).ToList();
            Assert.Equal(new[] { "root.alpha", "root.bravo" }, foundNamespaces);
        }
    }
}
