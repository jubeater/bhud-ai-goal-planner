using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using jubeater.aigoalplanner.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.BitmapFonts;

namespace jubeater.aigoalplanner.UI
{
    /// <summary>
    /// The Chat window: what the Player sees after clicking the corner icon.
    /// </summary>
    /// <remarks>
    /// The window only shows what the Chat holds; every Player action goes through the Chat.
    /// </remarks>
    internal sealed class ChatWindow : IDisposable
    {
        private static readonly Logger Logger = Logger.GetLogger<ChatWindow>();

        private const string WindowId = "jubeater.aigoalplanner.chat";

        private const int WindowWidth       = 560;
        private const int WindowHeight      = 780;

        /// <summary>
        /// What is left of the window inside its frame: 690 px of the 780, as the frame takes 50 above
        /// the content and 40 below it.
        /// </summary>
        private const int ContentHeight     = WindowHeight - 90;

        private const int MessageListHeight = 360;
        private const int MessageSpacing    = 6;

        /// <summary>
        /// Where the Review sits, between the conversation and the input row. The window grew for it:
        /// a Review needs a row per Action with what it would change, why, and what cannot be done.
        /// </summary>
        private const int ReviewTitleTop    = 368;
        private const int ReviewPanelTop    = 390;
        private const int ReviewPanelHeight = 140;
        private const int ReviewButtonsTop  = 536;
        private const int ReviewIndent      = 16;
        private const int SideEffectWidth   = 84;

        private const int InputRowTop       = 574;
        private const int ProviderRowTop    = 608;
        private const int RequestBoxWidth   = 300;
        private const int UndoButtonWidth   = 90;

        /// <summary>
        /// Blish HUD's <see cref="Scrollbar"/> is 12 px wide. The message list is worked out from the
        /// window's content region, so it has to set that much room aside, plus the gap that keeps the
        /// scrollbar off the list and off the window's right edge.
        /// </summary>
        private const int ScrollbarWidth         = 12;
        private const int ScrollbarGap           = 4;
        private const int MessageListRightMargin = 8;

        /// <summary>
        /// Room left between the end of a wrapped line and the edge of the message list. Blish HUD's
        /// own wrapping measures a line word by word, which comes out a few pixels short of the line
        /// that is then drawn, so a line wrapped right up to the edge loses its last characters under
        /// the clip. Messages are wrapped by <see cref="TextWrap.Wrap"/> instead, stopping short of
        /// the edge.
        /// </summary>
        private const int TextRightMargin = 4;

        private static readonly Color PlayerColor = new Color(235, 200, 130);
        private static readonly Color AiColor     = Color.White;

        /// <summary>
        /// Notices are what the Chat says itself, and they are usually bad news, so they are kept
        /// apart from what the Player and the AI said.
        /// </summary>
        private static readonly Color NoticeColor = new Color(240, 165, 150);
        private static readonly Color HintColor   = new Color(170, 170, 170);

        /// <summary>
        /// What the module says itself about what it did: an Apply, an Undo. It is not the AI talking,
        /// so it does not look like an AI reply.
        /// </summary>
        private static readonly Color ModuleColor = new Color(175, 215, 175);

        /// <summary>A Side Effect is a change the Player did not ask for, and it says so in red.</summary>
        private static readonly Color SideEffectColor = new Color(230, 150, 120);

        private readonly Chat         _chat;
        private readonly Action       _openProviderSettings;
        private readonly Func<string> _describeProvider;

        private readonly StandardWindow _window;
        private readonly Panel          _messageList;
        private readonly Scrollbar      _scrollbar;
        private readonly Label          _emptyHint;
        private readonly Label          _providerLabel;
        private readonly Label          _tokenLabel;
        private readonly TextBox        _requestBox;
        private readonly StandardButton _sendButton;
        private readonly StandardButton _cancelButton;
        private readonly StandardButton _newChatButton;
        private readonly StandardButton _providerButton;
        private readonly StandardButton _undoButton;
        private readonly List<Control>  _messageControls = new List<Control>();

