using System;
using System.Collections.Generic;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using jubeater.aigoalplanner.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

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

        private const int MessageListWidth  = 500;
        private const int MessageListHeight = 540;
        private const int MessageSpacing    = 6;
        private const int InputRowTop       = 550;
        private const int RequestBoxWidth   = 300;

        private static readonly Color PlayerColor = new Color(235, 200, 130);
        private static readonly Color AiColor     = Color.White;
        private static readonly Color HintColor   = new Color(170, 170, 170);

        private readonly Chat           _chat;
        private readonly StandardWindow _window;
        private readonly Panel          _messageList;
        private readonly Scrollbar      _scrollbar;
        private readonly Label          _emptyHint;
        private readonly TextBox        _requestBox;
        private readonly StandardButton _sendButton;
        private readonly StandardButton _newChatButton;
        private readonly List<Control>  _messageControls = new List<Control>();

        public ChatWindow(Chat chat, AsyncTexture2D windowBackground, Texture2D emblem) {
            _chat = chat ?? throw new ArgumentNullException(nameof(chat));

            _window = new StandardWindow(
                windowBackground,
                new Rectangle(25, 26, 560, 640),
                new Rectangle(40, 50, 540, 590)) {
                Parent        = GameService.Graphics.SpriteScreen,
                Title         = "AI Goal Planner",
                Subtitle      = "Describe what you want to set up",
                Emblem        = emblem,
                Id            = WindowId,
                Location      = new Point(300, 80),
                SavesPosition = true,
                CanResize     = false
            };

            _messageList = new Panel {
                Parent          = _window,
                Location        = new Point(0, 0),
                Size            = new Point(MessageListWidth, MessageListHeight),
                BackgroundColor = Color.Transparent
            };

            _scrollbar = new Scrollbar(_messageList) {
                Parent   = _window,
                Location = new Point(MessageListWidth + 4, 0),
                Height   = MessageListHeight
            };

            _emptyHint = new Label {
                Parent         = _messageList,
                Location       = new Point(0, 0),
                Width          = MessageListWidth,
                Text           = "Nothing here yet. Ask for what you want to do in Guild Wars 2, then press Send.",
                Font           = GameService.Content.DefaultFont16,
                TextColor      = HintColor,
                WrapText       = true,
                AutoSizeHeight = true
            };

            _requestBox = new TextBox {
                Parent          = _window,
                Location        = new Point(0, InputRowTop),
                Size            = new Point(RequestBoxWidth, 27),
                PlaceholderText = "What do you want to do?"
            };
            _requestBox.EnterPressed += OnRequestBoxEnterPressed;

            _sendButton = new StandardButton {
                Parent   = _window,
                Location = new Point(RequestBoxWidth + 6, InputRowTop),
                Size     = new Point(90, StandardButton.STANDARD_CONTROL_HEIGHT),
                Text     = "Send"
            };
            _sendButton.Click += OnSendClicked;

            _newChatButton = new StandardButton {
                Parent   = _window,
                Location = new Point(RequestBoxWidth + 102, InputRowTop),
                Size     = new Point(112, StandardButton.STANDARD_CONTROL_HEIGHT),
                Text     = "New chat"
            };
            _newChatButton.Click += OnNewChatClicked;

            _chat.MessagesChanged += OnMessagesChanged;

            // The Player opens the Chat from the corner icon, so it starts out hidden.
            _window.Visible = false;
            RenderMessages();
        }

        /// <summary>
        /// Shows the Chat window if it is hidden, and hides it if it is showing.
        /// </summary>
        public void Toggle() {
            if (_window.Visible) {
                _window.Hide();
            } else {
                _window.Show();
            }
        }

        public void Dispose() {
            _chat.MessagesChanged -= OnMessagesChanged;

            _requestBox.EnterPressed -= OnRequestBoxEnterPressed;
            _sendButton.Click        -= OnSendClicked;
            _newChatButton.Click     -= OnNewChatClicked;

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
            _chat.StartNewChat();
        }

        private void SendRequest() {
            string request = _requestBox.Text;

            if (string.IsNullOrWhiteSpace(request)) return;

            _requestBox.Text = string.Empty;

            SendRequestAsync(request);
        }

        private async void SendRequestAsync(string request) {
            try {
                await _chat.SendAsync(request);
            } catch (Exception exception) {
                Logger.Warn(exception, "Sending the Request failed.");
            }
        }

        private void OnMessagesChanged(object sender, EventArgs e) {
            RenderMessages();
        }

        /// <summary>
        /// Rebuilds the message list from the Chat and keeps the newest message in view.
        /// </summary>
        private void RenderMessages() {
            foreach (Control messageControl in _messageControls) {
                messageControl.Dispose();
            }

            _messageControls.Clear();

            IReadOnlyList<ChatMessage> messages = _chat.Messages;

            _emptyHint.Visible = messages.Count == 0;

            int top = 0;

            foreach (ChatMessage message in messages) {
                var label = new Label {
                    Parent         = _messageList,
                    Text           = GetAuthorName(message.Author) + ": " + message.Text,
                    Font           = GameService.Content.DefaultFont16,
                    TextColor      = message.Author == ChatMessageAuthor.Player ? PlayerColor : AiColor,
                    WrapText       = true,
                    Width          = MessageListWidth,
                    AutoSizeHeight = true
                };

                label.RecalculateLayout();
                label.Location = new Point(0, top);

                _messageControls.Add(label);

                top = label.Bottom + MessageSpacing;
            }

            _scrollbar.RecalculateLayout();
            _scrollbar.ScrollDistance = 1f;
        }

        private static string GetAuthorName(ChatMessageAuthor author) {
            return author == ChatMessageAuthor.Player ? "Player" : "AI";
        }
    }
}
