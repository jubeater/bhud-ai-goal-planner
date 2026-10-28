using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// The Presets bundled with the module: every <c>Presets/*.json</c> data file, so adding one
    /// needs no code change. What the files name — goals, packs, categories, content — stays in
    /// the files; this class only reads them.
    /// </summary>
    public static class PresetLibrary
    {
        private const string ResourcePrefix = "jubeater.aigoalplanner.Presets.";

        /// <summary>
        /// Every bundled preset, oldest file name first, with the bad files left out.
        /// </summary>
        public static IReadOnlyList<Preset> Load() {
            return LoadFrom(typeof(PresetLibrary).Assembly, ResourcePrefix);
        }

        internal static IReadOnlyList<Preset> LoadFrom(Assembly assembly, string prefix) {
            var presets = new List<Preset>();

            IEnumerable<string> resources = assembly.GetManifestResourceNames()
                                                    .Where(name => name.StartsWith(prefix, StringComparison.Ordinal)
                                                                && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                                                    .OrderBy(name => name);

            foreach (string resource in resources) {
                using (Stream stream = assembly.GetManifestResourceStream(resource))
                using (var reader = new StreamReader(stream)) {
                    Preset preset = Preset.Parse(reader.ReadToEnd());

                    if (preset != null) presets.Add(preset);
                }
            }

            return presets;
        }
    }
}