        private readonly Label          _reviewTitle;
        private readonly Panel          _reviewPanel;
        private readonly Scrollbar      _reviewScrollbar;
        private readonly StandardButton _applyButton;
        private readonly StandardButton _cancelReviewButton;
        private readonly List<Control>  _reviewControls = new List<Control>();

        /// <summary>
        /// Which Action each checkbox in the Review stands for, so Apply sends the ones the Player
        /// left checked — and in the order the Plan has them.
        /// </summary>
        private readonly List<KeyValuePair<Checkbox, int>> _reviewRows = new List<KeyValuePair<Checkbox, int>>();

        /// <summary>
        /// The Actions of the Review on screen that the Player has unticked. The panel is rebuilt
        /// whenever the Chat changes, so what the Player decided has to outlive the controls that
        /// showed it — otherwise a repaint would tick everything again.
        /// </summary>
        private readonly HashSet<int> _reviewUnchosen = new HashSet<int>();

        /// <summary>The Review those ticks belong to, so a different Plan starts with everything ticked.</summary>
        private PlanReview _shownReview;

        /// <summary>
        /// The Request that is on its way right now, so Cancel can stop it. Null when the Player has
        /// nothing waiting on the Provider; set on the game thread, read wherever Cancel is clicked.
        /// </summary>
        private CancellationTokenSource _requestCancellation;

        /// <summary>
        /// Whether a Request is in flight, shown as Send giving way to Cancel. Touched from threads
        /// the Request ends on, so it is only applied to the controls in the render pass on the game
        /// thread.
        /// </summary>
        private volatile bool _requestInFlight;

        private bool _renderQueued;
        private bool _disposed;

