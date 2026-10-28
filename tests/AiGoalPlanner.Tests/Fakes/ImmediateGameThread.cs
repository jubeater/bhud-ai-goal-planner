using System;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;

namespace jubeater.aigoalplanner.Tests.Fakes
{
    /// <summary>
    /// An <see cref="IGameThread"/> for tests that executes immediately without queuing,
    /// used when a test drives <see cref="GameThreadWorld"/> without needing to simulate
    /// tick delays on the Blish HUD update loop.
    /// </summary>
    internal sealed class ImmediateGameThread : IGameThread
    {
        public Task<T> RunAsync<T>(Func<T> read, CancellationToken cancellationToken) {
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<T>(cancellationToken);
            try {
                return Task.FromResult(read());
            } catch (Exception ex) {
                return Task.FromException<T>(ex);
            }
        }
    }
}
