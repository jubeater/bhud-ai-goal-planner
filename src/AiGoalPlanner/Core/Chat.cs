using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Newtonsoft.Json.Linq;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// One Chat: the conversation between the Player and the AI, from "New chat" until the next one.
    /// </summary>
    /// <remarks>
    /// This is the planning core's public seam. The Chat window and the Provider layer talk to a
    /// Chat only through this class, which keeps it free of Blish HUD types. The model is reached
    /// through <see cref="IChatClient"/>, so tests can drive the Chat with a scripted model, and the
    /// Lookups the model calls run against an <see cref="IBlishHudWorld"/> through the
    /// <see cref="ToolRegistry"/>. When a Request needs Lookups, the Chat runs them and returns all
    /// of a round's results to the model before it is asked again, until the model answers, stops
    /// calling tools, or the tool-call limit ends the loop.
    /// <para>
    /// Every Provider round trip is logged at debug level through the log the module hands in, so a
    /// slow or odd Request can be diagnosed from the Blish HUD log. What is logged is timing,
    /// finish reason and counts; never the Player's words, the model's reply, or anything else that
    /// could hold a key.
    /// </para>
    /// </remarks>
    public sealed class Chat
    {
        /// <summary>
        /// How many tool calls one Request may run before the Chat stops the model. The limit is
        /// checked per round: a round that would go past it ends the loop instead of running.
        /// </summary>
        private const int ToolCallLimit = 15;

        /// <summary>
        /// How many times one Request may ask the model again after it applied a Plan. A Continuation
        /// is one more turn of the same Request, and a model that answers every Apply with another
        /// Plan must not be able to keep the Player waiting for ever.
        /// </summary>
        private const int ContinuationLimit = 3;

        private readonly Func<IChatClient> _chatClient;
        private readonly Func<string>       _systemPrompt;
        private readonly ToolRegistry       _tools;

        /// <summary>
        /// The Plan pipeline: what makes a proposal into a Plan, checks it against the Player's real
        /// setup, applies it and can Undo it. Null in a Chat that was built without one, which can
        /// then only repeat what the model said about a plan.
        /// </summary>
        private readonly Plans _plans;

        /// <summary>
        /// Where the diagnostics of a Request go — one line per Provider round trip — or null when
        /// nobody is listening. The module hands in its debug log; tests hand in a list.
        /// </summary>
        private readonly Action<string> _logDebug;

        /// <summary>
        /// Where warnings from the Chat or Lookups go — such as missing API permissions or lookup
        /// execution warnings — or null when nobody is listening. The module hands in its warn log;
        /// tests hand in a list.
        /// </summary>
        private readonly Action<string> _logWarn;

        /// <summary>
        /// Why this Chat cannot be used, e.g. that the module failed to load, or null when it can.
        /// An unavailable Chat has no client at all: nothing can reach a Provider.
        /// </summary>
        private readonly string _unavailableReason;

        private readonly List<ChatMessage> _messages     = new List<ChatMessage>();
        private readonly object            _messagesLock = new object();
        private readonly SemaphoreSlim     _sendLock     = new SemaphoreSlim(1, 1);

        /// <summary>
        /// What the model is told, oldest first: the Player's and the AI's words, and the tool
        /// exchanges of the Requests that needed Lookups. It is not what the Player reads — Notices
        /// and progress never reach the model, and tool exchanges are not shown in the Chat.
        /// </summary>
        private readonly List<AiChatMessage> _modelHistory = new List<AiChatMessage>();

        /// <summary>
        /// What the Chat is doing right now, e.g. "Checking installed modules…", for the Player to
        /// read while the Lookups of the current Request run.
        /// </summary>
        private readonly List<string> _progressLines = new List<string>();

#if DEBUG
        /// <summary>
        /// The Provider round trips so far in this Chat, oldest first, each with how long it took
        /// and how it finished. The developer channel reads back only what each operation added;
        /// no other caller reads these.
        /// </summary>
        private readonly List<DevRoundTrip> _roundTrips = new List<DevRoundTrip>();

        /// <summary>
        /// The Lookups run so far in this Chat, oldest first, each with its name, how long it took
        /// and what it read. The developer channel reads back only what each operation added; no
        /// other caller reads these.
        /// </summary>
        private readonly List<DevLookup> _lookups = new List<DevLookup>();

        /// <summary>
        /// The Apply that ran last, and what it did — including what Auto-apply took on its own
        /// (ADR-0005 allows only Setting changes without Side Effects to be auto-applied). The
        /// developer channel reads it to answer an Apply with what the Apply did; the Player gets the
        /// same report as a message the Chat holds.
        /// </summary>
        private AppliedPlan _lastApply;
#endif

        /// <summary>
        /// Which Chat this is, so work started in one Chat can tell that the Player has moved on. It
        /// moves on under <c>_messagesLock</c>, together with emptying the Chat, which is what makes
        /// "still the same Chat?" and "add to it" one step. A read outside that lock only decides
        /// whether it is worth starting; whether a message lands is decided under the lock.
        /// </summary>
        private int _generation;

        /// <summary>
        /// The Plan the Player is being asked to Review, or null when no Review is open. It belongs
        /// to the Chat the Player is in, so a new Chat takes it away with everything else.
        /// </summary>
        private PlanReview _review;

        /// <summary>
        /// What Auto-apply already applied of the Plan that is waiting in the Review. The Review keeps
        /// the Player's remaining Actions, so the Continuation waits for them; when it comes, it
        /// carries this outcome as well as the Player's, and the model sees everything this Request
        /// did (ADR-0010).
        /// </summary>
        private AppliedPlan _appliedBeforeReview;

        /// <summary>
        /// A Chat talks to the model <paramref name="chatClient"/> builds and is told what it is by
        /// <paramref name="systemPrompt"/>. Both are asked again for every Request, so a change of
        /// Settings takes effect without losing the conversation. <paramref name="tools"/> holds the
        /// Lookups the model may call; without it the Chat simply has none to offer.
        /// <paramref name="logDebug"/> receives one line per Provider round trip; without it the
        /// Chat logs nothing. <paramref name="logWarn"/> receives warnings, such as notices from Lookups.
        /// <paramref name="plans"/> is what turns a proposal into a Plan the Player can Review and
        /// Apply; without it the Chat can only repeat what the model said about one.
        /// </summary>
        public Chat(Func<IChatClient> chatClient, Func<string> systemPrompt, ToolRegistry tools = null,
                    Action<string> logDebug = null, Action<string> logWarn = null, Plans plans = null)
            : this(chatClient, systemPrompt, tools, logDebug, logWarn, plans, unavailableReason: null) { }

        public Chat(Func<IChatClient> chatClient, Func<string> systemPrompt, ToolRegistry tools,
                    Action<string> logDebug, Plans plans)
            : this(chatClient, systemPrompt, tools, logDebug, logWarn: null, plans, unavailableReason: null) { }

        private Chat(Func<IChatClient> chatClient, Func<string> systemPrompt, ToolRegistry tools,
                     Action<string> logDebug, Action<string> logWarn, Plans plans, string unavailableReason) {
            _unavailableReason = unavailableReason;

            if (_unavailableReason == null) {
                _chatClient   = chatClient   ?? throw new ArgumentNullException(nameof(chatClient));
                _systemPrompt = systemPrompt ?? throw new ArgumentNullException(nameof(systemPrompt));
            } else {
                // The failure is visible the first time the window opens, the same Notice that
                // New chat restores.
                _messages.Add(new ChatMessage(ChatMessageAuthor.Notice, _unavailableReason));
            }

            _tools    = tools;
            _plans    = plans;
            _logDebug = logDebug;
            _logWarn  = logWarn;
        }

        /// <summary>
        /// Makes a Chat that cannot be used, e.g. because the module failed to load (ADR-0008). It
        /// has no client at all, so nothing is ever sent to a Provider. It starts with
        /// <paramref name="reason"/> as a Notice, so the Player sees it from the first time the
        /// window opens; every Request shows the Player's words followed by the reason again, and
        /// starting a new Chat shows it again, so it cannot be cleared away.
        /// </summary>
        public static Chat Unavailable(string reason) {
            return new Chat(null, null, null, null, null, null, reason ?? throw new ArgumentNullException(nameof(reason)));
        }

        /// <summary>
        /// Whether this Chat cannot be used, e.g. because the module failed to load. A Request to an
        /// unavailable Chat is answered with the reason as a Notice, and the Chat window keeps the
        /// input out of the Player's way.
        /// </summary>
        public bool IsUnavailable => _unavailableReason != null;

        /// <summary>
        /// Raised whenever what the Player sees in the Chat changes, so the Chat window can redraw.
        /// It may be raised on a background thread; the Chat window marshals it to the game thread.
        /// </summary>
        public event EventHandler<EventArgs> MessagesChanged;

        /// <summary>
        /// Raised whenever the progress lines change, so the Chat window can show what the AI is
        /// checking right now. Like <see cref="MessagesChanged"/>, it may be raised on a background
        /// thread.
        /// </summary>
        public event EventHandler<EventArgs> ProgressChanged;

        /// <summary>
        /// Everything said in this Chat so far, oldest first. The identity message the Chat puts
        /// ahead of the Player's words is not one of them: it is instruction, not conversation.
        /// </summary>
        public IReadOnlyList<ChatMessage> Messages {
            get {
                lock (_messagesLock) {
                    return _messages.ToArray();
                }
            }
        }

        /// <summary>
        /// What the current Request is doing, oldest line first, e.g. "Checking installed modules…".
        /// The lines are gone once the Request is done, whatever its end: a reply, a Notice or a
        /// cancellation.
        /// </summary>
        public IReadOnlyList<string> ProgressLines {
            get {
                lock (_messagesLock) {
                    return _progressLines.ToArray();
                }
            }
        }

        /// <summary>
        /// Raised whenever the Review opens or closes, so the Chat window can show the Actions the
        /// Player may approve. Like <see cref="MessagesChanged"/>, it may be raised on a background
        /// thread.
        /// </summary>
        public event EventHandler<EventArgs> ReviewChanged;

        /// <summary>
        /// The Plan waiting for the Player's Review, or null when there is none. Its Actions each
        /// say what would change, from what to what, why, and — when the validator refused one — why
        /// it cannot be applied.
        /// </summary>
        public PlanReview Review {
            get {
                lock (_messagesLock) {
                    return _review;
                }
            }
        }

        /// <summary>
        /// Whether an Apply is waiting to be undone. Undo is not part of the conversation, so a new
        /// Chat leaves it in place.
        /// </summary>
        public bool CanUndo => _plans != null && _plans.CanUndo;

#if DEBUG
        /// <summary>
        /// Whether the Chat is working on something right now — a Request still running, or an Apply
        /// or Undo still going — whoever started it. The developer channel waits on this before it
        /// dispatches, so a request that arrives while the Player is busy waits its turn instead of
        /// cutting in. Best-effort across threads: work starting between the check and the dispatch
        /// still serializes on the Chat's own locks.
        /// </summary>
        public bool IsBusy => _sendLock.CurrentCount == 0;
#endif

#if DEBUG
        /// <summary>
        /// The Provider round trips so far in this Chat, oldest first, each with how long it took
        /// and how it finished — the same what the debug log keeps, in the shape the developer
        /// channel reports. Empty when no Request has run through this Chat yet.
        /// </summary>
        public IReadOnlyList<DevRoundTrip> RoundTrips {
            get {
                lock (_messagesLock) {
                    return _roundTrips.ToArray();
                }
            }
        }

        /// <summary>
        /// The Lookups run so far in this Chat, oldest first, each with its name, how long it took
        /// and what it read — the same what the debug log keeps, in the shape the developer channel
        /// reports. Empty when no Request has run through this Chat yet.
        /// </summary>
        public IReadOnlyList<DevLookup> Lookups {
            get {
                lock (_messagesLock) {
                    return _lookups.ToArray();
                }
            }
        }

        /// <summary>
        /// The Apply that ran last, and what it did, or null when nothing has been applied in this
        /// Chat yet. The developer channel reads it to answer an Apply with what the Apply did; the
        /// Player gets the same report as a message the Chat holds.
        /// </summary>
        public AppliedPlan LastApply => _lastApply;
#endif

        /// <summary>
        /// Sends a Request in this Chat: the Player's words are added to the Chat and the AI answers.
        /// An empty Request (a Send with nothing typed) is ignored.
        /// </summary>
        /// <remarks>
        /// History is append-only: earlier messages are never edited or replaced. Only what the
        /// Player and the AI said, and the tool exchanges the model itself asked for, go back to the
        /// model, behind the identity message that says which product the model is part of; notices
        /// and progress stay in the Chat. Sends are serialized, and a Request that is still waiting
        /// its turn when the Player starts a new Chat is dropped instead of being sent into the new
        /// one. So is whatever else the old Chat still had coming: a reply or a Notice joins the
        /// Chat only while it is still the current one, so it can neither appear in the new Chat nor
        /// be sent to the model with it.
        /// </remarks>
        public async Task SendAsync(string request, CancellationToken cancellationToken = default) {
            if (string.IsNullOrWhiteSpace(request)) return;

            // An unavailable Chat has no client to reach, so the Request is shown and its whole
            // answer is the reason for the failure, whatever was asked.
            if (_unavailableReason != null) {
                int generation = Volatile.Read(ref _generation);

                await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);

                try {
                    if (TryBeginRequest(generation, request.Trim())) {
                        TryAppendNotice(generation, _unavailableReason);
                    }
                } finally {
                    _sendLock.Release();
                }

                return;
            }

            // Read before waiting for the lock: a Request belongs to the Chat the Player sent it
            // in, not to whatever Chat is current by the time the model is free to answer it.
            int currentGeneration = Volatile.Read(ref _generation);

            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try {
                // The Player started a new Chat while this Request was queued: it is dropped
                // before it is shown and before the model sees it.
                if (!TryBeginRequest(currentGeneration, request.Trim())) return;

                try {
                    await RunRequest(currentGeneration, cancellationToken).ConfigureAwait(false);
                } finally {
                    ClearProgress(currentGeneration);
                }
            } finally {
                _sendLock.Release();
            }
        }

        /// <summary>
        /// Shows a Notice from the module itself, e.g. that it failed to load. The Player reads it
        /// like any other Notice; the model never sees it.
        /// </summary>
        public void ShowNotice(string text) {
            TryAppendNotice(Volatile.Read(ref _generation), text);
        }

        /// <summary>
        /// Applies the Actions of the open Review that the Player approved, and then lets the AI carry
        /// on: its next turn works from the state the Apply made, which is the Continuation. The Chat
        /// reports what was applied and what was not, and one Action that fails does not stop the
        /// rest.
        /// </summary>
        /// <remarks>
        /// The Review closes before anything is written, so a second click cannot apply the same plan
        /// twice. A Chat the Player has left mid-Apply does not get the report, but the change itself
        /// is the Player's configuration and still happened; its Undo record is in the journal either
        /// way. An Apply the Player cancels part-way reports and records what it got through, and
        /// then ends the Request as cancelled: the Continuation belongs to an Apply that finished.
        /// </remarks>
        public async Task ApplyAsync(IEnumerable<int> actionIndexes, CancellationToken cancellationToken = default) {
            if (_plans == null) return;

            PlanReview review = Review;

            if (review == null) return;

            int generation = Volatile.Read(ref _generation);

            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try {
                if (generation != Volatile.Read(ref _generation)) return;

                SetReview(generation, null);

                AppliedPlan applied = await _plans.ApplyAsync(review, actionIndexes, cancellationToken).ConfigureAwait(false);

#if DEBUG
                _lastApply = applied;
#endif

                ShowReport(generation, applied.Describe());

                // Nothing was ticked and nothing was applied before the Review: there is nothing to
                // tell the model and nothing to carry on from. A Review with an Auto-apply behind it
                // is resolved all the same, so its outcome is still owed to the model.
                if (applied.Outcomes.Count == 0 && !HasAppliedBeforeReview) return;

                if (!TryAppendApplyOutcome(generation, applied)) return;

                // The Player stopped this Apply part-way. What was applied is reported and in the
                // journal, and the Request ends as any cancelled Request does.
                cancellationToken.ThrowIfCancellationRequested();

                try {
                    await RunRequest(generation, cancellationToken).ConfigureAwait(false);
                } finally {
                    ClearProgress(generation);
                }
            } finally {
                _sendLock.Release();
            }
        }

        /// <summary>
        /// The Player dismissed the open Review: nothing left in it is applied. The model is told, so
        /// its next answer does not assume the changes it proposed are in place — and when
        /// Auto-apply had already applied some of them, it is told that too, rather than being told
        /// the whole Plan was dropped.
        /// </summary>
        public void CancelReview() {
            int generation = Volatile.Read(ref _generation);

            lock (_messagesLock) {
                if (generation != Volatile.Read(ref _generation) || _review == null) return;

                AppliedPlan earlier = _appliedBeforeReview;

                _review              = null;
                _appliedBeforeReview = null;

                _modelHistory.Add(new AiChatMessage(ChatRole.User,
                                                    earlier == null
                                                        ? "The Player dismissed the plan. Nothing in it was applied."
                                                        : "The Player dismissed the rest of the plan. " + earlier.Describe()));
            }

            ReviewChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Puts the most recent Apply back the way it was. Undo is about the Player's configuration,
        /// not the conversation, so it works whatever the Chat is showing — including after New chat.
        /// </summary>
        /// <remarks>
        /// The model is deliberately not told. What an Undo restores is the Player's own state, which
        /// the next Lookup reads live; sending undo records to a Provider would hand it a history of
        /// the Player's configuration it has no use for.
        /// </remarks>
        public async Task UndoAsync(CancellationToken cancellationToken = default) {
            if (_plans == null) return;

            int generation = Volatile.Read(ref _generation);

            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try {
                UndoResult result = await _plans.UndoAsync(cancellationToken).ConfigureAwait(false);

                ShowReport(generation, result.Describe());
            } finally {
                _sendLock.Release();
            }
        }

        /// <summary>
        /// Starts a new Chat: what the Player said before is cleared, and the next Request starts from scratch.
        /// </summary>
        /// <remarks>
        /// Emptying the Chat and moving on to the next generation happen together, under the lock
        /// that guards the messages, so nothing that is happening in the old Chat at that moment can
        /// land in the new one: a reply, a Notice about a Request that failed, or a leftover
        /// progress line either gets there first and is cleared with the old Chat, or does not get
        /// there at all.
        /// </remarks>
        public void StartNewChat() {
            lock (_messagesLock) {
                _messages.Clear();
                _modelHistory.Clear();
                _progressLines.Clear();
                _review              = null;
                _appliedBeforeReview = null;
#if DEBUG
                _roundTrips.Clear();
                _lookups.Clear();
                _lastApply = null;
#endif
                Interlocked.Increment(ref _generation);
            }

            MessagesChanged?.Invoke(this, EventArgs.Empty);
            ProgressChanged?.Invoke(this, EventArgs.Empty);
            ReviewChanged?.Invoke(this, EventArgs.Empty);

            // Starting over is not a way to clear the failure away: an unavailable Chat shows its
            // reason again in the fresh Chat.
            if (_unavailableReason != null) {
                TryAppendNotice(Volatile.Read(ref _generation), _unavailableReason);
            }
        }

        /// <summary>
        /// One Request, from the model's first answer to the Chat's last word: tool rounds as long as
        /// the model calls read-only tools, then the reply — or a Plan, which ends the Request with
        /// the Review open, unless Auto-apply carried all of it out and the model is asked to carry
        /// on from there. Everything it shows or tells the model is gated on
        /// <paramref name="generation"/>, so nothing lands in a Chat the Player has left.
        /// </summary>
        private async Task RunRequest(int generation, CancellationToken cancellationToken) {
            int executedToolCalls = 0;
            int continuations     = 0;
            int round             = 0;

            while (true) {
                round++;

                ChatResponse response;
                Stopwatch    clock = Stopwatch.StartNew();

                try {
                    IChatClient chatClient = _chatClient();

                    response = await chatClient
                                      .GetResponseAsync(BuildHistory(), BuildOptions(), cancellationToken)
                                      .ConfigureAwait(false);
                } catch (ProviderException exception) {
                    LogRoundFailed(round, clock, exception.Failure.ToString());
                    TryAppendNotice(generation, Describe(exception.Failure));

                    return;
                } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    LogRoundFailed(round, clock, "cancelled");

                    throw;
                } catch (Exception exception) {
                    LogRoundFailed(round, clock, exception.GetType().Name);
                    TryAppendNotice(generation, Describe(ProviderFailure.Unexpected));

                    return;
                }

                if (response == null) {
                    LogRoundFailed(round, clock, "no response");
                    TryAppendNotice(generation, Describe(ProviderFailure.Unexpected));

                    return;
                }

                List<FunctionCallContent> calls = response.Messages.SelectMany(message => message.Contents)
                                                                   .OfType<FunctionCallContent>()
                                                                   .ToList();

                LogRound(round, clock, response, calls.Count);

                if (calls.Count == 0) {
                    ShowResponse(generation, response);

                    return;
                }

                // Only a reply that finished normally gets its calls run: a reply that was cut off
                // or refused stopped mid-thought, so its tool calls are the remains of an idea, not
                // a request to check something. They are not added to the history either — the
                // model never made them — and the same Notice a plain reply gets explains why
                // there is no answer.
                ChatFinishReason? finishReason = response.FinishReason;

                if (finishReason.HasValue
                 && (finishReason.Value.Equals(ChatFinishReason.Length) || finishReason.Value.Equals(ChatFinishReason.ContentFilter))) {
                    TryAppendNotice(generation, DescribeFinishReason(finishReason.Value, response.Text));

                    return;
                }

                if (_tools != null && calls.Any(call => _tools.IsProposal(call.Name))) {
                    // A proposal is delivered to the Player, not run: per ADR-0003 nothing changes
                    // until the Player applies it.
                    if (_plans == null) {
                        ShowUndeliverableProposal(generation, response);

                        return;
                    }

                    bool continuation = await DeliverPlanAsync(generation, response, calls, cancellationToken)
                                            .ConfigureAwait(false);

                    if (!continuation || ++continuations > ContinuationLimit) return;

                    continue;
                }

                if (executedToolCalls + calls.Count > ToolCallLimit) {
                    TryAppendNotice(generation,
                                    "The AI kept checking things without giving an answer: " + executedToolCalls + " Lookups in, the Chat stopped it. "
                                  + "Try a shorter request or start a New chat.");

                    return;
                }

                List<FunctionResultContent> results = await RunToolCalls(generation, calls, cancellationToken)
                                                          .ConfigureAwait(false);

                executedToolCalls += calls.Count;

                // The exchange is added to what the model is told as one step: a tool call without
                // its result is a hole in the conversation the next Request could not explain. If
                // the Player has started a new Chat meanwhile, none of it lands anywhere.
                if (!TryAppendToolExchange(generation, response, results)) return;
            }
        }

        /// <summary>
        /// Runs the tool calls of one round, in the order the model asked for them, and gathers
        /// their results. A call for a tool that does not exist, or one that fails, becomes a result
        /// that says so — the model gets an answer for every call it made, so it can carry on.
        /// </summary>
        private async Task<List<FunctionResultContent>> RunToolCalls(int generation, List<FunctionCallContent> calls,
                                                                     CancellationToken cancellationToken) {
            var results = new List<FunctionResultContent>(calls.Count);

            foreach (FunctionCallContent call in calls) {
                if (_tools == null || !_tools.TryGet(call.Name, out RegisteredTool tool)) {
                    results.Add(new FunctionResultContent(call.CallId, null) {
                        Exception = new InvalidOperationException("There is no tool named " + call.Name + ".")
                    });

                    continue;
                }

                AddProgress(generation, tool.ProgressText(call.Arguments));

                try {
#if DEBUG
                    LookupFunction lookup = tool.Function as LookupFunction;

                    if (lookup != null) {
                        lookup.Completed += OnLookupCompleted;
                    }

                    try {
#endif
                        object result = await tool.Function
                                                  .InvokeAsync(new AIFunctionArguments(call.Arguments), cancellationToken)
                                                  .ConfigureAwait(false);

                        if (result is string jsonString && TryExtractNotice(jsonString, out string notice)) {
                            TryAppendNotice(generation, notice);
                            _logWarn?.Invoke("Lookup " + call.Name + ": " + notice);
                        }

                        results.Add(new FunctionResultContent(call.CallId, result));
#if DEBUG
                    } finally {
                        if (lookup != null) lookup.Completed -= OnLookupCompleted;
                    }
#endif
                } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    throw;
                } catch (Exception exception) {
                    _logWarn?.Invoke("Lookup " + call.Name + " failed: " + exception.GetType().Name);
                    results.Add(new FunctionResultContent(call.CallId, null) {
                        Exception = exception
                    });
                }
            }

            return results;
        }

