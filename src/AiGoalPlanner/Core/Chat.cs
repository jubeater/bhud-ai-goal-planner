using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
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

        private readonly Func<IChatClient> _chatClient;
        private readonly Func<string>       _systemPrompt;
        private readonly ToolRegistry       _tools;

        /// <summary>
        /// Where the diagnostics of a Request go — one line per Provider round trip — or null when
        /// nobody is listening. The module hands in its debug log; tests hand in a list.
        /// </summary>
        private readonly Action<string> _logDebug;

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

        /// <summary>
        /// Which Chat this is, so work started in one Chat can tell that the Player has moved on. It
        /// moves on under <c>_messagesLock</c>, together with emptying the Chat, which is what makes
        /// "still the same Chat?" and "add to it" one step. A read outside that lock only decides
        /// whether it is worth starting; whether a message lands is decided under the lock.
        /// </summary>
        private int _generation;

        /// <summary>
        /// A Chat talks to the model <paramref name="chatClient"/> builds and is told what it is by
        /// <paramref name="systemPrompt"/>. Both are asked again for every Request, so a change of
        /// Settings takes effect without losing the conversation. <paramref name="tools"/> holds the
        /// Lookups the model may call; without it the Chat simply has none to offer.
        /// <paramref name="logDebug"/> receives one line per Provider round trip; without it the
        /// Chat logs nothing.
        /// </summary>
        public Chat(Func<IChatClient> chatClient, Func<string> systemPrompt, ToolRegistry tools = null,
                    Action<string> logDebug = null)
            : this(chatClient, systemPrompt, tools, logDebug, unavailableReason: null) { }

        private Chat(Func<IChatClient> chatClient, Func<string> systemPrompt, ToolRegistry tools,
                     Action<string> logDebug, string unavailableReason) {
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
            _logDebug = logDebug;
        }

        /// <summary>
        /// Makes a Chat that cannot be used, e.g. because the module failed to load (ADR-0008). It
        /// has no client at all, so nothing is ever sent to a Provider. It starts with
        /// <paramref name="reason"/> as a Notice, so the Player sees it from the first time the
        /// window opens; every Request shows the Player's words followed by the reason again, and
        /// starting a new Chat shows it again, so it cannot be cleared away.
        /// </summary>
        public static Chat Unavailable(string reason) {
            return new Chat(null, null, null, null, reason ?? throw new ArgumentNullException(nameof(reason)));
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
                Interlocked.Increment(ref _generation);
            }

            MessagesChanged?.Invoke(this, EventArgs.Empty);
            ProgressChanged?.Invoke(this, EventArgs.Empty);

            // Starting over is not a way to clear the failure away: an unavailable Chat shows its
            // reason again in the fresh Chat.
            if (_unavailableReason != null) {
                TryAppendNotice(Volatile.Read(ref _generation), _unavailableReason);
            }
        }

        /// <summary>
        /// One Request, from the model's first answer to the Chat's last word: tool rounds as long as
        /// the model calls read-only tools, then the reply. Everything it shows or tells the model is
        /// gated on <paramref name="generation"/>, so nothing lands in a Chat the Player has left.
        /// </summary>
        private async Task RunRequest(int generation, CancellationToken cancellationToken) {
            int executedToolCalls = 0;
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
                    // until the Player applies it. The Review flow that takes a proposal from here
                    // arrives with its own ticket; until then the Chat can only repeat what the
                    // model said about it.
                    ShowUndeliverableProposal(generation, response);

                    return;
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
                    object result = await tool.Function
                                              .InvokeAsync(new AIFunctionArguments(call.Arguments), cancellationToken)
                                              .ConfigureAwait(false);

                    results.Add(new FunctionResultContent(call.CallId, result));
                } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    throw;
                } catch (Exception exception) {
                    results.Add(new FunctionResultContent(call.CallId, null) {
                        Exception = exception
                    });
                }
            }

            return results;
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
        /// The model called a proposal tool. Until the Review flow can take a proposal, all the Chat
        /// can do is repeat what the model said about it, so the Player is never left with the
        /// impression that something has been done — it has not.
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
