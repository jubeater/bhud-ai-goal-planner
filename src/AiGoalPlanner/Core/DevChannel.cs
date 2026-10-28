using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace jubeater.aigoalplanner.Core
{
#if DEBUG
    /// <summary>
    /// The developer channel: the way a script runs the in-game checks the maintainer does by hand
    /// today. A request file dropped in the module's <c>dev\inbox</c> is handled one at a time, in
    /// file-name order, against the real Chat — the same methods the Chat window's buttons call —
    /// and the answer is written to <c>dev\outbox\&lt;id&gt;.json</c>. The module stays advisory:
    /// no input is sent to the game, and the checks cost the Player tokens like any other Request.
    /// </summary>
    /// <remarks>
    /// The channel runs behind three gates: this code only exists in Debug builds, Blish HUD must be
    /// started with <c>--debug</c>, and the module setting <c>Developer channel (for testing only)</c>
    /// must be on. With any gate missing nothing is read from the inbox. Only one request runs at a
    /// time; a request that arrives while the Chat is busy, including one the Player started, waits
    /// up to <see cref="DefaultBusyTimeout"/> and then fails with <see cref="DevErrors.Busy"/>.
    /// </remarks>
    public sealed class DevChannel
    {
        /// <summary>Request files over this size are rejected before they are run.</summary>
        public const int MaxRequestBytes = 16 * 1024;

        /// <summary>How often the watcher looks for a new request file.</summary>
        private const int PollMilliseconds = 250;

        /// <summary>How long a request that arrives while another one runs waits for its turn.</summary>
        private static readonly TimeSpan DefaultBusyTimeout = TimeSpan.FromSeconds(120);

        /// <summary>How long one request may run before the channel answers <see cref="DevErrors.Timeout"/>.</summary>
        private static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromSeconds(120);

        private readonly Chat             _chat;
        private readonly Func<bool>       _isOpen;
        private readonly Action<string>   _logDebug;
        private readonly IDevInbox        _inbox;
        private readonly TimeSpan         _busyTimeout;
        private readonly TimeSpan         _operationTimeout;
        private readonly SemaphoreSlim    _gate = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Builds the channel. <paramref name="isOpen"/> says whether the Player turned the module
        /// setting on and Blish HUD started with <c>--debug</c>; <paramref name="busyTimeout"/> is how
        /// long a request that arrives while another one is running waits before it answers
        /// <see cref="DevErrors.Busy"/>, and <paramref name="operationTimeout"/> how long one request
        /// may run before it answers <see cref="DevErrors.Timeout"/>.
        /// </summary>
        public DevChannel(Chat chat, Func<bool> isOpen, Action<string> logDebug, IDevInbox inbox,
                          TimeSpan busyTimeout = default(TimeSpan),
                          TimeSpan operationTimeout = default(TimeSpan)) {
            _chat             = chat   ?? throw new ArgumentNullException(nameof(chat));
            _isOpen           = isOpen ?? throw new ArgumentNullException(nameof(isOpen));
            _logDebug         = logDebug;
            _inbox            = inbox  ?? throw new ArgumentNullException(nameof(inbox));
            _busyTimeout      = busyTimeout      <= TimeSpan.Zero ? DefaultBusyTimeout      : busyTimeout;
            _operationTimeout = operationTimeout <= TimeSpan.Zero ? DefaultOperationTimeout : operationTimeout;
        }

        /// <summary>
        /// Runs one request against the Chat and answers what it produced: every operation calls the
        /// same Chat method the window's button calls (Send, Apply, the Review's Cancel, Undo, New
        /// chat), so the window shows the result as if the Player had clicked. <c>state</c> changes
        /// nothing. A request the gates reject never reaches the Chat.
        /// </summary>
        public async Task<DevResponse> HandleAsync(DevRequest request, CancellationToken cancellationToken = default) {
            Stopwatch watch = Stopwatch.StartNew();
            DevResponse response;

            if (!_isOpen()) {
                response = DevResponse.Failed(DevErrors.ChannelOff);
            } else if (await _gate.WaitAsync(_busyTimeout, cancellationToken).ConfigureAwait(false)) {
                try {
                    response = await ExecuteWithTimeoutAsync(request, cancellationToken).ConfigureAwait(false);
                } catch (Exception exception) {
                    // The type, not the message: a message can carry a Setting's value back out.
                    response = DevResponse.Failed("failed: " + exception.GetType().Name);
                } finally {
                    _gate.Release();
                }
            } else {
                response = DevResponse.Failed(DevErrors.Busy);
            }

            _logDebug?.Invoke("dev request " + request.Id + ": " + DescribeOp(request) + ", "
                            + (response.Ok ? "ok" : response.Error) + ", " + watch.ElapsedMilliseconds + " ms");

            return response;
        }

        /// <summary>
        /// Handles the next waiting request file, if the gates are open and one is waiting: read it,
        /// run it, write its response and delete the file. Answers the handled request's id, or null
        /// when there was nothing to handle. Files too big or not shaped like a request are rejected
        /// without being run; files whose names cannot be a request id are left alone.
        /// </summary>
        public async Task<string> PollAsync(CancellationToken cancellationToken = default(CancellationToken)) {
            if (!_isOpen()) return null;

            foreach (string fileName in _inbox.Waiting) {
                if (!IsRequestFile(fileName, out string stem)) continue;

                try {
                    string json = _inbox.Read(fileName);
                    string id;
                    DevResponse response;

                    if (Encoding.UTF8.GetByteCount(json) > MaxRequestBytes) {
                        id       = stem;
                        response = DevResponse.Failed(DevErrors.BadRequest);
                    } else if (!DevRequest.TryParse(json, out DevRequest request, out string error)) {
                        id       = stem;
                        response = DevResponse.Failed(DevErrors.BadRequest);
                    } else {
                        id       = request.Id;
                        response = await HandleAsync(request, cancellationToken).ConfigureAwait(false);
                    }

                    _inbox.Respond(id, response.ToJson());
                    _inbox.Remove(fileName);

                    return id;
                } catch (IOException) {
                    // The file went away, or the disk hiccuped, between listing and reading it: leave
                    // it for the next poll instead of stopping the channel over one file.
                    return null;
                }
            }

            return null;
        }

        /// <summary>
        /// Watches the inbox for request files and handles them one at a time, in file-name order. It
        /// stops when <paramref name="cancellationToken"/> is cancelled, which is also what stops the
        /// channel when the setting is turned off or the module unloads: files left in the inbox stay
        /// there, never run later.
        /// </summary>
        public async Task WatchAsync(CancellationToken cancellationToken = default(CancellationToken)) {
            while (!cancellationToken.IsCancellationRequested) {
                await PollAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(PollMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }

        private static bool IsRequestFile(string fileName, out string id) {
            id = null;

            if (fileName == null || !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;

            string stem = fileName.Substring(0, fileName.Length - ".json".Length);

            if (!DevRequest.IsValidId(stem)) return false;

            id = stem;
            return true;
        }

        /// <summary>
        /// Runs one operation against the Chat, or answers why it could not. A request the channel
        /// does not accept never reaches the Chat.
        /// </summary>
        private async Task<DevResponse> ExecuteWithTimeoutAsync(DevRequest request, CancellationToken cancellationToken) {
            using (CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
                Task<DevResponse> execution = ExecuteAsync(request, timeoutSource.Token);
                Task timeout                = Task.Delay(_operationTimeout, timeoutSource.Token);

                Task winner = await Task.WhenAny(execution, timeout).ConfigureAwait(false);

                if (winner == execution) return await execution.ConfigureAwait(false);

                timeoutSource.Cancel();

                try {
                    return await execution.ConfigureAwait(false);
                } catch (OperationCanceledException) {
                    return DevResponse.Failed(DevErrors.Timeout);
                }
            }
        }

        private async Task<DevResponse> ExecuteAsync(DevRequest request, CancellationToken cancellationToken) {
            switch (request.Op) {
                case DevRequestOp.Send:
                    await _chat.SendAsync(request.Text, cancellationToken).ConfigureAwait(false);
                    return Ok(_chat);

                case DevRequestOp.Apply:
                    await _chat.ApplyAsync(request.Actions, cancellationToken).ConfigureAwait(false);
                    return Ok(_chat, _chat.LastApply);

                case DevRequestOp.CancelReview:
                    _chat.CancelReview();
                    return Ok(_chat);

                case DevRequestOp.Undo:
                    await _chat.UndoAsync(cancellationToken).ConfigureAwait(false);
                    return Ok(_chat);

                case DevRequestOp.NewChat:
                    _chat.StartNewChat();
                    return Ok(_chat);

                case DevRequestOp.State:
                    return Ok(_chat);

                default:
                    return DevResponse.Failed(DevErrors.BadRequest);
            }
        }

        private static DevResponse Ok(Chat chat, AppliedPlan applied = null) {
            return new DevResponse(true, null, chat.Messages, chat.Review, chat.CanUndo,
                                   chat.RoundTrips, chat.Lookups, applied);
        }

        private static string DescribeOp(DevRequest request) {
            switch (request.Op) {
                case DevRequestOp.Send:         return "send";
                case DevRequestOp.Apply:        return "apply";
                case DevRequestOp.CancelReview: return "cancel_review";
                case DevRequestOp.Undo:         return "undo";
                case DevRequestOp.NewChat:      return "new_chat";
                default:                        return "state";
            }
        }
    }
#endif
}