        public ChatWindow(Chat          chat,
                          AsyncTexture2D windowBackground,
                          Texture2D     emblem,
                          Action        openProviderSettings,
                          Func<string>  describeProvider) {
            _chat                 = chat ?? throw new ArgumentNullException(nameof(chat));
            _openProviderSettings = openProviderSettings ?? throw new ArgumentNullException(nameof(openProviderSettings));
            _describeProvider     = describeProvider ?? throw new ArgumentNullException(nameof(describeProvider));

            _window = new StandardWindow(
                windowBackground,
                new Rectangle(25, 26, WindowWidth, WindowHeight),
                // The content region is the part inside the frame: the conversation, the Review, the
                // input row and the Provider row all have to fit in it, so it grows with the window.
                new Rectangle(40, 50, 540, ContentHeight)) {
                Parent        = GameService.Graphics.SpriteScreen,
                Title         = "AI Goal Planner",
                Subtitle      = "Describe a goal",
                Emblem        = emblem,
                Id            = WindowId,
                Location      = new Point(300, 80),
                SavesPosition = true,
                CanResize     = false
            };

            // A Blish HUD window already shifts its children by its ContentRegion, so the panel only
            // has to fill that region: its own top-left corner is where the content starts.
            var content = new Panel {
                Parent          = _window,
                Size            = _window.ContentRegion.Size,
                BackgroundColor = Color.Transparent
            };

            // The content region is what the Player can actually see, so the message list takes what
            // is left of it once the scrollbar and the space around it are set aside: text that goes
            // past that is text nobody reads.
            int messageListWidth = _window.ContentRegion.Width - ScrollbarWidth - ScrollbarGap - MessageListRightMargin;

            _messageList = new Panel {
                Parent          = content,
                Location        = new Point(0, 0),
                Size            = new Point(messageListWidth, MessageListHeight),
                BackgroundColor = Color.Transparent
            };

            _scrollbar = new Scrollbar(_messageList) {
                Parent   = content,
                Location = new Point(messageListWidth + ScrollbarGap, 0),
                Height   = MessageListHeight
            };

            BitmapFont font = GameService.Content.DefaultFont16;

            _emptyHint = new Label {
                Parent         = _messageList,
                Location       = new Point(0, 0),
                Width          = messageListWidth,
                Text           = TextWrap.Wrap("Nothing here yet. Ask for what you want to do in Guild Wars 2, then press Send.",
                                               s => font.MeasureString(s).Width,
                                               messageListWidth - TextRightMargin),
                Font           = font,
                TextColor      = HintColor,
                AutoSizeHeight = true
            };

            _requestBox = new TextBox {
                Parent          = content,
                Location        = new Point(0, InputRowTop),
                Size            = new Point(RequestBoxWidth, 27),
                PlaceholderText = "What do you want to do?"
            };
            _requestBox.EnterPressed += OnRequestBoxEnterPressed;

            _sendButton = new StandardButton {
                Parent   = content,
                Location = new Point(RequestBoxWidth + 6, InputRowTop),
                Size     = new Point(90, StandardButton.STANDARD_CONTROL_HEIGHT),
                Text     = "Send"
            };
            _sendButton.Click += OnSendClicked;

            // Cancel takes Send's place while a Request is running: there is nothing to send until
            // the AI has answered, but there is something to stop.
            _cancelButton = new StandardButton {
                Parent   = content,
                Location = new Point(RequestBoxWidth + 6, InputRowTop),
                Size     = new Point(90, StandardButton.STANDARD_CONTROL_HEIGHT),
                Text     = "Cancel",
                Visible  = false
            };
            _cancelButton.Click += OnCancelClicked;

            _newChatButton = new StandardButton {
                Parent   = content,
                Location = new Point(RequestBoxWidth + 102, InputRowTop),
                Size     = new Point(112, StandardButton.STANDARD_CONTROL_HEIGHT),
                Text     = "New chat"
            };
            _newChatButton.Click += OnNewChatClicked;

            _providerButton = new StandardButton {
                Parent   = content,
                Location = new Point(0, ProviderRowTop),
                Size     = new Point(150, StandardButton.STANDARD_CONTROL_HEIGHT),
                Text     = "Provider settings"
            };
            _providerButton.Click += OnProviderButtonClicked;

            // Undo is about the Player's configuration, not the conversation, so it sits beside the
            // settings rather than in the part of the window that a New chat empties. It is only
            // clickable while an Apply waiting to be undone exists.
            _undoButton = new StandardButton {
                Parent   = content,
                Location = new Point(156, ProviderRowTop),
                Size     = new Point(UndoButtonWidth, StandardButton.STANDARD_CONTROL_HEIGHT),
                Text     = "Undo"
            };
            _undoButton.Click += OnUndoClicked;

            // A reminder of which Provider the Chat will talk to, so a wrong endpoint or model name
            // is visible before the Player gets an error back.
            _providerLabel = new Label {
                Parent    = content,
                Location  = new Point(156 + UndoButtonWidth + 6, ProviderRowTop + 6),
                Size      = new Point(540 - (156 + UndoButtonWidth + 6), 20),
                Font      = GameService.Content.DefaultFont14,
                TextColor = HintColor,
                Text      = string.Empty
            };

            // What this Chat has used in total, on its own line below the Provider row: the
            // title-bar subtitle the totals sat in first turned out too narrow in game, running
            // under the close button and out of the window, so they moved here where the full
            // content width fits the counts, the cached split and the cost.
            _tokenLabel = new Label {
                Parent    = content,
                Location  = new Point(0, ProviderRowTop + StandardButton.STANDARD_CONTROL_HEIGHT + 6),
                Size      = new Point(_window.ContentRegion.Width - TextRightMargin, 20),
                Font      = GameService.Content.DefaultFont14,
                TextColor = HintColor,
                Text      = string.Empty
            };

            // The Review: what the AI proposed, one row per Action, and the two buttons that decide
            // what happens to it. It is its own area so a long Plan cannot push the conversation out
            // of the window.
            _reviewTitle = new Label {
                Parent    = content,
                Location  = new Point(0, ReviewTitleTop),
                Size      = new Point(messageListWidth, 20),
                Font      = GameService.Content.DefaultFont14,
                TextColor = HintColor,
                Text      = string.Empty,
                Visible   = false
            };

            _reviewPanel = new Panel {
                Parent          = content,
                Location        = new Point(0, ReviewPanelTop),
                Size            = new Point(messageListWidth, ReviewPanelHeight),
                BackgroundColor = Color.Transparent,
                Visible         = false
            };

            _reviewScrollbar = new Scrollbar(_reviewPanel) {
                Parent   = content,
                Location = new Point(messageListWidth + ScrollbarGap, ReviewPanelTop),
                Height   = ReviewPanelHeight,
                Visible  = false
            };

            _applyButton = new StandardButton {
                Parent   = content,
                Location = new Point(messageListWidth - 90, ReviewButtonsTop),
                Size     = new Point(90, StandardButton.STANDARD_CONTROL_HEIGHT),
                Text     = "Apply",
                Visible  = false
            };
            _applyButton.Click += OnApplyClicked;

            _cancelReviewButton = new StandardButton {
                Parent   = content,
                Location = new Point(messageListWidth - 90 - 6 - 100, ReviewButtonsTop),
                Size     = new Point(100, StandardButton.STANDARD_CONTROL_HEIGHT),
                Text     = "Cancel",
                Visible  = false
            };
            _cancelReviewButton.Click += OnCancelReviewClicked;

            _chat.MessagesChanged += OnMessagesChanged;
            _chat.ProgressChanged += OnProgressChanged;
            _chat.ReviewChanged   += OnReviewChanged;

            // The Player opens the Chat from the corner icon, so it starts out hidden.
            _window.Visible = false;
            RefreshProvider();
            RenderMessages();
            RenderReview();
        }

