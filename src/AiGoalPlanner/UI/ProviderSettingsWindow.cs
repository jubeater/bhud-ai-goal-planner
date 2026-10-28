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
        private const int WindowHeight = 510;

        private const int ContentWidth  = 540;
        private const int ContentHeight = 430;

        private static readonly Color HintColor  = new Color(170, 170, 170);
        private static readonly Color ErrorColor = new Color(240, 165, 150);
        private static readonly Color SavedColor = new Color(190, 230, 170);

        private readonly Func<string>              _readEndpoint;
        private readonly Func<string>              _readModel;
        private readonly Func<string, string>      _describeKey;
        private readonly Action<string, string>    _save;
        private readonly Action<string, string>    _saveKey;
        private readonly Action                    _providerChanged;
        private readonly Func<string, string>      _getSavedModel;
        private readonly Func<IEnumerable<string>> _getSavedEndpoints;
        private readonly Action<string>            _removeProvider;
        private readonly Func<string>              _readTokenBudget;
        private readonly Func<string, string>      _readInputPrice;
        private readonly Func<string, string>      _readOutputPrice;
        private readonly Action<string, string, string, string> _saveTokens;

        private readonly Dictionary<string, string> _customEndpointsByLabel =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly StandardWindow _window;
        private readonly Dropdown       _providerDropdown;
        private readonly StandardButton _removeButton;
        private readonly TextBox        _endpointBox;
        private readonly TextBox        _modelBox;
        private readonly TextBox        _budgetBox;
        private readonly TextBox        _inputPriceBox;
        private readonly TextBox        _outputPriceBox;
        private readonly Label          _keyStatusLabel;
        private readonly StandardButton _pasteKeyButton;
        private readonly StandardButton _clearKeyButton;
        private readonly StandardButton _saveButton;
        private readonly Label          _statusLabel;

        private bool _disposed;
        private bool _updatingUI;

        public ProviderSettingsWindow(AsyncTexture2D            windowBackground,
                                      Texture2D                 emblem,
                                      Func<string>              readEndpoint,
                                      Func<string>              readModel,
                                      Func<string, string>      describeKey,
                                      Action<string, string>    save,
                                      Action<string, string>    saveKey,
                                      Action                    providerChanged,
                                      Func<string, string>      getSavedModel     = null,
                                      Func<IEnumerable<string>> getSavedEndpoints = null,
                                      Action<string>            removeProvider    = null,
                                      Func<string>              readTokenBudget   = null,
                                      Func<string, string>      readInputPrice    = null,
                                      Func<string, string>      readOutputPrice   = null,
                                      Action<string, string, string, string> saveTokens = null) {
            _readEndpoint      = readEndpoint      ?? throw new ArgumentNullException(nameof(readEndpoint));
            _readModel         = readModel         ?? throw new ArgumentNullException(nameof(readModel));
            _describeKey       = describeKey       ?? throw new ArgumentNullException(nameof(describeKey));
            _save              = save              ?? throw new ArgumentNullException(nameof(save));
            _saveKey           = saveKey           ?? throw new ArgumentNullException(nameof(saveKey));
            _providerChanged   = providerChanged   ?? throw new ArgumentNullException(nameof(providerChanged));
            _getSavedModel     = getSavedModel;
            _getSavedEndpoints = getSavedEndpoints;
            _removeProvider    = removeProvider;
            _readTokenBudget   = readTokenBudget;
            _readInputPrice    = readInputPrice;
            _readOutputPrice   = readOutputPrice;
            _saveTokens        = saveTokens;

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
                Text      = "Provider"
            };

            _providerDropdown = new Dropdown {
                Parent   = content,
                Location = new Point(0, 22),
                Size     = new Point(ContentWidth - 96, 27)
            };

            _removeButton = new StandardButton {
                Parent   = content,
                Location = new Point(ContentWidth - 90, 22),
                Size     = new Point(90, StandardButton.STANDARD_CONTROL_HEIGHT),
                Text     = "Remove",
                Enabled  = false
            };
            _removeButton.Click += OnRemoveClicked;

            PopulateDropdown(string.Empty);
            _providerDropdown.ValueChanged += OnProviderDropdownChanged;

            new Label {
                Parent    = content,
                Location  = new Point(0, 57),
                Size      = new Point(ContentWidth, 20),
                Font      = GameService.Content.DefaultFont14,
                TextColor = HintColor,
                Text      = "Endpoint URL (OpenAI-compatible)"
            };

            // Both fields start empty: a placeholder is a hint, and a placeholder that looks like a
            // value (or names one Provider's model) is read as something already filled in.
            _endpointBox = new TextBox {
                Parent          = content,
                Location        = new Point(0, 79),
                Size            = new Point(ContentWidth, 27),
                PlaceholderText = "e.g. https://your-provider.example/v1"
            };
            _endpointBox.TextChanged += OnEndpointBoxTextChanged;

            new Label {
                Parent    = content,
                Location  = new Point(0, 114),
                Size      = new Point(ContentWidth, 20),
                Font      = GameService.Content.DefaultFont14,
                TextColor = HintColor,
                Text      = "Model"
            };

            _modelBox = new TextBox {
                Parent          = content,
                Location        = new Point(0, 136),
                Size            = new Point(ContentWidth, 27),
                PlaceholderText = "Model name from your Provider"
            };

            new Label {
                Parent    = content,
                Location  = new Point(0, 171),
                Size      = new Point(ContentWidth, 20),
                Font      = GameService.Content.DefaultFont14,
                TextColor = HintColor,
                Text      = "API key (optional: copy it, then click Paste key)"
            };

            // The key is pasted rather than typed, so it is never drawn: the window is told only
            // whether one is saved and its last four characters.
            _pasteKeyButton = new StandardButton {
                Parent   = content,
                Location = new Point(0, 193),
                Size     = new Point(110, StandardButton.STANDARD_CONTROL_HEIGHT),
                Text     = "Paste key"
            };
            _pasteKeyButton.Click += OnPasteKeyClicked;

            _clearKeyButton = new StandardButton {
                Parent   = content,
                Location = new Point(116, 193),
                Size     = new Point(110, StandardButton.STANDARD_CONTROL_HEIGHT),
                Text     = "Clear key"
            };
            _clearKeyButton.Click += OnClearKeyClicked;

            _keyStatusLabel = new Label {
                Parent    = content,
                Location  = new Point(232, 198),
                Size      = new Point(ContentWidth - 232, 20),
                Font      = GameService.Content.DefaultFont14,
                TextColor = HintColor,
                Text      = ProviderKeySummary.NoKey
            };

            _saveButton = new StandardButton {
                Parent   = content,
                Location = new Point(0, 229),
                Size     = new Point(90, StandardButton.STANDARD_CONTROL_HEIGHT),
                Text     = "Save"
            };
            _saveButton.Click += OnSaveClicked;

            new Label {
                Parent    = content,
                Location  = new Point(0, 263),
                Size      = new Point(ContentWidth, 20),
                Font      = GameService.Content.DefaultFont14,
                TextColor = HintColor,
                Text      = "Token Budget (tokens per Chat, 0 = no limit)"
            };

            // A number the Player types: Blish HUD draws the setting's own box elsewhere too, and
            // this one keeps the budget next to the Provider it applies to.
            _budgetBox = new TextBox {
                Parent          = content,
                Location        = new Point(0, 285),
                Size            = new Point(200, 27),
                PlaceholderText = "e.g. 300000"
            };

            new Label {
                Parent    = content,
                Location  = new Point(0, 320),
                Size      = new Point(ContentWidth, 20),
                Font      = GameService.Content.DefaultFont14,
                TextColor = HintColor,
                Text      = "Prices in $ per million tokens (optional, for this Provider)"
            };

            // Empty fields mean no estimate: nothing is shown until both prices are entered.
            _inputPriceBox = new TextBox {
                Parent          = content,
                Location        = new Point(0, 342),
                Size            = new Point(265, 27),
                PlaceholderText = "Input $/M, e.g. 3"
            };

            _outputPriceBox = new TextBox {
                Parent          = content,
                Location        = new Point(275, 342),
                Size            = new Point(265, 27),
                PlaceholderText = "Output $/M, e.g. 15"
            };

            // The status gets its own row, the full width and a wrap: the longest message is two
            // lines, and a status that is cut off says nothing.
            _statusLabel = new Label {
                Parent         = content,
                Location       = new Point(0, 377),
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

            string endpoint = (_readEndpoint() ?? string.Empty).Trim();
            string model    = (_readModel() ?? string.Empty).Trim();

            _updatingUI = true;
            try {
                PopulateDropdown(endpoint);

                _endpointBox.Text = endpoint;
                _modelBox.Text    = model;
                _statusLabel.Text = string.Empty;

                _budgetBox.Text     = _readTokenBudget?.Invoke() ?? string.Empty;
                _inputPriceBox.Text  = _readInputPrice?.Invoke(endpoint) ?? string.Empty;
                _outputPriceBox.Text = _readOutputPrice?.Invoke(endpoint) ?? string.Empty;

                RefreshKeyStatus(endpoint);
                UpdateRemoveButtonState();
            } finally {
                _updatingUI = false;
            }

            _window.Show();
            _endpointBox.Focused = true;
        }

        public void Dispose() {
            _disposed = true;

            _saveButton.Click              -= OnSaveClicked;
            _removeButton.Click            -= OnRemoveClicked;
            _pasteKeyButton.Click          -= OnPasteKeyClicked;
            _clearKeyButton.Click          -= OnClearKeyClicked;
            _providerDropdown.ValueChanged -= OnProviderDropdownChanged;
            _endpointBox.TextChanged       -= OnEndpointBoxTextChanged;

            _window.Dispose();
        }

        private static string HostFor(string endpoint) {
            if (Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uri) && !string.IsNullOrWhiteSpace(uri.Host)) {
                return uri.Host;
            }

            return (endpoint ?? string.Empty).Trim();
        }

        private void PopulateDropdown(string selectedEndpoint) {
            _providerDropdown.Items.Clear();
            _customEndpointsByLabel.Clear();

            foreach (ProviderEndpointSuggestion suggestion in ProviderEndpointSuggestions.All) {
                _providerDropdown.Items.Add(suggestion.Name);
            }

            if (_getSavedEndpoints != null) {
                foreach (string endpoint in _getSavedEndpoints()) {
                    if (string.IsNullOrWhiteSpace(endpoint)) continue;
                    if (ProviderEndpointSuggestions.Match(endpoint) != null) continue;

                    string label = HostFor(endpoint);
                    string uniqueLabel = label;
                    int counter = 2;
                    while (_providerDropdown.Items.Contains(uniqueLabel) || uniqueLabel == "Custom") {
                        uniqueLabel = label + " (" + counter++ + ")";
                    }

                    _customEndpointsByLabel[uniqueLabel] = endpoint;
                    _providerDropdown.Items.Add(uniqueLabel);
                }
            }

            _providerDropdown.Items.Add("Custom");

            SelectDropdownItemForEndpoint(selectedEndpoint);
            UpdateRemoveButtonState();
        }

        private void SelectDropdownItemForEndpoint(string endpoint) {
            ProviderEndpointSuggestion match = ProviderEndpointSuggestions.Match(endpoint);
            if (match != null) {
                _providerDropdown.SelectedItem = match.Name;
                return;
            }

            if (!string.IsNullOrWhiteSpace(endpoint)) {
                foreach (KeyValuePair<string, string> entry in _customEndpointsByLabel) {
                    if (ProviderEndpointSuggestions.Normalize(entry.Value).Equals(ProviderEndpointSuggestions.Normalize(endpoint), StringComparison.OrdinalIgnoreCase)) {
                        _providerDropdown.SelectedItem = entry.Key;
                        return;
                    }
                }
            }

            _providerDropdown.SelectedItem = "Custom";
        }

        private void OnProviderDropdownChanged(object sender, ValueChangedEventArgs e) {
            if (_updatingUI || _disposed) return;

            UpdateRemoveButtonState();

            string selected = _providerDropdown.SelectedItem;
            if (string.IsNullOrEmpty(selected) || selected == "Custom") return;

            string endpoint = ProviderEndpointSuggestions.FindByName(selected)?.Endpoint;
            if (endpoint == null && !_customEndpointsByLabel.TryGetValue(selected, out endpoint)) {
                return;
            }

            _updatingUI = true;
            try {
                _endpointBox.Text = endpoint;

                string savedModel = _getSavedModel?.Invoke(endpoint);
                _modelBox.Text = savedModel ?? string.Empty;

                _inputPriceBox.Text  = _readInputPrice?.Invoke(endpoint) ?? string.Empty;
                _outputPriceBox.Text = _readOutputPrice?.Invoke(endpoint) ?? string.Empty;

                RefreshKeyStatus(endpoint);
                UpdateRemoveButtonState();
            } finally {
                _updatingUI = false;
            }
        }

        private void OnEndpointBoxTextChanged(object sender, EventArgs e) {
            if (_updatingUI || _disposed) return;

            string endpoint = ReadEndpoint();

            _updatingUI = true;
            try {
                SelectDropdownItemForEndpoint(endpoint);
                UpdateRemoveButtonState();

                if (string.IsNullOrWhiteSpace(_modelBox.Text) && !string.IsNullOrWhiteSpace(endpoint)) {
                    string savedModel = _getSavedModel?.Invoke(endpoint);
                    if (!string.IsNullOrWhiteSpace(savedModel)) {
                        _modelBox.Text = savedModel;
                    }
                }

                if (string.IsNullOrWhiteSpace(_inputPriceBox.Text) && string.IsNullOrWhiteSpace(_outputPriceBox.Text)
                 && !string.IsNullOrWhiteSpace(endpoint)) {
                    _inputPriceBox.Text  = _readInputPrice?.Invoke(endpoint) ?? string.Empty;
                    _outputPriceBox.Text = _readOutputPrice?.Invoke(endpoint) ?? string.Empty;
                }

                RefreshKeyStatus(endpoint);
            } finally {
                _updatingUI = false;
            }
        }

        private void UpdateRemoveButtonState() {
            string selected = _providerDropdown.SelectedItem;
            _removeButton.Enabled = !string.IsNullOrEmpty(selected) && _customEndpointsByLabel.ContainsKey(selected);
        }

        private void OnRemoveClicked(object sender, MouseEventArgs e) {
            if (_disposed) return;

            string selected = _providerDropdown.SelectedItem;
            if (string.IsNullOrEmpty(selected) || !_customEndpointsByLabel.TryGetValue(selected, out string endpoint)) {
                return;
            }

            try {
                _removeProvider?.Invoke(endpoint);
            } catch (Exception exception) {
                Logger.Warn(exception, "Removing the stored API key failed.");
                ShowStatus("The saved key could not be removed.", ErrorColor);
                return;
            }

            _updatingUI = true;
            try {
                PopulateDropdown(string.Empty);
                _providerDropdown.SelectedItem = "Custom";
                _endpointBox.Text              = string.Empty;
                _modelBox.Text                 = string.Empty;
                _inputPriceBox.Text            = string.Empty;
                _outputPriceBox.Text           = string.Empty;
                RefreshKeyStatus(string.Empty);
                UpdateRemoveButtonState();
                ShowStatus(string.Empty, HintColor);
            } finally {
                _updatingUI = false;
            }

            _providerChanged();
        }

        private void OnSaveClicked(object sender, MouseEventArgs e) {
            string endpoint = ReadEndpoint();

            // The token settings are validated first, so a mistyped budget or price stops the save
            // with an error instead of keeping half of what the Player typed.
            try {
                _saveTokens?.Invoke(endpoint, _budgetBox.Text, _inputPriceBox.Text, _outputPriceBox.Text);
            } catch (ArgumentException exception) {
                ShowStatus(exception.Message, ErrorColor);
                return;
            } catch (ProviderException exception) {
                ShowStatus(DescribeProblem(exception.Failure), ErrorColor);
                return;
            } catch (Exception exception) {
                Logger.Warn(exception, "Saving the token settings failed.");
                ShowStatus("The token settings could not be saved.", ErrorColor);
                return;
            }

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

            _updatingUI = true;
            try {
                PopulateDropdown(endpoint);
            } finally {
                _updatingUI = false;
            }

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
