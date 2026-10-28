using System;
using Newtonsoft.Json.Linq;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// One ready-made Request for a common goal: the button's name, the starter Request that fills
    /// the text box for the Player to edit, and the hints the AI follows when the Chat is about
    /// that goal.
    /// </summary>
    public sealed class Preset
    {
        public Preset(string name, string request, string hints) {
            Name    = name    ?? string.Empty;
            Request = request ?? string.Empty;
            Hints   = hints   ?? string.Empty;
        }

        public string Name { get; }

        public string Request { get; }

        public string Hints { get; }

        /// <summary>
        /// Reads one preset data file: its name, starter Request and hints. Anything else in the
        /// file is ignored, so a data file can carry notes for its author. A file with a missing
        /// or blank field is not a preset and reads as null, so one bad file cannot take the
        /// others down with it.
        /// </summary>
        public static Preset Parse(string json) {
            if (string.IsNullOrWhiteSpace(json)) return null;

            JObject file;

            try {
                file = JObject.Parse(json);
            } catch (Exception) {
                return null;
            }

            string name    = file.Value<string>("name");
            string request = file.Value<string>("request");
            string hints   = file.Value<string>("hints");

            if (string.IsNullOrWhiteSpace(name)
             || string.IsNullOrWhiteSpace(request)
             || string.IsNullOrWhiteSpace(hints)) {
                return null;
            }

            return new Preset(name.Trim(), request.Trim(), hints.Trim());
        }
    }
}
