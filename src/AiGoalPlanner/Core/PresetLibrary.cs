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
        /// <paramref name="logDebug"/> hears about each skipped file by name and failure type, so
        /// a broken data file is visible in the log without ever logging file content.
        /// </summary>
        public static IReadOnlyList<Preset> Load(Action<string> logDebug = null) {
            Assembly assembly = typeof(PresetLibrary).Assembly;

            IEnumerable<string> resources = assembly.GetManifestResourceNames()
                                                    .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                                                                && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                                                    .OrderBy(name => name);

            return LoadFrom(resources, resource => ReadResource(assembly, resource), logDebug);
        }

        /// <summary>
        /// The presets in <paramref name="resources"/>, read through <paramref name="read"/>, so a
        /// test can hold a broken file without shipping one. A file that cannot be read or parsed
        /// is skipped with a <paramref name="logDebug"/> line naming the file and the failure type,
        /// and the rest still load.
        /// </summary>
        public static IReadOnlyList<Preset> LoadFrom(IEnumerable<string> resources, Func<string, string> read,
                                                     Action<string> logDebug) {
            var presets = new List<Preset>();

            foreach (string resource in resources ?? Enumerable.Empty<string>()) {
                Preset preset = null;

                try {
                    preset = Preset.Parse(read(resource));
                } catch (Exception exception) {
                    logDebug?.Invoke("Skipped preset " + resource + " (" + exception.GetType().Name + ").");
                    continue;
                }

                if (preset == null) {
                    logDebug?.Invoke("Skipped preset " + resource + " (missing field).");
                    continue;
                }

                presets.Add(preset);
            }

            return presets;
        }

        private static string ReadResource(Assembly assembly, string resource) {
            using (Stream stream = assembly.GetManifestResourceStream(resource))
            using (var reader = new StreamReader(stream)) {
                return reader.ReadToEnd();
            }
        }

        /// <summary>
        /// The presets as one more paragraph of the system prompt: each button's name and the
        /// hints the AI follows when the Chat is about that goal. Empty when there are no
        /// presets, so the prompt reads as if they were never there.
        /// </summary>
        public static string SystemPromptSectionFor(IReadOnlyList<Preset> presets) {
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
