using System;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// What came of enabling or disabling a Module: whether it happened, the state it had before
    /// (so Undo can put it back), and the reason when it did not.
    /// </summary>
    /// <remarks>
    /// A write answers from live state, the way a read does: everything it touches happens inside the
    /// call, which <c>GameThreadWorld</c> hands to the game thread (ADR-0009).
    /// </remarks>
    public sealed class ModuleChange
    {
        public ModuleChange(bool applied, bool wasEnabled, string problem = null) {
            Applied    = applied;
            WasEnabled = wasEnabled;
            Problem    = problem ?? string.Empty;
        }

        public bool Applied { get; }

        /// <summary>The state before the change, whether or not it happened.</summary>
        public bool WasEnabled { get; }

        /// <summary>Why it did not happen, or empty.</summary>
        public string Problem { get; }
    }

    /// <summary>
    /// What came of changing a Setting: whether it happened, the value it had before (so Undo can
    /// put it back exactly), and the reason when it did not.
    /// </summary>
    public sealed class ModuleSettingChange
    {
        public ModuleSettingChange(bool applied, string previousValue, string problem = null) {
            Applied       = applied;
            PreviousValue = previousValue ?? string.Empty;
            Problem       = problem      ?? string.Empty;
        }

        public bool Applied { get; }

        /// <summary>The value before the change, in the same text form a Lookup would read.</summary>
        public string PreviousValue { get; }

        /// <summary>Why it did not happen, or empty.</summary>
        public string Problem { get; }
    }
}
