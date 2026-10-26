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
    /// <see cref="ProviderKeyStore"/>. A saved key is never read back into the window, so it cannot
    /// be shown on screen by accident.
    /// </remarks>
    internal sealed class ProviderSettingsWindow : IDisposable
    {
        private static readonly Logger Logger = Logger.GetLogger<ProviderSettingsWindow>();

        private const int WindowWidth  = 470;
        private const int WindowHeight = 300;

        private static readonly Color HintColor   = new Color(170, 170, 170);
        private static readonly Color ErrorColor  = new Color(240, 165, 150);
        private static readonly Color SavedColor  = new Color(190, 230, 170);

        private readonly Func<string>      _readEndpoint;
        private readonly Func<string>      _readModel;
        private readonly Func<string, bool> _hasKey;
        private readonly Action<string, string, string> _save;
        private readonly Action<string>    _forgetKey;
        private readonly Action            _providerChanged;

        private readonly StandardWindow _window;
        private readonly TextBox        _endpointBox;
        private readonly TextBox        _modelBox;
        private readonly TextBox        _keyBox;
        private readonly StandardButton _saveButton;
        private readonly StandardButton _forgetButton;
        private readonly Label          _statusLabel;

        private bool _disposed;

        public ProviderSettingsWindow(AsyncTexture2D    windowBackground,
                                      Texture2D         emblem,
                                      Func<string>      readEndpoint,
                                      Func<string>      readModel,
                                      Func<string, bool> hasKey,
                                      Action<string, string, string> save,
                                      Action<string>    forgetKey,
                                      Action            providerChanged) {
            _readEndpoint    = readEndpoint    ?? throw new ArgumentNullException(nameof(readEndpoint));
            _readModel       = readModel       ?? throw new ArgumentNullException(nameof(readModel));
            _hasKey          = hasKey          ?? throw new ArgumentNullException(nameof(hasKey));
            _save            = save            ?? throw new ArgumentNullException(nameof(save));
            _forgetKey       = forgetKey       ?? throw new ArgumentNullException(nameof(forgetKey));
            _providerChanged = providerChanged ?? throw new ArgumentNullException(nameof(providerChanged));

            _window = new StandardWindow(
                windowBackground,
                new Rectangle(25, 26, WindowWidth, WindowHeight),
                new Rectangle(40, 50, 440, 220)) {
                Parent        = GameService.Graphics.SpriteScreen,
                Title         = "Provider settings",
                Subtitle      = "Any OpenAI-compatible endpoint",
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
                Size      = new Point(440, 20),
                Font      = GameService.Content.DefaultFont14,
                TextColor = HintColor,
                Text      = "Endpoint URL (OpenAI-compatible, for example https://ollama.com/v1)"
            };

            _endpointBox = new TextBox {
                Parent          = content,
                Location        = new Point(0, 22),
                Size            = new Point(440, 27),
                PlaceholderText = "https://api.deepseek.com"
            };

            new Label {
                Parent    = content,
                Location  = new Point(0, 57),
                Size      = new Point(440, 20),
                Font      = GameService.Content.DefaultFont14,
                TextColor = HintColor,
                Text      = "Model"
            };

            _modelBox = new TextBox {
                Parent          = content,
                Location        = new Point(0, 79),
                Size            = new Point(440, 27),
                PlaceholderText = "deepseek-chat"
            };

            new Label {
                Parent    = content,
                Location  = new Point(0, 114),
                Size      = new Point(440, 20),
                Font      = GameService.Content.DefaultFont14,
                TextColor = HintColor,
                Text      = "API key"
            };

            // Blish HUD has no masked text field, so the box starts empty and is emptied again after
            // a save: a stored key is never put back on screen.
            _keyBox = new TextBox {
                Parent   = content,
                Location = new Point(0, 136),
                Size     = new Point(440, 27)
            };

            _saveButton = new StandardButton {
                Parent   = content,
                Location = new Point(0, 172),
                Size     = new Point(90, StandardButton.STANDARD_CONTROL_HEIGHT),
                Text     = "Save"
            };
            _saveButton.Click += OnSaveClicked;

            _forgetButton = new StandardButton {
                Parent   = content,
                Location = new Point(96, 172),
                Size     = new Point(110, StandardButton.STANDARD_CONTROL_HEIGHT),
                Text     = "Forget key"
            };
            _forgetButton.Click += OnForgetKeyClicked;

            _statusLabel = new Label {
                Parent    = content,
                Location  = new Point(212, 177),
                Size      = new Point(228, 20),
                Font      = GameService.Content.DefaultFont14,
                TextColor = HintColor,
                Text      = string.Empty
            };

            _window.Visible = false;
        }

        /// <summary>
        /// Shows the window with what is already saved, so the Player edits the Settings rather than
        /// a copy of them that has drifted.
        /// </summary>
        public void Show() {
            if (_disposed) return;

            string endpoint = (_readEndpoint() ?? string.Empty).Trim();

            _endpointBox.Text = endpoint;
            _modelBox.Text    = (_readModel() ?? string.Empty).Trim();
            _keyBox.Text      = string.Empty;
            _statusLabel.Text = string.Empty;

            RefreshKeyHint(endpoint);

            _window.Show();
            _endpointBox.Focused = true;
        }

        public void Dispose() {
            _disposed = true;

            _saveButton.Click   -= OnSaveClicked;
            _forgetButton.Click -= OnForgetKeyClicked;

            _window.Dispose();
        }

        private void OnSaveClicked(object sender, MouseEventArgs e) {
            try {
                _save(_endpointBox.Text, _modelBox.Text, _keyBox.Text);
            } catch (ProviderException exception) {
                ShowStatus(DescribeProblem(exception.Failure), ErrorColor);
                return;
            } catch (Exception exception) {
                Logger.Warn(exception, "Saving the Provider settings failed.");
                ShowStatus("Those settings could not be saved.", ErrorColor);
                return;
            }

            // Show what was actually kept, not what was typed.
            string endpoint = (_endpointBox.Text ?? string.Empty).Trim();

            _endpointBox.Text = endpoint;
            _modelBox.Text    = (_modelBox.Text ?? string.Empty).Trim();
            _keyBox.Text      = string.Empty;

            RefreshKeyHint(endpoint);
            ShowStatus("Saved. Your next message uses this.", SavedColor);

            _providerChanged();
        }

        private void OnForgetKeyClicked(object sender, MouseEventArgs e) {
            string endpoint = (_endpointBox.Text ?? string.Empty).Trim();

            if (endpoint.Length == 0) {
                ShowStatus("Enter the endpoint first.", ErrorColor);
                return;
            }

            try {
                _forgetKey(endpoint);
            } catch (Exception exception) {
                Logger.Warn(exception, "Removing the stored key failed.");
                ShowStatus("The saved key could not be removed.", ErrorColor);
                return;
            }

            RefreshKeyHint(endpoint);
            ShowStatus("The saved key was removed.", SavedColor);

            _providerChanged();
        }

        /// <summary>
        /// Says in the key box what leaving it empty will do, since there is no way to show a stored
        /// key back to the Player for comparison.
        /// </summary>
        private void RefreshKeyHint(string endpoint) {
            bool hasKey = endpoint.Length > 0 && _hasKey(endpoint);

            _keyBox.PlaceholderText = hasKey
                                          ? "A key is saved. Type a new one to replace it."
                                          : "Leave empty if your Provider does not need a key.";
        }

        private void ShowStatus(string text, Color color) {
            _statusLabel.Text      = text;
            _statusLabel.TextColor = color;
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