        /// <summary>
        /// Shows the Chat window if it is hidden, and hides it if it is showing.
        /// </summary>
        public void Toggle() {
            if (_window.Visible) {
                _window.Hide();
                return;
            }

            // Settings may have changed while the window was closed.
            RefreshProvider();
            _window.Show();
            _requestBox.Focused = true;
            RenderReview();
        }

        /// <summary>
        /// Rewrites the line that says which Provider the Chat will use.
        /// </summary>
        public void RefreshProvider() {
            if (_disposed) return;

            _providerLabel.Text = _describeProvider();
        }

        /// <summary>
        /// Rewrites the per-Chat token line, for a settings change that brought no new message to
        /// render: prices changed, so the per-Chat cost reads differently now. What a new message
        /// uses is rewritten in the render pass instead.
        /// </summary>
        public void RefreshTokens() {
            if (_disposed) return;

            _tokenLabel.Text = TokenLine();
        }

        public void Dispose() {
            _disposed = true;

            StopRequest();

            _chat.MessagesChanged -= OnMessagesChanged;
            _chat.ProgressChanged -= OnProgressChanged;
            _chat.ReviewChanged   -= OnReviewChanged;

            _requestBox.EnterPressed -= OnRequestBoxEnterPressed;
            _sendButton.Click        -= OnSendClicked;
            _cancelButton.Click      -= OnCancelClicked;
            _newChatButton.Click     -= OnNewChatClicked;
            _providerButton.Click    -= OnProviderButtonClicked;
            _applyButton.Click       -= OnApplyClicked;
            _cancelReviewButton.Click -= OnCancelReviewClicked;
            _undoButton.Click        -= OnUndoClicked;

            _window.Dispose();
        }

        private void OnRequestBoxEnterPressed(object sender, EventArgs e) {
            SendRequest();
        }

        private void OnSendClicked(object sender, MouseEventArgs e) {
            SendRequest();
        }

        private void OnNewChatClicked(object sender, MouseEventArgs e) {
            _requestBox.Text = string.Empty;

            // A Request that is still running belongs to the Chat the Player is leaving, so it is
            // stopped rather than left to finish into a Chat nobody reads.
            StopRequest();

            _chat.StartNewChat();
        }

        private void OnProviderButtonClicked(object sender, MouseEventArgs e) {
            _openProviderSettings();
        }

        private void OnCancelClicked(object sender, MouseEventArgs e) {
            StopRequest();
        }

