using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// The Blish HUD host, read where Blish HUD expects to be read: every Lookup's read is handed to
    /// the game thread through <see cref="IGameThread"/>, so a Chat that is thinking on a thread-pool
    /// thread never touches another Module's Settings from it.
    /// </summary>
    /// <remarks>
    /// This is where the two rules meet. The Chat keeps running off the game thread, and Blish HUD
    /// state is only read on it: the read is queued there, the caller awaits the result from
    /// wherever it is, and a cancellation ends the wait without the read ever running. The world
    /// underneath (<c>World.BlishHudWorld</c> in the module, a fake in tests) does the reading and
    /// knows nothing about threads.
    /// </remarks>
    public sealed class GameThreadWorld : IBlishHudWorld
    {
        private readonly IBlishHudWorld _world;
        private readonly IGameThread    _gameThread;

        public GameThreadWorld(IBlishHudWorld world, IGameThread gameThread) {
            _world      = world      ?? throw new ArgumentNullException(nameof(world));
            _gameThread = gameThread ?? throw new ArgumentNullException(nameof(gameThread));
        }

        public Task<IReadOnlyList<InstalledModuleInfo>> ListInstalledModulesAsync(CancellationToken cancellationToken) {
            return ReadAsync(() => _world.ListInstalledModulesAsync(cancellationToken), cancellationToken);
        }

        public Task<ModuleSettingsResult> GetModuleSettingsAsync(string @namespace, CancellationToken cancellationToken) {
            return ReadAsync(() => _world.GetModuleSettingsAsync(@namespace, cancellationToken), cancellationToken);
        }

        public Task<PlayerLocationResult> GetLocationAsync(CancellationToken cancellationToken) {
            return ReadAsync(() => _world.GetLocationAsync(cancellationToken), cancellationToken);
        }

        public Task<AccountAccessResult> GetAccountAccessAsync(CancellationToken cancellationToken) {
            return ReadAsync(() => _world.GetAccountAccessAsync(cancellationToken), cancellationToken);
        }

        public Task<ProgressResult> GetProgressAsync(ProgressKind kind, CancellationToken cancellationToken) {
            return ReadAsync(() => _world.GetProgressAsync(kind, cancellationToken), cancellationToken);
        }

        /// <summary>
        /// Hands one read to the game thread and carries its answer back. The read itself is the
        /// queued call: a world read answers from live state, so its task is already complete when
        /// the game thread hands it back and none of it runs on the caller's thread. A world read
        /// that awaited something would resume wherever that await completed, off the game thread,
        /// which is exactly what ADR-0009 is here to prevent.
        /// </summary>
        private async Task<T> ReadAsync<T>(Func<Task<T>> read, CancellationToken cancellationToken) {
            Task<T> queued = await _gameThread.RunAsync(read, cancellationToken).ConfigureAwait(false);

            return await queued.ConfigureAwait(false);
        }
    }
}
