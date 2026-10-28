using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

        /// <summary>How closely admission watches the gate and the Chat while a request waits.</summary>
        private static readonly TimeSpan AdmissionSlice = TimeSpan.FromMilliseconds(10);

        /// <summary>Why a wait ended: its turn came, the gate closed, or the budget ran out.</summary>
        private enum WaitOutcome
        {
            Ready,
            GateClosed,
            BudgetOut
        }

        private readonly Chat             _chat;
        private readonly Func<bool>       _isOpen;
        private readonly Action<string>   _logDebug;
        private readonly IDevInbox        _inbox;
        private readonly TimeSpan         _busyTimeout;
        private readonly TimeSpan         _operationTimeout;
        private readonly Func<DateTime>   _utcNow;
        private readonly SemaphoreSlim    _gate = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Files older than this never run: they were already waiting the last time the gate
        /// reopened, or they predate this channel. Re-saving a file counts as new and runs.
        /// </summary>
        private DateTime _notBeforeUtc;

        /// <summary>Guards the gate observations below: the gate can be read from any thread.</summary>
        private readonly object _gateState = new object();

        /// <summary>What the last gate reading saw, so reopening can retire the backlog.</summary>
        private bool _gateWasOpen = true;

        /// <summary>
        /// How many gate closures have been seen. An operation captures this when admitted and is
        /// dropped when it changed before dispatch, even if the gate is open again.
        /// </summary>
        private long _gateClosures;

        /// <summary>The closures the last open poll had seen, so reopening retires the backlog.</summary>
        private long _lastPollClosures;

        /// <summary>
        /// Builds the channel. <paramref name="isOpen"/> says whether the Player turned the module
        /// setting on and Blish HUD started with <c>--debug</c>; <paramref name="busyTimeout"/> is how
        /// long a request that arrives while another one is running waits before it answers
        /// <see cref="DevErrors.Busy"/>, and <paramref name="operationTimeout"/> how long one request
        /// may run before it answers <see cref="DevErrors.Timeout"/>.
        /// </summary>
        public DevChannel(Chat chat, Func<bool> isOpen, Action<string> logDebug, IDevInbox inbox,
                          TimeSpan busyTimeout = default(TimeSpan),
                          TimeSpan operationTimeout = default(TimeSpan),
                          Func<DateTime> utcNow = null) {
            _chat             = chat   ?? throw new ArgumentNullException(nameof(chat));
            _isOpen           = isOpen ?? throw new ArgumentNullException(nameof(isOpen));
            _logDebug         = logDebug;
            _inbox            = inbox  ?? throw new ArgumentNullException(nameof(inbox));
            _busyTimeout      = busyTimeout      <= TimeSpan.Zero ? DefaultBusyTimeout      : busyTimeout;
            _operationTimeout = operationTimeout <= TimeSpan.Zero ? DefaultOperationTimeout : operationTimeout;
            _utcNow           = utcNow ?? (() => DateTime.UtcNow);
            _notBeforeUtc     = _utcNow();
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

            long admitted = GateClosures;

            if (!NoteGate()) {
                response = DevResponse.Failed(DevErrors.ChannelOff);
            } else {
                WaitOutcome turn = await WaitForTurnAsync(_busyTimeout, cancellationToken).ConfigureAwait(false);

                if (turn != WaitOutcome.Ready) {
                    response = turn == WaitOutcome.GateClosed
                                   ? DevResponse.Failed(DevErrors.ChannelOff)
                                   : DevResponse.Failed(DevErrors.Busy);
                } else {
                    try {
                        WaitOutcome chat = await WaitForChatAsync(_busyTimeout, cancellationToken).ConfigureAwait(false);

                        if (chat != WaitOutcome.Ready) {
                            response = chat == WaitOutcome.GateClosed
                                           ? DevResponse.Failed(DevErrors.ChannelOff)
                                           : DevResponse.Failed(DevErrors.Busy);
                        } else if (GateClosures != admitted || !NoteGate()) {
                            // The gate closed while this request waited its turn: it never runs,
                            // even if the gate is open again.
                            response = DevResponse.Failed(DevErrors.ChannelOff);
                        } else {
                            response = await ExecuteWithTimeoutAsync(request, cancellationToken).ConfigureAwait(false);
                        }
                    } catch (Exception exception) {
                        // The type, not the message: a message can carry a Setting's value back out.
                        response = DevResponse.Failed("failed: " + exception.GetType().Name);
                    } finally {
                        _gate.Release();
                    }
                }
            }

            _logDebug?.Invoke("dev request " + request.Id + ": " + DescribeOp(request) + ", "
                            + (response.Ok ? "ok" : response.Error) + ", " + watch.ElapsedMilliseconds + " ms");

            return response;
        }

        /// <summary>
        /// Reads the gate and notes the reading: every observed open-to-closed transition moves the
        /// closure generation, so an operation that waited through a closure is dropped even after
        /// the gate reopens. Answers whether the gate is open now.
        /// </summary>
        private bool NoteGate() {
            lock (_gateState) {
                bool open = _isOpen();

                if (_gateWasOpen && !open) _gateClosures++;

                _gateWasOpen = open;

                return open;
            }
        }

        private long GateClosures {
            get {
                lock (_gateState) {
                    return _gateClosures;
                }
            }
        }

        /// <summary>
        /// Waits for this request's turn — no other channel request running — up to
        /// <paramref name="budget"/>, watching the gate meanwhile. Answers why the wait ended.
        /// </summary>
        private async Task<WaitOutcome> WaitForTurnAsync(TimeSpan budget, CancellationToken cancellationToken) {
            Stopwatch watch = Stopwatch.StartNew();

            while (watch.Elapsed < budget) {
                if (await _gate.WaitAsync(AdmissionSlice, cancellationToken).ConfigureAwait(false)) {
                    return WaitOutcome.Ready;
                }

                if (!NoteGate()) return WaitOutcome.GateClosed;
            }

            return NoteGate() ? WaitOutcome.BudgetOut : WaitOutcome.GateClosed;
        }

        /// <summary>
        /// Waits until the Chat is free — no Request, Apply or Undo still running, whoever started
        /// it — up to <paramref name="budget"/>, watching the gate meanwhile, so a request that
        /// arrives while the Player is busy waits its turn. Answers why the wait ended.
        /// </summary>
        private async Task<WaitOutcome> WaitForChatAsync(TimeSpan budget, CancellationToken cancellationToken) {
            Stopwatch watch = Stopwatch.StartNew();

            while (true) {
                if (!NoteGate()) return WaitOutcome.GateClosed;
                if (!_chat.IsBusy) return WaitOutcome.Ready;
                if (watch.Elapsed >= budget) return WaitOutcome.BudgetOut;

                await Task.Delay(AdmissionSlice, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Handles the next waiting request file, if the gates are open and one is waiting: read it,
        /// run it, write its response and delete the file. Answers the handled request's id, or null
        /// when there was nothing to handle. Files too big or not shaped like a request are rejected
        /// without being run; files whose names cannot be a request id, and files already waiting the
        /// last time a gate was closed, are left alone and never run.
        /// </summary>
        public async Task<string> PollAsync(CancellationToken cancellationToken = default(CancellationToken)) {
            if (!NoteGate()) {
                _notBeforeUtc = _utcNow();

                return null;
            }

            if (GateClosures != _lastPollClosures) {
                // The gate closed and reopened since the last open poll: whatever is already waiting
                // arrived while it was closed, so none of it runs. Only files written from now on do.
                _notBeforeUtc     = _utcNow();
                _lastPollClosures = GateClosures;
            }

            foreach (string fileName in _inbox.Waiting) {
                if (!IsRequestFile(fileName, out string stem)) continue;

                try {
                    if (_inbox.GetChangedUtc(fileName) < _notBeforeUtc) continue;

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
            // What the Chat held before this operation: the response reports only what the operation
            // added — messages, Lookups and round trips alike — so an operation that makes no
            // Provider call reports none.
            int messagesBefore   = _chat.Messages.Count;
            int roundTripsBefore = _chat.RoundTrips.Count;
            int lookupsBefore    = _chat.Lookups.Count;
            AppliedPlan applied  = null;

            switch (request.Op) {
                case DevRequestOp.Send:
                    await _chat.SendAsync(request.Text, cancellationToken).ConfigureAwait(false);
                    break;

                case DevRequestOp.Apply:
                    await _chat.ApplyAsync(request.Actions, cancellationToken).ConfigureAwait(false);
                    applied = _chat.LastApply;
                    break;

                case DevRequestOp.CancelReview:
                    _chat.CancelReview();
                    break;

                case DevRequestOp.Undo:
                    await _chat.UndoAsync(cancellationToken).ConfigureAwait(false);
                    break;

                case DevRequestOp.NewChat:
                    _chat.StartNewChat();

                    // New chat replaces the conversation instead of adding to it: what is there now
                    // — the fresh Chat, or its failure Notice — is what the operation produced.
                    return Ok(_chat, _chat.Messages.ToList(), _chat.RoundTrips.ToList(), _chat.Lookups.ToList(),
                              applied);

                case DevRequestOp.State:
                    break;

                default:
                    return DevResponse.Failed(DevErrors.BadRequest);
            }

            return Ok(_chat, _chat.Messages.Skip(messagesBefore).ToList(),
                      _chat.RoundTrips.Skip(roundTripsBefore).ToList(), _chat.Lookups.Skip(lookupsBefore).ToList(),
                      applied);
        }

        private static DevResponse Ok(Chat chat, IReadOnlyList<ChatMessage> added,
                                      IReadOnlyList<DevRoundTrip> roundTrips, IReadOnlyList<DevLookup> lookups,
                                      AppliedPlan applied = null) {
            return new DevResponse(true, null, added, chat.Review, chat.CanUndo, roundTrips, lookups, applied);
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