        /// <summary>
        /// Cancels the Request that is on its way, if there still is one. Whoever swaps the source
        /// out of the field owns it: exactly one of the Player's Cancel click and the Request's own
        /// end gets it, so a disposed source is never cancelled.
        /// </summary>
        private void StopRequest() {
            CancellationTokenSource running = Interlocked.Exchange(ref _requestCancellation, null);

            if (running == null) return;

            running.Cancel();
            running.Dispose();
        }

        private void SendRequest() {
            string request = _requestBox.Text;

            if (string.IsNullOrWhiteSpace(request)) return;
            if (_requestInFlight) return;

            // The Player's Request is shown by the Chat, so the box is cleared straight away
            // instead of waiting for the Provider to answer.
            _requestBox.Text = string.Empty;

            RunWhileInFlight("Sending the Request to the Provider failed.",
                             cancellationToken => _chat.SendAsync(request, cancellationToken));
        }

        /// <summary>
        /// Applies the Actions the Player left checked. The Chat closes the Review, changes what it
        /// can, says what it did in the conversation and lets the AI carry on from there.
        /// </summary>
        private void OnApplyClicked(object sender, MouseEventArgs e) {
            IReadOnlyList<int> chosen = _reviewRows.Where(row => row.Key.Checked).Select(row => row.Value).ToList();

            RunWhileInFlight("Applying the plan failed.", cancellationToken => _chat.ApplyAsync(chosen, cancellationToken));
        }

        /// <summary>
        /// Throws the Review away without changing anything. The AI is told, so its next answer does
        /// not assume the changes it proposed are in place.
        /// </summary>
        private void OnCancelReviewClicked(object sender, MouseEventArgs e) {
            _chat.CancelReview();
        }

        /// <summary>
        /// Puts the last Apply back the way it was. Undo changes the Player's configuration, which is
        /// why it is offered outside the Review and outlives a New chat.
        /// </summary>
        private void OnUndoClicked(object sender, MouseEventArgs e) {
            RunWhileInFlight("Undoing the last change failed.", cancellationToken => _chat.UndoAsync(cancellationToken));
        }

        /// <summary>
        /// Runs one piece of Chat work with Cancel available and the buttons handed back whichever way
        /// it ends. What the Chat itself says about the outcome is already in the conversation, so the
        /// only thing left here is to keep the game from crashing on an unexpected error.
        /// </summary>
        private async void RunWhileInFlight(string failureMessage, Func<CancellationToken, Task> work) {
            // One piece of work at a time: the buttons that start one are disabled while one runs,
            // and this is what makes sure a click that arrives anyway cannot replace the cancellation
            // source of the work already running.
            if (_requestInFlight) return;

            _requestCancellation = new CancellationTokenSource();
            _requestInFlight     = true;
            QueueRender();

            CancellationToken cancellationToken = _requestCancellation.Token;

            try {
                await work(cancellationToken);
            } catch (OperationCanceledException) {
                // The Player cancelled this Request; the Chat has nothing more to add to it.
            } catch (Exception exception) {
                Logger.Warn(exception, failureMessage);
            } finally {
                // Taken out of the field here, so a Cancel click can never reach this source again.
                CancellationTokenSource finished = Interlocked.Exchange(ref _requestCancellation, null);
                _requestInFlight = false;
                QueueRender();

                finished?.Dispose();
            }
        }

        /// <summary>
        /// The Chat raises this on whatever thread the Provider answered on, and Blish HUD controls
        /// may only be touched from the game's update thread, so painting is queued there.
        /// </summary>
        private void OnMessagesChanged(object sender, EventArgs e) {
            QueueRender();
        }

        /// <summary>
        /// The Chat raises this on whatever thread a Lookup started or ended on; like the messages,
        /// the progress lines are painted on the game thread.
        /// </summary>
        private void OnProgressChanged(object sender, EventArgs e) {
            QueueRender();
        }

        /// <summary>
        /// The Chat opens or closes a Review on whatever thread the model or the Player reached it
        /// from, so the panel that shows it is painted on the game thread like everything else.
        /// </summary>
        private void OnReviewChanged(object sender, EventArgs e) {
            QueueRender();
        }

