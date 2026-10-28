using System;
using System.Collections.Generic;
using System.Linq;
using jubeater.aigoalplanner.Core;

namespace jubeater.aigoalplanner.Tests.Fakes
{
#if DEBUG
    /// <summary>
    /// The developer channel's inbox as tests see it: request files are added in memory, handled in
    /// file-name order like the real folders, and responses land in a dictionary by request id.
    /// </summary>
    internal sealed class FakeDevInbox : IDevInbox
    {
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Responses written so far, by request id.</summary>
        public readonly Dictionary<string, string> Responses = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Request files removed after handling, in the order they were removed.</summary>
        public readonly List<string> Removed = new List<string>();

        public void AddFile(string fileName, string json) {
            _files[fileName] = json;
        }

        public IReadOnlyList<string> Waiting {
            get { return _files.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList(); }
        }

        public string Read(string fileName) {
            return _files[fileName];
        }

        public void Remove(string fileName) {
            _files.Remove(fileName);
            Removed.Add(fileName);
        }

        public void Respond(string id, string json) {
            Responses[id] = json;
        }
    }
#endif
}
