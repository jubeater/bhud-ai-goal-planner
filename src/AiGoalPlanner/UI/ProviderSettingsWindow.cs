using System;
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
    /// The window where the Player connects a Provider: the endpoint, the model and, when the
    /// Provider needs one, the API key.
    /// </summary>
    /// <remarks>
    /// The window collects what the Player typed and hands it over; what a usable Provider is, and
    /// where the key is kept, is decided by <see cref="ProviderSettings"/> and
    /// <see cref="ProviderKeyStore"/>. Blish HUD has no masked text box, so a key is never typed
    /// into a field where it would be drawn on screen: it is pasted from the clipboard, and this
    /// window only ever says whether one is saved and how it ends.
    /// </remarks>
    internal sealed class ProviderSettingsWindow : IDisposable
    {
        private static readonly Logger Logger = Logger.GetLogger<ProviderSettingsWindow>();

        /// <summary>
        /// As wide as the Chat window, so the subtitle has room to the left of the close button:
        /// Blish HUD's title bar spends 80 px on the emblem and then the title itself, and the close
        /// button sits 16 px in from the right edge.
        /// </summary>
        private const int WindowWidth  = 560;
        private const int WindowHeight = 340;

        private const int ContentWidth  = 540;
        private const int ContentHeight = 260;

        private static readonly Color HintColor  = new Color(170, 170, 170);
        private static readonly Color ErrorColor = new Color(240, 165, 150);
        private static readonly Color SavedColor = new Color(190, 230, 170);

        private readonly Func<string>           _readEndpoint;
        private readonly Func<string>           _readModel;
        private readonly Func<string, string>   _describeKey;
        private readonly Action<string, string> _save;
        private readonly Action<string, string> _saveKey;
        private readonly Action                 _providerChanged;

        private readonly StandardWindow _window;
        private readonly TextBox        _endpointBox;
        private readonly TextBox        _modelBox;
        private readonly Label          _keyStatusLabel;
        private readonly StandardButton _pasteKeyButton;
        private readonly StandardButton _clearKeyButton;
        private readonly StandardButton _saveButton;
        private readonly Label          _statusLabel;

        private bool _disposed;

        public ProviderSettingsWindow(AsyncTexture2D         windowBackground,
                                      Texture2D              emblem,
                                      Func<string>           readEndpoint,
                                      Func<string>           readModel,
                                      Func<string, string>   describeKey,
                                      Action<string, string> save,
                                      Action<string, string> saveKey,
                                      Action                 providerChanged) {
            _readEndpoint    = readEndpoint    ?? throw new ArgumentNullException(nameof(readEndpoint));
            _readModel       = readModel       ?? throw new ArgumentNullException(nameof(readModel));
            _describeKey     = describeKey     ?? throw new ArgumentNullException(nameof(describeKey));
            _save            = save            ?? throw new ArgumentNullException(nameof(save));
            _saveKey         = saveKey         ?? throw new ArgumentNullException(nameof(saveKey));
            _providerChanged = providerChanged ?? throw new ArgumentNullException(nameof(providerChanged));

            _window = new StandardWindow(
                windowBackground,
                new Rectangle(25, 26, WindowWidth, WindowHeight),
                new Rectangle(40, 50, ContentWidth, ContentHeight)) {
                Parent        = GameService.Graphics.SpriteScreen,
                Title         = "Provider settings",
                Subtitle      = "Connect a Provider",
                Emblem        = emblem,
                Id            = "jubeater.aigoalplanner.provider",
                Location      = new Point(340, 140),
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

            new Label {
                Parent    = content,
                Location  = new Point(0, 0),
                Size      = new Point(ContentWidth, 20),
                Font      = GameService.Content.DefaultFont14,
                TextColor = HintColor,
                Text      = "Endpoint URL (OpenAI-compatible)"
            };

            // Both fields start empty: a placeholder is a hint, and a placeholder that looks like a
            // value (or names one Provider's model) is read as something already filled in.
            _endpointBox = new TextBox {
                Parent          = content,
                Location        = new Point(0, 22),
                Size            = new Point(ContentWidth, 27),
                PlaceholderText = "e.g. https://your-provider.example/v1"
            };

            new Label {
                Parent    = content,
                Location  = new Point(0, 57),
                Size      = new Point(ContentWidth, 20),
                Font      = GameService.Content.DefaultFont14,
                TextColor = HintColor,
                Text      = "Model"
            };

            _modelBox = new TextBox {
                Parent          = content,
                Location        = new Point(0, 79),
                Size            = new Point(ContentWidth, 27),
                PlaceholderText = "Model name from your Provider"
            };

            new Label {
                Parent    = content,
                Location  = new Point(0, 114),
                Size      = new Point(ContentWidth, 20),
                Font      = GameService.Content.DefaultFont14,
                TextColor = HintColor,
                Text      = "API key (optional: copy it, then click Paste key)"
            };

            // The key is pasted rather than typed, so it is never drawn: the window is told only
            // whether one is saved and its last four characters.
            _pasteKeyButton = new StandardButton {
                Parent   = content,
                Location = new Point(0, 136),
                Size     = new Point(110, StandardButton.STANDARD_CONTROL_HEIGHT),
                Text     = "Paste key"
            };
            _pasteKeyButton.Click += OnPasteKeyClicked;

            _clearKeyButton = new StandardButton {
                Parent   = content,
                Location = new Point(116, 136),
                Size     = new Point(110, StandardButton.STANDARD_CONTROL_HEIGHT),
                Text     = "Clear key"
            };
            _clearKeyButton.Click += OnClearKeyClicked;

            _keyStatusLabel = new Label {
                Parent    = content,
                Location  = new Point(232, 141),
                Size      = new Point(ContentWidth - 232, 20),
                Font      = GameService.Content.DefaultFont14,
                TextColor = HintColor,
                Text      = ProviderKeySummary.NoKey
            };

            _saveButton = new StandardButton {
                Parent   = content,
                Location = new Point(0, 172),
                Size     = new Point(90, StandardButton.STANDARD_CONTROL_HEIGHT),
                Text     = "Save"
            };
            _saveButton.Click += OnSaveClicked;

            // The status gets its own row, the full width and a wrap: the longest message is two
            // lines, and a status that is cut off says nothing.
            _statusLabel = new Label {
                Parent         = content,
                Location       = new Point(0, 206),
                Width          = ContentWidth,
                Font           = GameService.Content.DefaultFont14,
                TextColor      = HintColor,
                Text           = string.Empty,
                WrapText       = true,
                AutoSizeHeight = true
            };

            _window.Visible = false;
        }

        /// <summary>
        /// Shows the window with what is already saved, so the Player edits the Settings rather than
        /// a copy of them that has drifted.
        /// </summary>
        public void Show() {
            if (_disposed) return;

            string endpoint = ReadEndpoint();

            _endpointBox.Text = endpoint;
            _modelBox.Text    = (_readModel() ?? string.Empty).Trim();
            _statusLabel.Text = string.Empty;

            RefreshKeyStatus(endpoint);

            _window.Show();
            _endpointBox.Focused = true;
        }

        public void Dispose() {
            _disposed = true;

            _saveButton.Click     -= OnSaveClicked;
            _pasteKeyButton.Click -= OnPasteKeyClicked;
            _clearKeyButton.Click -= OnClearKeyClicked;

            _window.Dispose();
        }

        private void OnSaveClicked(object sender, MouseEventArgs e) {
            string endpoint = ReadEndpoint();

            try {
                _save(endpoint, _modelBox.Text);
            } catch (ProviderException exception) {
                ShowStatus(DescribeProblem(exception.Failure), ErrorColor);
                return;
            } catch (Exception exception) {
                Logger.Warn(exception, "Saving the Provider settings failed.");
                ShowStatus("Those settings could not be saved.", ErrorColor);
                return;
            }

            // Show what was actually kept, not what was typed.
            endpoint = ReadEndpoint();

            _endpointBox.Text = endpoint;
            _modelBox.Text    = (_modelBox.Text ?? string.Empty).Trim();

            RefreshKeyStatus(endpoint);
            ShowStatus("Saved. Your next message uses this.", SavedColor);

            _providerChanged();
        }

        /// <summary>
        /// Reads the key from the clipboard. A key is never typed into the window, so this is the
        /// only way one gets in, and the window is never given the text it saved.
        /// </summary>
        private void OnPasteKeyClicked(object sender, MouseEventArgs e) {
            string endpoint = ReadEndpoint();

            if (endpoint.Length == 0) {
                ShowStatus("Enter the endpoint first.", ErrorColor);
                return;
            }

            PasteKeyFromClipboardAsync(endpoint);
        }

        private async void PasteKeyFromClipboardAsync(string endpoint) {
            string clipboardText;

            try {
                clipboardText = await ClipboardUtil.WindowsClipboardService.GetTextAsync().ConfigureAwait(false);
            } catch (Exception exception) {
                // Only the failure is logged: what was on the clipboard never reaches the log.
                Logger.Warn(exception, "Reading the clipboard for the API key failed.");

                ShowOnMainThread(() => ShowStatusIfOpen("The clipboard could not be read.", ErrorColor));
                return;
            }

            string key = ProviderKeySummary.FromClipboard(clipboardText);

            // The clipboard is read off the game thread, and Blish HUD controls may only be touched
            // from the game thread, so the rest happens there.
            ShowOnMainThread(() => SavePastedKey(endpoint, key));
        }

        private void SavePastedKey(string endpoint, string key) {
            if (_disposed) return;

            if (key == null) {
                ShowStatus("The clipboard is empty. Copy your API key first.", ErrorColor);
                return;
            }

            try {
                _saveKey(endpoint, key);
            } catch (ProviderException exception) {
                ShowStatus(DescribeProblem(exception.Failure), ErrorColor);
                return;
            } catch (Exception exception) {
                Logger.Warn(exception, "Saving the pasted API key failed.");
                ShowStatus("The API key could not be saved.", ErrorColor);
                return;
            }

            RefreshKeyStatus(endpoint);
            ShowStatus("Key saved. Your next message uses it.", SavedColor);

            _providerChanged();
        }

        private void OnClearKeyClicked(object sender, MouseEventArgs e) {
            string endpoint = ReadEndpoint();

            if (endpoint.Length == 0) {
                ShowStatus("Enter the endpoint first.", ErrorColor);
                return;
            }

            try {
                _saveKey(endpoint, string.Empty);
            } catch (ProviderException exception) {
                ShowStatus(DescribeProblem(exception.Failure), ErrorColor);
                return;
            } catch (Exception exception) {
                Logger.Warn(exception, "Removing the stored API key failed.");
                ShowStatus("The saved key could not be removed.", ErrorColor);
                return;
            }

            RefreshKeyStatus(endpoint);
            ShowStatus("The saved key was removed.", SavedColor);

            _providerChanged();
        }

        /// <summary>
        /// Rewrites the one line the window shows about the key: that there is none, or that there
        /// is one and how it ends. The key itself is never asked for here.
        /// </summary>
        private void RefreshKeyStatus(string endpoint) {
            string description = _describeKey(endpoint);

            _keyStatusLabel.Text      = description;
            _keyStatusLabel.TextColor = description == ProviderKeySummary.NoKey ? HintColor : SavedColor;
        }

        private static void ShowOnMainThread(Action action) {
            GameService.Overlay.QueueMainThreadUpdate(gameTime => action());
        }

        private string ReadEndpoint() {
            return (_endpointBox.Text ?? string.Empty).Trim();
        }

        private void ShowStatus(string text, Color color) {
            _statusLabel.Text      = text;
            _statusLabel.TextColor = color;
        }

        /// <summary>
        /// Shows a status that was delivered from off the game thread, which may arrive after the
        /// window has already been closed and its controls disposed.
        /// </summary>
        private void ShowStatusIfOpen(string text, Color color) {
            if (_disposed) return;

            ShowStatus(text, color);
        }

        private static string DescribeProblem(ProviderFailure failure) {
            switch (failure) {
                case ProviderFailure.NotConfigured:
                    return "An endpoint and a model are both needed.";
                case ProviderFailure.InvalidEndpoint:
                    return "That endpoint is not an http or https URL.";
                default:
                    return "Those Provider settings could not be saved.";
            }
        }
    }
}