        private void QueueRender() {
            if (_disposed || _renderQueued) return;

            _renderQueued = true;

            GameService.Overlay.QueueMainThreadUpdate(OnMainThreadUpdate);
        }

        private void OnMainThreadUpdate(GameTime gameTime) {
            _renderQueued = false;

            if (_disposed) return;

            RenderMessages();
            RenderReview();
        }

        /// <summary>
        /// Rebuilds the message list from the Chat, shows what the AI is checking right now under
        /// it, and keeps the newest message in view.
        /// </summary>
        private void RenderMessages() {
            foreach (Control messageControl in _messageControls) {
                messageControl.Dispose();
            }

            _messageControls.Clear();

            IReadOnlyList<ChatMessage> messages = _chat.Messages;

            _emptyHint.Visible = messages.Count == 0;

            // Send and Cancel share a spot: while a Request is on its way there is nothing to send,
            // but there is something to stop. An unavailable Chat keeps the whole input row out of
            // the Player's way — the Core answers every Request with the failure Notice anyway.
            _requestBox.Enabled   = !_chat.IsUnavailable;
            _sendButton.Visible   = !_chat.IsUnavailable && !_requestInFlight;
            _cancelButton.Visible = !_chat.IsUnavailable && _requestInFlight;

            int top = 0;

            // The per-Chat totals sit on their own line under the Provider row, where the full
            // content width fits the counts, the cached split and the cost; what one reply used
            // sits under that reply instead.
            _tokenLabel.Text = TokenLine();

            foreach (ChatMessage message in messages) {
                top = AddLabel(Format(message), ColorFor(message.Author), top);

                if (message.Author == ChatMessageAuthor.Ai && message.Usage != null && !message.Usage.IsEmpty) {
                    top = AddLabel("    " + message.Usage.DescribeWithCost(message.EstimatedCostUsd), HintColor, top);
                }

                if (message.Author == ChatMessageAuthor.Report && message.RemovableModules != null) {
                    foreach (RemovableModuleInfo mod in message.RemovableModules) {
                        if (_chat.CanRemoveModule(mod.Namespace)) {
                            bool awaiting = _chat.IsAwaitingRemoveConfirmation(mod.Namespace);
                            string buttonText = awaiting ? "Confirm remove " + mod.Name : "Remove " + mod.Name;
                            BitmapFont font = GameService.Content.DefaultFont16;
                            int buttonWidth = Math.Min(Math.Max(160, (int)font.MeasureString(buttonText).Width + 30), _messageList.Width - TextRightMargin);
                            var removeButton = new StandardButton {
                                Parent   = _messageList,
                                Text     = buttonText,
                                Location = new Point(0, top),
                                Width    = buttonWidth,
                                Height   = StandardButton.STANDARD_CONTROL_HEIGHT,
                                Enabled  = !_requestInFlight
                            };
                            string modNs = mod.Namespace;
                            removeButton.Click += (s, e) => {
                                RunWhileInFlight("Removing the module failed.",
                                                 ct => _chat.RemoveModuleAsync(modNs, _chat.IsAwaitingRemoveConfirmation(modNs), ct));
                            };
                            _messageControls.Add(removeButton);
                            top = removeButton.Bottom + MessageSpacing;
                        }
                    }
                }
            }

            foreach (string line in _chat.ProgressLines) {
                top = AddLabel(line, HintColor, top);
            }

            // The scrollbar scrolls the panel, so it has to be told about the new content before it
            // is pushed to the bottom to show the reply that just arrived.
            _scrollbar.RecalculateLayout();
            _scrollbar.ScrollDistance = 1f;
        }

        /// <summary>
        /// Adds one line to the message list and says where the next one goes.
        /// </summary>
        private int AddLabel(string text, Color color, int top) {
            BitmapFont font = GameService.Content.DefaultFont16;

            var label = new Label {
                Parent         = _messageList,
                // Lines are wrapped by TextWrap, not by the Label: see TextRightMargin.
                Text           = TextWrap.Wrap(text, s => font.MeasureString(s).Width, _messageList.Width - TextRightMargin),
                Font           = font,
                TextColor      = color,
                Width          = _messageList.Width,
                AutoSizeHeight = true
            };

            label.RecalculateLayout();
            label.Location = new Point(0, top);

            _messageControls.Add(label);

            return label.Bottom + MessageSpacing;
        }

