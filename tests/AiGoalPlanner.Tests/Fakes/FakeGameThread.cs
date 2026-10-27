using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;

namespace jubeater.aigoalplanner.Tests.Fakes
{
    /// <summary>
    /// The game thread for tests: reads wait in a queue the test runs itself, so a test can look at
    /// a Lookup while it is still waiting its turn — and cancel it there.
    /// </summary>
    internal sealed class FakeGameThread : IGameThread
    {
        private readonly List<Action> _queued = new List<Action>();
        private readonly object       _lock   = new object();

        /// <summary>How many reads are waiting for the game thread right now.</summary>
        public int WaitingCount {
            get {
                lock (_lock) {
                    return _queued.Count;
                }
            }
        }

        public Task<T> RunAsync<T>(Func<T> read, CancellationToken cancellationToken) {
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<T>(cancellationToken);

            var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            CancellationTokenRegistration cancellation = default(CancellationTokenRegistration);

            if (cancellationToken.CanBeCanceled) {
                cancellation = cancellationToken.Register(() => result.TrySetCanceled(cancellationToken));
            }

            Action run = () => {
                if (result.Task.IsCompleted) return;

                try {
                    result.TrySetResult(read());
                } catch (Exception exception) {
                    result.TrySetException(exception);
                }
            };

            lock (_lock) {
                _queued.Add(run);
            }

            return AwaitAsync(result.Task, cancellation);
        }

        /// <summary>Runs every read waiting for the game thread, the way one turn of the game does.</summary>
        public void Pump() {
            Action[] waiting;

            lock (_lock) {
                waiting = _queued.ToArray();
                _queued.Clear();
            }

            foreach (Action run in waiting) {
                run();
            }
        }

        private static async Task<T> AwaitAsync<T>(Task<T> task, CancellationTokenRegistration cancellation) {
            try {
                return await task.ConfigureAwait(false);
            } finally {
                cancellation.Dispose();
            }
        }
    }
}
