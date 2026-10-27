using System;
using System.Collections.Generic;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// The last few Applies, newest last, so the Player can Undo one after another. Ten are kept: an
    /// Undo is a safety net for a plan that just went wrong, not an edit history, and a journal that
    /// grew without end would outlive the state it describes once modules are reloaded.
    /// </summary>
    /// <remarks>
    /// The journal holds what it takes to put the Player's configuration back: Module states and
    /// Setting values, never anything the model wrote. It is never sent to the model and never
    /// shown in the Chat beyond the one line an Undo says; only Apply and Undo read it.
    /// </remarks>
    public sealed class UndoJournal
    {
        /// <summary>How many Applies are remembered. The oldest falls out when another arrives.</summary>
        public const int Capacity = 10;

        private readonly List<AppliedPlan> _applied = new List<AppliedPlan>();
        private readonly object            _lock    = new object();

        /// <summary>How many Applies can still be undone.</summary>
        public int Count {
            get {
                lock (_lock) {
                    return _applied.Count;
                }
            }
        }

        /// <summary>
        /// Remembers one Apply. An Apply that changed nothing — a plan the validator rejected, a
        /// Player who approved nothing, or a run of Actions that asked for the values already in
        /// place — has nothing to put back, so it is not recorded and cannot push a real Undo out of
        /// the journal (ADR-0010).
        /// </summary>
        public void Record(AppliedPlan applied) {
            if (applied == null || !applied.AnyChanged) return;

            lock (_lock) {
                _applied.Add(applied);

                while (_applied.Count > Capacity) {
                    _applied.RemoveAt(0);
                }
            }
        }

        /// <summary>
        /// Takes the most recent Apply out of the journal, so it can only be undone once. Null when
        /// there is nothing left.
        /// </summary>
        public AppliedPlan Take() {
            lock (_lock) {
                if (_applied.Count == 0) return null;

                AppliedPlan last = _applied[_applied.Count - 1];

                _applied.RemoveAt(_applied.Count - 1);

                return last;
            }
        }
    }
}