        /// <summary>
        /// The per-Chat token line under the Provider row: the tokens this Chat has used, with the
        /// estimated cost when the Player entered prices. An empty Chat keeps the line empty.
        /// </summary>
        private string TokenLine() {
            TokenUsage total = _chat.TotalUsage;

            if (total.IsEmpty) return string.Empty;

            return total.DescribeWithCost(_chat.TotalEstimatedCostUsd);
        }

        private static string Format(ChatMessage message) {
            switch (message.Author) {
                case ChatMessageAuthor.Player:
                    return "Player: " + message.Text;
                case ChatMessageAuthor.Ai:
                    return "AI: " + PlainText.Strip(message.Text);
                default:
                    return message.Text;
            }
        }

        private static Color ColorFor(ChatMessageAuthor author) {
            switch (author) {
                case ChatMessageAuthor.Player:
                    return PlayerColor;
                case ChatMessageAuthor.Ai:
                    return AiColor;
                case ChatMessageAuthor.Report:
                    return ModuleColor;
                default:
                    return NoticeColor;
            }
        }

        /// <summary>
        /// Rebuilds the Review from the Chat: one row per Action, with what it would change, why, and
        /// what cannot be done — a checkbox for each Action that can be applied, and nothing to tick
        /// on the ones that cannot.
        /// </summary>
        private void RenderReview() {
            foreach (Control reviewControl in _reviewControls) {
                reviewControl.Dispose();
            }

            _reviewControls.Clear();
            _reviewRows.Clear();

            PlanReview review = _chat.Review;
            bool       open   = review != null;

            // A different Plan starts with everything ticked; the same Plan keeps what the Player
            // already decided, however often the Chat repaints.
            if (!ReferenceEquals(review, _shownReview)) {
                _reviewUnchosen.Clear();
                _shownReview = review;
            }

            _reviewTitle.Visible     = open;
            _reviewPanel.Visible     = open;
            _reviewScrollbar.Visible = open;
            _cancelReviewButton.Visible   = open;
            _applyButton.Visible     = open && review.HasSomethingToApply;
            _applyButton.Enabled     = !_requestInFlight;
            _cancelReviewButton.Enabled   = !_requestInFlight;

            // Undo is offered for as long as there is an Apply to put back, whatever the Chat shows.
            _undoButton.Enabled = _chat.CanUndo && !_requestInFlight;

            if (!open) return;

            _reviewTitle.Text = "Review: " + Describe(review);

            int top = 0;

            for (int index = 0; index < review.Actions.Count; index++) {
                top = AddReviewRow(review.Actions[index], index, top);
            }

            _reviewScrollbar.RecalculateLayout();
            _reviewScrollbar.ScrollDistance = 0f;
        }

        private static string Describe(PlanReview review) {
            int applyable = review.Actions.Count(action => action.CanApply);

            if (applyable == 0) return "nothing here can be applied";

            return applyable == 1 ? "1 change to apply" : applyable + " changes to apply";
        }