#if DEBUG
        /// <summary>
        /// One Lookup of the Request that is running just finished, however it ended: with what it
        /// read, or with why it did not. The Chat collects what the developer channel reports about
        /// the Request, and nothing else.
        /// </summary>
        private void OnLookupCompleted(object sender, LookupOutcome outcome) {
            lock (_messagesLock) {
                _lookups.Add(new DevLookup(outcome.Name, outcome.DurationMs, outcome.Detail, outcome.Chars));
            }
        }
#endif

        private static bool TryExtractNotice(string json, out string notice) {
            notice = null;
            if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{")) return false;

            try {
                var obj = JObject.Parse(json);
                if (obj.TryGetValue("notice", out JToken token) && token != null) {
                    notice = token.ToString();
                    return !string.IsNullOrWhiteSpace(notice);
                }
            } catch {
                // Not a valid JSON object or syntax error.
            }

            return false;
        }

        /// <summary>
        /// What the model is offered besides the conversation: the Lookups it may call. Without a
        /// registry there is nothing to offer, and the Request goes out as it always did.
        /// </summary>
        private ChatOptions BuildOptions() {
            if (_tools == null) return null;

            List<AIFunctionDeclaration> declarations = _tools.Declarations.ToList();

            return declarations.Count == 0
                       ? null
                       : new ChatOptions { Tools = declarations.Cast<AITool>().ToList() };
        }

        /// <summary>
        /// What the model is sent: the identity message first, then everything the Player and the AI
        /// said and the tool exchanges of this Chat. The identity message is not one of the Chat's
        /// messages, so the Player never reads it and New chat leaves it in place.
        /// </summary>
        private IReadOnlyList<AiChatMessage> BuildHistory() {
            var history = new List<AiChatMessage> {
                new AiChatMessage(ChatRole.System, _systemPrompt())
            };

            lock (_messagesLock) {
                history.AddRange(_modelHistory);
            }

            return history;
        }

        /// <summary>
        /// Adds the Player's words to the Chat and to what the model will be told, only while
        /// <paramref name="generation"/> is still the Chat the Player is looking at.
        /// </summary>
        private bool TryBeginRequest(int generation, string request) {
            lock (_messagesLock) {
                if (generation != Volatile.Read(ref _generation)) return false;

                _messages.Add(new ChatMessage(ChatMessageAuthor.Player, request));
                _modelHistory.Add(new AiChatMessage(ChatRole.User, request));
            }

            MessagesChanged?.Invoke(this, EventArgs.Empty);

            return true;
        }

        private void ShowResponse(int generation, ChatResponse response) {
            ChatFinishReason? finishReason = response.FinishReason;
            string            text         = response.Text;

            if (finishReason.HasValue && !string.IsNullOrEmpty(finishReason.Value.Value) && !finishReason.Value.Equals(ChatFinishReason.Stop)) {
                TryAppendNotice(generation, DescribeFinishReason(finishReason.Value, text));
                return;
            }

            if (string.IsNullOrWhiteSpace(text)) {
                TryAppendNotice(generation, "The Provider returned an empty reply. Try again.");
                return;
            }

            TryAppendAiReply(generation, text);
        }

        /// <summary>
        /// The model called a proposal tool and this Chat has no Plan pipeline to give it to. That is
        /// a wiring mistake rather than something a Player can reach, but the Player is still not
        /// left believing something happened: nothing has.
        /// </summary>
        private void ShowUndeliverableProposal(int generation, ChatResponse response) {
            string text = response.Text;

            if (!string.IsNullOrWhiteSpace(text)) {
                TryAppendAiReply(generation, text);
                return;
            }

            TryAppendNotice(generation, "The model proposed changes but did not say which. This version of AI Goal Planner cannot show proposals yet.");
        }

        /// <summary>
        /// Takes the Plan the model proposed and puts it in front of the Player: its reply as the AI's
        /// answer, and its Actions as a Review. Answers whether the model should be asked again — it
        /// should when Auto-apply already carried part of the Plan out, so the model can carry on
        /// from the state it made.
        /// </summary>
        private async Task<bool> DeliverPlanAsync(int generation, ChatResponse response, List<FunctionCallContent> calls,
                                                  CancellationToken cancellationToken) {
            FunctionCallContent call = calls.First(candidate => _tools.IsProposal(candidate.Name));
            Plan                plan = SubmitPlanTool.ReadPlan(call.Arguments);

            bool hasReply = !string.IsNullOrWhiteSpace(plan.Reply);

            // A call with neither words nor Actions is nothing the Player can be shown or asked to
            // approve; the Chat says so rather than opening an empty Review.
            if (plan.Actions.Count == 0 && !hasReply) {
                TryAppendNotice(generation, "The AI proposed changes but did not say which.");

                return false;
            }

            if (hasReply && !TryAppendAiReply(generation, plan.Reply)) return false;

            if (plan.Actions.Count == 0) {
                TryAppendPlanExchange(generation, response, calls, call.CallId, PlanResult(0, 0, 0));

                return false;
            }

            PlanReview review;

            try {
                review = await _plans.ReviewAsync(plan, cancellationToken).ConfigureAwait(false);
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            } catch (Exception exception) {
                // The Player's installation could not be read — a Module that fails while its
                // Settings are read is the usual cause. Nothing is proposed against state that
                // could not be checked: the Review already open, and what Auto-apply did of the Plan
                // behind it, both stay where they are.
                if (_logDebug != null) _logDebug("Plan could not be checked: " + exception.GetType().Name);

                TryAppendNotice(generation, "The plan could not be checked against your installation. Try again.");

                return false;
            }

            // Only a Plan that can be checked replaces the open Review, so this is where the outcome
            // of an Auto-apply that came with that Review is resolved: the model is told what was
            // applied and that the rest of the old Plan was set aside, and it is not told again
            // afterwards (ADR-0010).
            if (!TryAppendPlanSetAside(generation)) return false;

            IReadOnlyList<int> autoApplied = _plans.AutoApplies ? AutoApplyable(review) : new int[0];

            AppliedPlan applied = autoApplied.Count == 0
                                      ? null
                                      : await _plans.ApplyAsync(review, autoApplied, cancellationToken).ConfigureAwait(false);

#if DEBUG
            if (applied != null) _lastApply = applied;
#endif

            if (!TryAppendPlanExchange(generation, response, calls, call.CallId,
                                       PlanResult(review.Actions.Count,
                                                  review.Actions.Count(action => !action.IsAccepted),
                                                  autoApplied.Count))) {
                return false;
            }

            PlanReview remaining = applied == null ? review : review.Without(autoApplied);

            SetReview(generation, remaining.IsEmpty ? null : remaining);

            if (applied == null) return false;

            ShowReport(generation, applied.Describe());

            // The Player still has Actions to decide on, so the Continuation waits: the model's next
            // Plan would replace the Review it has not answered yet. What Auto-apply did is
            // remembered, and goes to the model with the rest once the Player decides (ADR-0010).
            if (!remaining.IsEmpty) {
                SetAppliedBeforeReview(generation, applied);

                return false;
            }

            return TryAppendApplyOutcome(generation, applied);
        }

        /// <summary>
        /// Which Actions Auto-apply may take without the Player: Setting changes the validator
        /// accepted and that are not Side Effects. Enabling and disabling Modules, Side Effects and
        /// rejected Actions always wait for Review (ADR-0005).
        /// </summary>
        private static IReadOnlyList<int> AutoApplyable(PlanReview review) {
            var indexes = new List<int>();

            for (int index = 0; index < review.Actions.Count; index++) {
                ReviewedAction action = review.Actions[index];

                if (action.CanApply && action.Action.Kind == PlanActionKind.SetSetting && !action.Action.IsSideEffect) {
                    indexes.Add(index);
                }
            }

            return indexes;
        }

        /// <summary>
        /// What the model is told its <c>submit_plan</c> call came to: how many Actions the Player was
        /// shown, how many the validator refused, and how many Auto-apply took. Nothing about values
        /// or undo records: the model reads the state it needs with a Lookup.
        /// </summary>
        private static string PlanResult(int actions, int rejected, int autoApplied) {
            return "{\"status\":\"delivered\",\"actions\":" + actions
                 + ",\"cannot_be_applied\":" + rejected
                 + ",\"auto_applied\":" + autoApplied + "}";
        }

        /// <summary>
        /// Shows the Player what an Apply or an Undo did, in the module's own voice: the AI did not do
        /// it, so it is not the AI that says it was done.
        /// </summary>
        private void ShowReport(int generation, string text) {
            TryAppend(generation, new ChatMessage(ChatMessageAuthor.Report, text));
        }

        /// <summary>
        /// Settles what Auto-apply applied of a Review that a new Plan is replacing: the model is told
        /// now, as part of the turn that brings the new Plan, so the outcome is passed on exactly once
        /// and never turns up in a later Continuation (ADR-0010). Nothing to settle is not an error.
        /// </summary>
        private bool TryAppendPlanSetAside(int generation) {
            lock (_messagesLock) {
                if (generation != Volatile.Read(ref _generation)) return false;

                AppliedPlan earlier = _appliedBeforeReview;

                if (earlier == null) return true;

                _appliedBeforeReview = null;

                _modelHistory.Add(new AiChatMessage(ChatRole.User,
                                                    earlier.Describe()
                                                  + " The Player's new request set aside the rest of that plan."));
            }

            return true;
        }

        /// <summary>
        /// Whether an Auto-apply outcome is still waiting for the Review it came with to be resolved.
        /// </summary>
        private bool HasAppliedBeforeReview {
            get {
                lock (_messagesLock) {
                    return _appliedBeforeReview != null;
                }
            }
        }

        /// <summary>
        /// Remembers what Auto-apply applied of the Plan the Review still holds, so the Continuation
        /// can carry it when the Player has finished with the rest.
        /// </summary>
        private void SetAppliedBeforeReview(int generation, AppliedPlan applied) {
            lock (_messagesLock) {
                if (generation != Volatile.Read(ref _generation)) return;

                _appliedBeforeReview = applied;
            }
        }

        /// <summary>
        /// Opens or closes the Review, only while <paramref name="generation"/> is still current.
        /// </summary>
        private void SetReview(int generation, PlanReview review) {
            lock (_messagesLock) {
                if (generation != Volatile.Read(ref _generation)) return;

                _review = review;
            }

            ReviewChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Adds the model's reply to the Chat and to what the model will be told, only while
        /// <paramref name="generation"/> is still current, as one step.
        /// </summary>
        private bool TryAppendAiReply(int generation, string text) {
            lock (_messagesLock) {
                if (generation != Volatile.Read(ref _generation)) return false;

                _messages.Add(new ChatMessage(ChatMessageAuthor.Ai, text));
                _modelHistory.Add(new AiChatMessage(ChatRole.Assistant, text));
            }

            MessagesChanged?.Invoke(this, EventArgs.Empty);

            return true;
        }

        /// <summary>
        /// Adds one tool round to what the model will be told: its own calls first, then the results
        /// it asked for, as one step under the lock.
        /// </summary>
        private bool TryAppendToolExchange(int generation, ChatResponse response, List<FunctionResultContent> results) {
            lock (_messagesLock) {
                if (generation != Volatile.Read(ref _generation)) return false;

                _modelHistory.Add(AssistantMessageOf(response));
                _modelHistory.Add(new AiChatMessage(ChatRole.Tool, results.Cast<AIContent>().ToList()));
            }

            return true;
        }

        /// <summary>
        /// Adds a proposal round to what the model will be told: its own calls, and a result for every
        /// one of them. The proposal's call gets <paramref name="result"/>; a Lookup asked for in the
        /// same turn was not run and its result says so, because a tool call without an answer is a
        /// hole the next Request could not explain.
        /// </summary>
        private bool TryAppendPlanExchange(int generation, ChatResponse response, List<FunctionCallContent> calls,
                                           string planCallId, string result) {
            var results = new List<FunctionResultContent>(calls.Count);

            foreach (FunctionCallContent call in calls) {
                results.Add(string.Equals(call.CallId, planCallId, StringComparison.Ordinal)
                                ? new FunctionResultContent(call.CallId, result)
                                : new FunctionResultContent(call.CallId,
                                      "{\"status\":\"not run\",\"reason\":\"The plan was delivered in this turn, so this call was not run.\"}"));
            }

            return TryAppendToolExchange(generation, response, results);
        }

        /// <summary>
        /// Tells the model what came of the Review the Player has just finished with, as one message
        /// of its own, so its next turn works from the state the Apply made. When Auto-apply had
        /// already applied part of this Plan while the Player decided the rest, that outcome travels
        /// with this one — it is the Review's, and this is where it is resolved. It holds what was
        /// applied and what failed and nothing else: no undo record, no earlier Chat, and no value
        /// the Player did not see in the Review.
        /// </summary>
        private bool TryAppendApplyOutcome(int generation, AppliedPlan applied) {
            lock (_messagesLock) {
                if (generation != Volatile.Read(ref _generation)) return false;

                AppliedPlan earlier = _appliedBeforeReview;

                _appliedBeforeReview = null;

                string text = "The Player applied the plan you proposed. ";

                if (earlier != null) text += earlier.Describe() + " ";

                text += applied.Outcomes.Count == 0
                            ? "The Player chose none of the other actions. "
                            : applied.Describe() + " ";

                _modelHistory.Add(new AiChatMessage(ChatRole.User,
                                                    text + "Look up anything you now need to, and tell the Player what you suggest next."));
            }

            return true;
        }

        /// <summary>
        /// The message to replay for what the model said this round: its tool calls, and any words
        /// it said along with them.
        /// </summary>
        private static AiChatMessage AssistantMessageOf(ChatResponse response) {
            if (response.Messages.Count == 1) return response.Messages[0];

            var contents = new List<AIContent>();

            foreach (AiChatMessage message in response.Messages) {
                contents.AddRange(message.Contents);
            }

            return new AiChatMessage(ChatRole.Assistant, contents);
        }

        private bool TryAppendNotice(int generation, string text) {
            return TryAppend(generation, new ChatMessage(ChatMessageAuthor.Notice, text));
        }

        /// <summary>
        /// One line for a Provider round trip that came back: how long it took, how it finished and
        /// how many Lookups the model asked for. Not what it said — a reply can hold anything the
        /// Player's key was used to buy, and the log is not the place for it.
        /// </summary>
        private void LogRound(int round, Stopwatch clock, ChatResponse response, int toolCalls) {
#if DEBUG
            lock (_messagesLock) {
                _roundTrips.Add(new DevRoundTrip(clock.ElapsedMilliseconds, FinishReasonText(response.FinishReason), toolCalls));
            }
#endif

            if (_logDebug == null) return;

            _logDebug("Provider round trip " + round + ": " + clock.ElapsedMilliseconds + " ms, finish_reason="
                    + FinishReasonText(response.FinishReason) + ", tool_calls=" + toolCalls);
        }

        /// <summary>
        /// The finish reason as the log may say it: one of the reasons the Chat knows by name, and
        /// "other" for anything else, "none" when the reply carried none.
        /// </summary>
        /// <remarks>
        /// The value arrives from the Provider as free text — a finish reason is whatever the
        /// endpoint wrote in the reply — so an endpoint that is not really a Provider can put an API
        /// key there, and the log must not repeat it (ADR-0006).
        /// </remarks>
        private static string FinishReasonText(ChatFinishReason? finishReason) {
            if (!finishReason.HasValue) return "none";

            ChatFinishReason reason = finishReason.Value;

            if (reason.Equals(ChatFinishReason.Stop))          return "stop";
            if (reason.Equals(ChatFinishReason.Length))        return "length";
            if (reason.Equals(ChatFinishReason.ToolCalls))     return "tool_calls";
            if (reason.Equals(ChatFinishReason.ContentFilter)) return "content_filter";

            return "other";
        }

        /// <summary>
        /// One line for a Provider round trip that did not come back — it failed, or the Player
        /// cancelled it — named by its failure rather than by its message: a Provider's message can
        /// echo the request, and with it the key.
        /// </summary>
        private void LogRoundFailed(int round, Stopwatch clock, string failure) {
            if (_logDebug == null) return;

            _logDebug("Provider round trip " + round + " failed after " + clock.ElapsedMilliseconds + " ms: " + failure);
        }

        private static string Describe(ProviderFailure failure) {
            switch (failure) {
                case ProviderFailure.NotConfigured:
                    return "Connect a Provider first: click Provider settings, then enter the endpoint URL and model name, and an API key if your Provider needs one.";
                case ProviderFailure.InvalidEndpoint:
                    return "The endpoint URL is not valid. Check it in Provider settings.";
                case ProviderFailure.InvalidKey:
                    return "The Provider rejected the API key. Check it in Provider settings and try again.";
                case ProviderFailure.UnknownModel:
                    return "The Provider does not have that model. Check the model name in Provider settings.";
                case ProviderFailure.RateLimited:
                    return "The Provider is rate limiting requests. Wait a moment, then try again.";
                case ProviderFailure.Network:
                    return "Could not reach the Provider. Check the endpoint URL and your internet connection, then try again.";
                case ProviderFailure.ToolCallingNotSupported:
                    return "That model cannot call tools, which AI Goal Planner needs. Choose a different model in Provider settings.";
                default:
                    return "Something went wrong while asking the Provider. Try again.";
            }
        }

        /// <summary>
        /// Adds <paramref name="message"/> to the Chat only while <paramref name="generation"/> is
        /// still the Chat the Player is looking at, and says whether it landed.
        /// </summary>
        /// <remarks>
        /// Asking whether it is still the same Chat and adding to it happen under one lock, the same
        /// lock New chat empties the Chat under. That is what keeps a reply from slipping into a Chat
        /// it does not belong to: it either lands first and goes with the old Chat, or it does not
        /// land at all. The Chat window is told outside the lock, so it can read the Chat back.
        /// </remarks>
        private bool TryAppend(int generation, ChatMessage message) {
            lock (_messagesLock) {
                if (generation != Volatile.Read(ref _generation)) return false;

                _messages.Add(message);
            }

            MessagesChanged?.Invoke(this, EventArgs.Empty);

            return true;
        }

        /// <summary>
        /// Shows a line in the Chat for what the AI is checking now, only while
        /// <paramref name="generation"/> is still current.
        /// </summary>
        private void AddProgress(int generation, string line) {
            lock (_messagesLock) {
                if (generation != Volatile.Read(ref _generation)) return;

                _progressLines.Add(line);
            }

            ProgressChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Ends the progress of a Request, whatever way it ended. A Request that belongs to a Chat
        /// the Player has left leaves no lines behind in the new one.
        /// </summary>
        private void ClearProgress(int generation) {
            lock (_messagesLock) {
                if (generation != Volatile.Read(ref _generation)) return;
                if (_progressLines.Count == 0) return;

                _progressLines.Clear();
            }

            ProgressChanged?.Invoke(this, EventArgs.Empty);
        }

        private static string DescribeFinishReason(ChatFinishReason finishReason, string text) {
            if (finishReason.Equals(ChatFinishReason.ContentFilter)) {
                return string.IsNullOrWhiteSpace(text)
                           ? "The model refused to answer this request."
                           : "The model refused to answer this request. It said: " + text;
            }

            if (finishReason.Equals(ChatFinishReason.Length)) {
                return "The reply was cut off because the model reached its length limit. Try a shorter request.";
            }

            if (finishReason.Equals(ChatFinishReason.ToolCalls)) {
                return "The model stopped mid-thought to check something, but the check never arrived. Try again.";
            }

            // The reason is whatever text the endpoint sent, so it is not repeated here either: the
            // log says "other", and the Player gets words of this module's own.
            return "The model stopped before finishing. Try again.";
        }
    }
}
