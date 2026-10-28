using System;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;

namespace jubeater.aigoalplanner
{
#if DEBUG
    /// <summary>
    /// Owns the developer channel's watcher for the running module: it watches the module's
    /// <c>dev\inbox</c> against the real Chat while the module is loaded, and stops watching when
    /// the setting is turned off or the module unloads. The watcher runs off the game thread — file
    /// IO never blocks it — and calls the same Chat methods the window's buttons call, which hand
    /// every Blish HUD read and write to the game thread themselves (ADR-0009).
    /// </summary>
    public sealed class DevChannelHost
    {
        private readonly object _door = new object();
        private CancellationTokenSource _watching;
        private bool _stopped = true;

        /// <summary>
        /// Starts watching <paramref name="inbox"/> against <paramref name="chat"/>.
        /// <paramref name="settingOn"/> is the module setting and <paramref name="blishDebug"/>
        /// whether Blish HUD was started with <c>--debug</c>; while either is missing nothing is
        /// read.
        /// </summary>
        public void Start(Chat chat, Func<bool> settingOn, Func<bool> blishDebug, Action<string> logDebug,
                          IDevInbox inbox) {
            Stop();

            lock (_door) {
                _stopped = false;

                var channel = new DevChannel(chat, () => !_stopped && settingOn() && blishDebug(), logDebug, inbox);

                CancellationTokenSource watching = new CancellationTokenSource();

                _watching = watching;
                _ = Task.Run(() => WatchUntilStopped(channel, logDebug, watching.Token));
            }
        }

        /// <summary>
        /// Stops watching. A request already running finishes on its own; files left in the inbox
        /// stay there, never run later.
        /// </summary>
        public void Stop() {
            lock (_door) {
                _stopped = true;

                CancellationTokenSource watching = _watching;

                _watching = null;

                if (watching != null) watching.Cancel();
            }
        }

        private static async Task WatchUntilStopped(DevChannel channel, Action<string> logDebug, CancellationToken cancellationToken) {
            try {
                await channel.WatchAsync(cancellationToken).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                // Stopped: the setting was turned off or the module unloaded.
            } catch (Exception exception) {
                // The type, not the message: a message can carry a Setting's value back out.
                logDebug?.Invoke("dev channel stopped: " + exception.GetType().Name);
            }
        }
    }
#endif
}
