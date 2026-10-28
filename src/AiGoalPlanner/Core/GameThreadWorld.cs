using System;
using System.Collections.Generic;
using System.Linq;
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
    /// wherever it is, and a cancellation ends the wait without the read ever running. Lookups read
    /// game state (Mumble Link, settings) on the game thread first, then await GW2 API calls off it.
    /// HTTP and anything after an await must not touch Blish HUD state, and any values needed after
    /// an await are captured before it. The world underneath (<c>World.BlishHudWorld</c> in the module,
    /// a fake in tests) does the reading and knows nothing about threads.
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

        public Task<ModuleChange> SetModuleEnabledAsync(string @namespace, bool enabled, CancellationToken cancellationToken) {
            // A write is handed to the game thread exactly like a read is, and answers the same way.
            return ReadAsync(() => _world.SetModuleEnabledAsync(@namespace, enabled, cancellationToken), cancellationToken);
        }

        public Task<ModuleSettingChange> SetSettingAsync(string @namespace, string key, string value,
                                                         CancellationToken cancellationToken) {
            // A write is handed to the game thread exactly like a read is, and answers the same way.
            return ReadAsync(() => _world.SetSettingAsync(@namespace, key, value, cancellationToken), cancellationToken);
        }

        public Task<MarkerCategoriesResult> FindMarkerCategoriesAsync(string search, int? mapId, CancellationToken cancellationToken) {
            // Reading packs runs off the game thread, since it's file I/O and touches no Blish HUD state (ADR-0009).
            return ReadAsync(() => _world.FindMarkerCategoriesAsync(search, mapId, cancellationToken), cancellationToken);
        }

        public Task<MarkerPacksResult> ListMarkerPacksAsync(CancellationToken cancellationToken) {
            return ReadAsync(() => _world.ListMarkerPacksAsync(cancellationToken), cancellationToken);
        }

        public async Task<MarkerPackChange> DownloadMarkerPackAsync(string pack, CancellationToken cancellationToken) {
            MarkerPackChange change = await _world.DownloadMarkerPackAsync(pack, cancellationToken).ConfigureAwait(false);
            if (!change.Applied || !change.Changed) return change;

            string restartProblem = await RestartPathingIfEnabledAsync().ConfigureAwait(false);
            if (!string.IsNullOrEmpty(restartProblem)) {
                return new MarkerPackChange(change.Applied, change.Changed, change.PackName, change.FileName, change.SizeMb, restartProblem);
            }

            return change;
        }

        public async Task<MarkerPackChange> DeleteMarkerPackAsync(string fileName, CancellationToken cancellationToken) {
            MarkerPackChange change = await _world.DeleteMarkerPackAsync(fileName, cancellationToken).ConfigureAwait(false);
            if (!change.Applied || !change.Changed) return change;

            string restartProblem = await RestartPathingIfEnabledAsync().ConfigureAwait(false);
            if (!string.IsNullOrEmpty(restartProblem)) {
                return new MarkerPackChange(change.Applied, change.Changed, change.PackName, change.FileName, change.SizeMb, restartProblem);
            }

            return change;
        }

        private async Task<string> RestartPathingIfEnabledAsync() {
            IReadOnlyList<InstalledModuleInfo> modules;
            try {
                modules = await ListInstalledModulesAsync(CancellationToken.None).ConfigureAwait(false);
            } catch (Exception ex) {
                return "Failed to check Pathing module state (" + ex.GetType().Name + ").";
            }

            InstalledModuleInfo pathing = modules?.FirstOrDefault(m =>
                string.Equals(m.Namespace, "bh.community.pathing", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m.Namespace, "bh.pathing", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m.Name, "Pathing", StringComparison.OrdinalIgnoreCase));

            if (pathing == null || !pathing.Enabled || string.IsNullOrEmpty(pathing.Namespace)) {
                return null;
            }

            string problem = null;
            bool wasDisabled = false;
            try {
                ModuleChange disableChange = await SetModuleEnabledAsync(pathing.Namespace, false, CancellationToken.None).ConfigureAwait(false);
                if (!disableChange.Applied) {
                    problem = ProblemOr(disableChange.Problem, "Pathing could not be disabled to reload marker packs.");
                } else {
                    wasDisabled = true;
                }
            } catch (Exception ex) {
                problem = "Failed to disable Pathing (" + ex.GetType().Name + ").";
            } finally {
                if (wasDisabled) {
                    try {
                        ModuleChange enableChange = await SetModuleEnabledAsync(pathing.Namespace, true, CancellationToken.None).ConfigureAwait(false);
                        if (!enableChange.Applied) {
                            string enableProblem = ProblemOr(enableChange.Problem, "Pathing could not be re-enabled after installing the marker pack.");
                            problem = string.IsNullOrEmpty(problem) ? enableProblem : problem + " " + enableProblem;
                        }
                    } catch (Exception ex) {
                        string enableProblem = "Failed to re-enable Pathing (" + ex.GetType().Name + ").";
                        problem = string.IsNullOrEmpty(problem) ? enableProblem : problem + " " + enableProblem;
                    }
                }
            }

            return problem;
        }

        private static string ProblemOr(string problem, string fallback) {
            return string.IsNullOrEmpty(problem) ? fallback : problem;
        }

        /// <summary>
        /// Hands one read to the game thread and carries its answer back. The read itself is the
        /// queued call: a world read answers from live state, so its task is already complete when
        /// the game thread hands it back and none of it runs on the caller's thread. A world read
        /// that awaited something would resume wherever that await completed, off the game thread,
        /// which is exactly what ADR-0009 is here to prevent.
        /// </summary>
        public Task<IReadOnlyList<CatalogModuleInfo>> GetCatalogAsync(CancellationToken cancellationToken) {
            return ReadAsync(() => _world.GetCatalogAsync(cancellationToken), cancellationToken);
        }

        public async Task<ModuleInstallChange> InstallModuleAsync(string @namespace, CancellationToken cancellationToken) {
            ModuleInstallChange change = await ReadAsync(() => _world.InstallModuleAsync(@namespace, cancellationToken), cancellationToken).ConfigureAwait(false);
            if (!change.Applied || !change.Changed) return change;

            try {
                if (cancellationToken.IsCancellationRequested) {
                    return new ModuleInstallChange(true, true, change.ModuleName, change.Namespace, change.Version,
                        "Installation completed, but enabling was cancelled.");
                }

                ModuleChange enableChange = await SetModuleEnabledAsync(change.Namespace, true, cancellationToken).ConfigureAwait(false);
                if (!enableChange.Applied) {
                    return new ModuleInstallChange(true, true, change.ModuleName, change.Namespace, change.Version,
                        ProblemOr(enableChange.Problem, "Blish HUD could not enable " + change.ModuleName + "."));
                }

                return change;
            } catch (OperationCanceledException) {
                return new ModuleInstallChange(true, true, change.ModuleName, change.Namespace, change.Version,
                    "Installation completed, but enabling was cancelled.");
            } catch (Exception ex) {
                return new ModuleInstallChange(true, true, change.ModuleName, change.Namespace, change.Version,
                    ProblemOr(ex.Message, "Blish HUD could not enable " + change.ModuleName + "."));
            }
        }



        private async Task<T> ReadAsync<T>(Func<Task<T>> read, CancellationToken cancellationToken) {
            Task<T> queued = await _gameThread.RunAsync(read, cancellationToken).ConfigureAwait(false);

            return await queued.ConfigureAwait(false);
        }
    }
}

