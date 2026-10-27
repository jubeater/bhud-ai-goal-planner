using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
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
        private const int WindowHeight      = 680;
        private const int MessageListHeight = 470;
        private const int MessageSpacing    = 6;
        private const int InputRowTop       = 480;
        private const int ProviderRowTop    = 514;
        private const int RequestBoxWidth   = 300;

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
        /// the clip. Messages are wrapped in <see cref="WrapText"/> instead, stopping short of the edge.
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

        private readonly Chat         _chat;
        private readonly Action       _openProviderSettings;
        private readonly Func<string> _describeProvider;

        private readonly StandardWindow _window;
        private readonly Panel          _messageList;
        private readonly Scrollbar      _scrollbar;
        private readonly Label          _emptyHint;
        private readonly Label          _providerLabel;
        private readonly TextBox        _requestBox;
        private readonly StandardButton _sendButton;
        private readonly StandardButton _cancelButton;
        private readonly StandardButton _newChatButton;
        private readonly StandardButton _providerButton;
        private readonly List<Control>  _messageControls = new List<Control>();

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
                new Rectangle(40, 50, 540, 590)) {
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

            _emptyHint = new Label {
                Parent         = _messageList,
                Location       = new Point(0, 0),
                Width          = messageListWidth,
                Text           = WrapText("Nothing here yet. Ask for what you want to do in Guild Wars 2, then press Send.",
                                          GameService.Content.DefaultFont16,
                                          messageListWidth - TextRightMargin),
                Font           = GameService.Content.DefaultFont16,
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

            // A reminder of which Provider the Chat will talk to, so a wrong endpoint or model name
            // is visible before the Player gets an error back.
            _providerLabel = new Label {
                Parent    = content,
                Location  = new Point(156, ProviderRowTop + 6),
                Size      = new Point(384, 20),
                Font      = GameService.Content.DefaultFont14,
                TextColor = HintColor,
                Text      = string.Empty
            };

            _chat.MessagesChanged += OnMessagesChanged;
            _chat.ProgressChanged += OnProgressChanged;

            // The Player opens the Chat from the corner icon, so it starts out hidden.
            _window.Visible = false;
            RefreshProvider();
            RenderMessages();
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
        }

        /// <summary>
        /// Rewrites the line that says which Provider the Chat will use.
        /// </summary>
        public void RefreshProvider() {
            if (_disposed) return;

            _providerLabel.Text = _describeProvider();
        }

        public void Dispose() {
            _disposed = true;

            StopRequest();

            _chat.MessagesChanged -= OnMessagesChanged;
            _chat.ProgressChanged -= OnProgressChanged;

            _requestBox.EnterPressed -= OnRequestBoxEnterPressed;
            _sendButton.Click        -= OnSendClicked;
            _cancelButton.Click      -= OnCancelClicked;
            _newChatButton.Click     -= OnNewChatClicked;
            _providerButton.Click    -= OnProviderButtonClicked;

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

            _requestCancellation = new CancellationTokenSource();
            _requestInFlight     = true;
            QueueRender();

            SendRequestAsync(request, _requestCancellation.Token);
        }

        /// <summary>
        /// Asks the Chat for a reply. Failures end up in the Chat as a Notice, so the only thing
        /// left to do here is to keep the game from crashing on an unexpected error, and to hand
        /// the buttons back when the Request is over, cancelled or not.
        /// </summary>
        private async void SendRequestAsync(string request, CancellationToken cancellationToken) {
            try {
                await _chat.SendAsync(request, cancellationToken);
            } catch (OperationCanceledException) {
                // The Player cancelled this Request; the Chat has nothing more to add to it.
            } catch (Exception exception) {
                Logger.Warn(exception, "Sending the Request to the Provider failed.");
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

        private void QueueRender() {
            if (_disposed || _renderQueued) return;

            _renderQueued = true;

            GameService.Overlay.QueueMainThreadUpdate(OnMainThreadUpdate);
        }

        private void OnMainThreadUpdate(GameTime gameTime) {
            _renderQueued = false;

            if (_disposed) return;

            RenderMessages();
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

            foreach (ChatMessage message in messages) {
                top = AddLabel(Format(message), ColorFor(message.Author), top);
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
                // Lines are wrapped here, not by the Label: see TextRightMargin.
                Text           = WrapText(text, font, _messageList.Width - TextRightMargin),
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
        /// Breaks <paramref name="text"/> into lines no wider than <paramref name="width"/>, so a
        /// message can be drawn entire inside the message list.
        /// </summary>
        /// <remarks>
        /// Blish HUD's own wrapping - what <see cref="Label.WrapText"/> uses - measures every word on
        /// its own, and every one of those measurements comes out a little short (a glyph starts left
        /// of its own origin). A line packed against the width is then drawn wider than the label it
        /// sits in, and its last characters are clipped at the edge. Measuring the whole candidate
        /// line keeps the line inside the width it is drawn in.
        /// A single word wider than a whole line has nothing to break at, so it is left as it is.
        /// </remarks>
        private static string WrapText(string text, BitmapFont font, int width) {
            var wrapped = new StringBuilder();
            var line    = new StringBuilder();

            bool firstParagraph = true;

            foreach (string paragraph in text.Split('\n')) {
                if (!firstParagraph) wrapped.Append('\n');

                firstParagraph = false;

                foreach (string word in paragraph.Split(' ')) {
                    string candidate = line.Length == 0 ? word : line + " " + word;

                    if (line.Length > 0 && font.MeasureString(candidate).Width > width) {
                        wrapped.Append(line).Append('\n');

                        line.Clear().Append(word);

                        continue;
                    }

                    if (line.Length > 0) line.Append(' ');

                    line.Append(word);
                }

                wrapped.Append(line);

                line.Clear();
            }

            return wrapped.ToString();
        }

        private static string Format(ChatMessage message) {
            switch (message.Author) {
                case ChatMessageAuthor.Player:
                    return "Player: " + message.Text;
                case ChatMessageAuthor.Ai:
                    return "AI: " + message.Text;
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
                default:
                    return NoticeColor;
            }
        }
    }
}