        /// <summary>
        /// Adds one Action to the Review and says where the next one goes: a checkbox the Player can
        /// tick, what the Action would change, and why the AI proposes it. An Action that cannot be
        /// applied keeps its row and its box, greyed out, with the reason the validator gave — a Plan
        /// the AI proposes is what the Player reviews, including the parts that will not work.
        /// </summary>
        private int AddReviewRow(ReviewedAction action, int index, int top) {
            var box = new Checkbox {
                Parent           = _reviewPanel,
                Location         = new Point(0, top),
                Width            = 20,
                Text             = string.Empty,
                Enabled          = action.CanApply,
                Checked          = action.CanApply && !_reviewUnchosen.Contains(index),
                BasicTooltipText = action.Action.Reason
            };

            box.CheckedChanged += (sender, e) => Remember(index, e.Checked);

            _reviewControls.Add(box);
            _reviewRows.Add(new KeyValuePair<Checkbox, int>(box, index));

            if (action.Action.IsSideEffect) {
                AddReviewLabel("Side effect", SideEffectColor, top, _reviewPanel.Width - SideEffectWidth);
            }

            int labelRightMargin = action.Action.IsSideEffect ? SideEffectWidth : 0;
            Color summaryColor = action.CanApply ? Color.White : HintColor;
            int summaryBottom = AddReviewLabel(action.Summary, summaryColor, top, 24, labelRightMargin, () => {
                if (box.Enabled) box.Checked = !box.Checked;
            });

            int detail = Math.Max(top + box.Height, summaryBottom) + 2;

            if (!action.IsAccepted) {
                return AddReviewLabel(ProblemOf(action), NoticeColor, detail, ReviewIndent) + MessageSpacing;
            }

            // Advice's Summary is the reason itself, so there is nothing to add under it.
            if (action.Action.Kind == PlanActionKind.Advice) return detail + MessageSpacing;

            return AddReviewLabel(DetailOf(action), HintColor, detail, ReviewIndent) + MessageSpacing;
        }

        /// <summary>
        /// What the Player would get: the value now, the value the Action would give it, and why the
        /// AI proposes it.
        /// </summary>
        private static string DetailOf(ReviewedAction action) {
            if (action.Action.Kind == PlanActionKind.Install) {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(action.Gap)) {
                    parts.Add("Gap: " + action.Gap.Trim());
                }
                if (!string.IsNullOrWhiteSpace(action.Description)) {
                    parts.Add(action.Description.Trim());
                }
                if (action.Dependencies != null && action.Dependencies.Count > 0) {
                    parts.Add("Dependencies: " + string.Join(", ", action.Dependencies));
                }
                if (!action.PermissionsKnown) {
                    string detailsNote = !string.IsNullOrWhiteSpace(action.DetailsProblem)
                        ? "Could not load module details (" + action.DetailsProblem + ")"
                        : "Could not load module details";
                    parts.Add(detailsNote);
                    parts.Add("API permissions: unknown");
                } else if (action.ApiPermissions != null && action.ApiPermissions.Count > 0) {
                    parts.Add("API permissions: " + string.Join(", ", action.ApiPermissions));
                }
                if (!string.IsNullOrWhiteSpace(action.Action.Reason)) {
                    parts.Add(action.Action.Reason.Trim());
                }
                return string.Join(". ", parts);
            }

            return "Now " + action.CurrentValue + " → " + action.ProposedValue + ". " + action.Action.Reason;
        }

        private static string ProblemOf(ReviewedAction action) {
            return "Cannot be applied: " + action.Problem;
        }

        /// <summary>
        /// Remembers what the Player did with one checkbox, so the next repaint of the Review shows
        /// the same choice instead of ticking everything again.
        /// </summary>
        private void Remember(int index, bool chosen) {
            if (chosen) {
                _reviewUnchosen.Remove(index);
                return;
            }

            _reviewUnchosen.Add(index);
        }

        /// <summary>
        /// Adds one line to the Review and says where the next one goes. It starts at
        /// <paramref name="left"/> and wraps inside what is left of the panel, so a long reason cannot
        /// run out of it.
        /// </summary>
        private int AddReviewLabel(string text, Color color, int top, int left, int rightMargin = 0, Action onClick = null) {
            BitmapFont font = GameService.Content.DefaultFont14;

            int width = _reviewPanel.Width - left - TextRightMargin - rightMargin;

            var label = new Label {
                Parent         = _reviewPanel,
                Text           = TextWrap.Wrap(text, s => font.MeasureString(s).Width, width),
                Font           = font,
                TextColor      = color,
                Width          = width,
                AutoSizeHeight = true
            };

            label.RecalculateLayout();
            label.Location = new Point(left, top);

            if (onClick != null) {
                label.Click += (sender, e) => onClick();
            }

            _reviewControls.Add(label);

            return label.Bottom;
        }
    }
}
