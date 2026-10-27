using System;
using System.Threading;
using System.Threading.Tasks;
using Blish_HUD;
using jubeater.aigoalplanner.Core;

namespace jubeater.aigoalplanner.World
{
    /// <summary>
    /// Blish HUD's own update loop, as <see cref="IGameThread"/>: the read is queued with
    /// <c>GameService.Overlay.QueueMainThreadUpdate</c> and the caller waits on a
    /// <see cref="TaskCompletionSource{TResult}"/> that the queued call completes.
    /// </summary>
    /// <remarks>
    /// The waiting happens off the game thread, so a Chat that is waiting for a read never blocks the
    /// update loop; the read itself runs inside one update, the way any other Blish HUD work does.
    /// Cancelling a Request while a read waits its turn ends the wait instead of leaving the Chat
    /// hanging until the next update. The queued call still runs afterwards and finds the wait
    /// already finished; it reads nothing more.
    /// </remarks>
    public sealed class OverlayGameThread : IGameThread
    {
        public Task<T> RunAsync<T>(Func<T> read, CancellationToken cancellationToken) {
            if (read == null) throw new ArgumentNullException(nameof(read));

            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<T>(cancellationToken);

            var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            CancellationTokenRegistration cancellation = default(CancellationTokenRegistration);

            if (cancellationToken.CanBeCanceled) {
                cancellation = cancellationToken.Register(() => result.TrySetCanceled(cancellationToken));
            }

            try {
                GameService.Overlay.QueueMainThreadUpdate(gameTime => Complete(result, read));
            } catch (Exception exception) {
                // Blish HUD is shutting down, or its overlay is not up yet: the read never runs, and
                // the Lookup is answered with that failure rather than left waiting for ever.
                result.TrySetException(exception);
            }

            return AwaitAsync(result.Task, cancellation);
        }

        /// <summary>
        /// Runs the read on the game thread and hands its result to <paramref name="result"/>. A read
        /// that fails fails the Lookup; it never takes Blish HUD's update loop down with it.
        /// </summary>
        private static void Complete<T>(TaskCompletionSource<T> result, Func<T> read) {
            // The Request was cancelled while this read waited its turn: nobody is listening, and
            // the state it would have read is not worth reading any more.
            if (result.Task.IsCompleted) return;

            try {
                result.TrySetResult(read());
            } catch (Exception exception) {
                result.TrySetException(exception);
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
