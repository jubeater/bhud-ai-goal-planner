using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace jubeater.aigoalplanner.Core
{
#if DEBUG
    /// <summary>
    /// The folders the developer channel reads request files from and writes response files to: one
    /// <c>dev\inbox</c> and one <c>dev\outbox</c> under the module's own data folder. The module
    /// implements it over the real folders; tests implement it over an in-memory list, so no real file
    /// ever reaches the disk from a test.
    /// </summary>
    public interface IDevInbox
    {
        /// <summary>The request files waiting to be handled, by file name, oldest name first.</summary>
        IReadOnlyList<string> Waiting { get; }

        /// <summary>Reads one waiting request file.</summary>
        string Read(string fileName);

        /// <summary>Deletes a handled request file.</summary>
        void Remove(string fileName);

        /// <summary>Writes a response file for a handled request.</summary>
        void Respond(string id, string json);
    }

    /// <summary>
    /// The developer channel's folders as the module sees them, under its own data folder
    /// (<paramref name="root"/>): request files land in <c>dev\inbox</c>, responses in
    /// <c>dev\outbox</c>, and the channel handles the requests one at a time, in file-name order.
    /// </summary>
    public sealed class DevInbox : IDevInbox
    {
        private readonly string _root;

        public DevInbox(string root) {
            _root = root ?? throw new ArgumentNullException(nameof(root));
        }

        /// <summary>
        /// The <c>*.json</c> request files waiting, by file name, oldest name first. A missing inbox
        /// folder means no requests are waiting, not an error.
        /// </summary>
        public IReadOnlyList<string> Waiting {
            get {
                if (!Directory.Exists(Inbox)) return new string[0];

                return Directory.GetFiles(Inbox, "*.json")
                                .Select(Path.GetFileName)
                                .OrderBy(name => name, StringComparer.Ordinal)
                                .ToList();
            }
        }

        public string Read(string fileName) {
            return File.ReadAllText(Path.Combine(Inbox, fileName));
        }

        public void Remove(string fileName) {
            File.Delete(Path.Combine(Inbox, fileName));
        }

        public void Respond(string id, string json) {
            Directory.CreateDirectory(Outbox);

            File.WriteAllText(Path.Combine(Outbox, id + ".json"), json);
        }

        private string Inbox {
            get { return Path.Combine(_root, "dev", "inbox"); }
        }

        private string Outbox {
            get { return Path.Combine(_root, "dev", "outbox"); }
        }
    }
}
#endif
