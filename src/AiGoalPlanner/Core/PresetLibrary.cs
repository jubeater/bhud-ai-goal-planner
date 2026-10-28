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

        /// <summary>
        /// The presets as one more paragraph of the system prompt: each button's name and the
        /// hints the AI follows when the Chat is about that goal. Empty when there are no
        /// presets, so the prompt reads as if they were never there.
        /// </summary>
        public static string SectionFor(IReadOnlyList<Preset> presets) {
            if (presets == null || presets.Count == 0) return string.Empty;

            var lines = new List<string> {
                "The Player can also start from a Preset button, which fills the text box with a "
              + "starter Request the Player may edit before sending. When a Request matches one of "
              + "these goals, follow its hints:"
            };

            foreach (Preset preset in presets) {
                lines.Add("- " + preset.Name + ": " + preset.Hints);
            }

            return string.Join("\n", lines);
        }
    }
}
