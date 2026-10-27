using System;
using System.Threading;
using System.Threading.Tasks;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// The game's update thread, as the Lookups see it: the one thread Blish HUD and the Modules
    /// expect their state to be read on.
    /// </summary>
    /// <remarks>
    /// Reading a Module's Settings off the game thread is not merely impolite. It runs other
    /// Modules' compliance-requisite code and loads their lazy subcollections there, and Event Table
    /// logged <c>Max … resolution is invalid. X: 2 - Y: 2</c> when its Settings were read from a
    /// thread-pool thread. This is the port; <c>World.OverlayGameThread</c> is the adapter that
    /// queues the read on Blish HUD's own update loop, and tests drive the Lookups with a game
    /// thread they run themselves.
    /// </remarks>
    public interface IGameThread
    {
        /// <summary>
        /// Runs <paramref name="read"/> on the game thread and answers what it returned. The caller
        /// waits off the game thread, so the game keeps turning while a Lookup waits its turn, and
        /// <paramref name="cancellationToken"/> ends the wait without the read running at all.
        /// </summary>
        /// <remarks>
        /// The read is a read of live state and answers from it: it must not wait for anything, or
        /// the rest of it would happen somewhere other than the game thread. Everything the read
        /// touches happens inside the call, and the caller only carries the answer away.
        /// </remarks>
        Task<T> RunAsync<T>(Func<T> read, CancellationToken cancellationToken);
    }
}
