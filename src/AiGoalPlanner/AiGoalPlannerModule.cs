using System;
using System.ComponentModel.Composition;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Blish_HUD.Modules;
using Blish_HUD.Modules.Managers;
using Blish_HUD.Settings;
using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.Providers;
using jubeater.aigoalplanner.UI;
using Microsoft.Extensions.AI;
using Microsoft.Xna.Framework.Graphics;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace jubeater.aigoalplanner
{
    /// <summary>
    /// The Blish HUD module: it wires the corner icon and the Chat window to one Chat.
    /// </summary>
    [Export(typeof(Module))]
    public class AiGoalPlannerModule : Module
    {
        private static readonly Logger Logger = Logger.GetLogger<AiGoalPlannerModule>();

        /// <summary>
        /// A constant, so this module's corner icon keeps its place in the Player's row of icons
        /// instead of moving around between sessions.
        /// </summary>
        private const int CornerIconPriority = 1937184993;

        /// <summary>
        /// Blish HUD's own window background texture, the same one Guild Wars 2 windows use.
        /// </summary>
        private const int WindowBackgroundAssetId = 155997;

        internal SettingsManager    SettingsManager    => this.ModuleParameters.SettingsManager;
        internal ContentsManager    ContentsManager    => this.ModuleParameters.ContentsManager;
        internal DirectoriesManager DirectoriesManager => this.ModuleParameters.DirectoriesManager;
        internal Gw2ApiManager      Gw2ApiManager      => this.ModuleParameters.Gw2ApiManager;

        private Chat       _chat;
        private ChatWindow _chatWindow;
        private CornerIcon _cornerIcon;

        /// <summary>
        /// Created the first time the Player opens Provider settings, so the module does not pay for
        /// a second window it may never show.
        /// </summary>
        private ProviderSettingsWindow _providerWindow;

        private SettingEntry<string> _endpointSetting;
        private SettingEntry<string> _modelSetting;
        private SettingEntry<string> _savedProvidersSetting;
        private SavedProviders       _savedProviders;
        private ProviderKeyStore     _keyStore;
        private ToolCallingCapabilityStore _capabilityStore;
        private HttpClient           _httpClient;

        /// <summary>
        /// The module's own icon, loaded once from <c>ref/</c> and shared by the corner icon and the
        /// Chat window. Content loaded from <c>ref/</c> belongs to the module, so <see cref="Unload"/>
        /// disposes it after the controls that draw it are gone.
        /// </summary>
        private Texture2D _iconTexture;

        [ImportingConstructor]
        public AiGoalPlannerModule([Import("ModuleParameters")] ModuleParameters moduleParameters) : base(moduleParameters) { /* NOOP */ }

        protected override void Initialize() {
            DefineProviderSettings();

            _keyStore        = new ProviderKeyStore(DirectoriesManager.GetFullDirectoryPath("aigoalplanner"));
            _capabilityStore = new ToolCallingCapabilityStore(DirectoriesManager.GetFullDirectoryPath("aigoalplanner"));
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            _chat = new Chat(CreateChatClient, BuildSystemPrompt);
        }

        private void DefineProviderSettings() {
            SettingCollection settings = this.SettingsManager.ModuleSettings;

            _endpointSetting = settings.DefineSetting(
                "provider.endpoint",
                string.Empty,
                () => "Endpoint URL",
                () => "The Provider's OpenAI-compatible endpoint, for example https://ollama.com/v1.");

            _modelSetting = settings.DefineSetting(
                "provider.model",
                string.Empty,
                () => "Model",
                () => "The model to use, for example a model name from the Provider's settings page.");

            _savedProvidersSetting = settings.DefineSetting(
                "provider.saved",
                string.Empty,
                () => "Saved Providers",
                () => "Remembers the models configured for each Provider.");

            _savedProviders = SavedProviders.FromJson(_savedProvidersSetting.Value);
        }

        /// <summary>
        /// Builds the Provider client for the current Settings. A change in Settings takes effect on
        /// the next Request; the Chat is not rebuilt, so its history stays.
        /// </summary>
        private IChatClient CreateChatClient() {
            string endpoint = _endpointSetting.Value;

            string model    = _modelSetting.Value;

            ProviderSettings settings = ProviderSettings.Parse(endpoint, model, _keyStore?.Load(endpoint));
            var client = new OpenAiCompatibleChatClient(settings, _httpClient);

            var toolCallingClient = new ToolCallingChatClient(client, endpoint, model, _capabilityStore);
            return new ProbeLoggingChatClient(toolCallingClient, message => Logger.Warn(message), settings.ApiKey);
        }

        protected override Task LoadAsync() {
            Logger.Info("AI Goal Planner is loading.");

            return Task.CompletedTask;
        }

        protected override void OnModuleLoaded(EventArgs e) {
            _iconTexture = this.ContentsManager.GetTexture("icon.png");

            CreateCornerIcon();
            CreateChatWindow();

            base.OnModuleLoaded(e);
        }

        /// <inheritdoc />
        protected override void Unload() {
            if (_cornerIcon != null) {
                _cornerIcon.Click -= OnCornerIconClicked;
                _cornerIcon.Dispose();
                _cornerIcon = null;
            }

            if (_chatWindow != null) {
                _chatWindow.Dispose();
                _chatWindow = null;
            }

            if (_providerWindow != null) {
                _providerWindow.Dispose();
                _providerWindow = null;
            }

            // Nothing draws the icon any more, so the module releases it. Nulling it out means a
            // re-enabled module loads a fresh texture instead of reusing a disposed one.
            if (_iconTexture != null) {
                // GetTexture falls back to Blish HUD's shared error texture, which is not ours to dispose.
                if (!ReferenceEquals(_iconTexture, ContentService.Textures.Error)) {
                    _iconTexture.Dispose();
                }

                _iconTexture = null;
            }

            _chat = null;
            _capabilityStore = null;
            _savedProviders  = null;
            _keyStore        = null;

            if (_httpClient != null) {
                _httpClient.Dispose();
                _httpClient = null;
            }

            Logger.Info("AI Goal Planner unloaded.");
        }

        private void CreateCornerIcon() {
            _cornerIcon = new CornerIcon {
                Icon             = _iconTexture,
                BasicTooltipText = "AI Goal Planner",
                Priority         = CornerIconPriority,
                Parent           = GameService.Graphics.SpriteScreen
            };

            _cornerIcon.Click += OnCornerIconClicked;
        }

        private void CreateChatWindow() {
            _chatWindow = new ChatWindow(
                _chat,
                AsyncTexture2D.FromAssetId(WindowBackgroundAssetId),
                _iconTexture,
                OpenProviderSettings,
                DescribeProvider);
        }

        private void OnCornerIconClicked(object sender, MouseEventArgs e) {
            _chatWindow?.Toggle();
        }

        private void OpenProviderSettings() {
            if (_providerWindow == null) {
                _providerWindow = new ProviderSettingsWindow(
                    AsyncTexture2D.FromAssetId(WindowBackgroundAssetId),
                    _iconTexture,
                    () => _endpointSetting.Value,
                    () => _modelSetting.Value,
                    DescribeProviderKey,
                    SaveProviderSettings,
                    SaveProviderKey,
                    NotifyProviderChanged,
                    endpoint => _savedProviders.GetModel(endpoint),
                    () => _savedProviders.Endpoints,
                    RemoveSavedProvider);
            }

            _providerWindow.Show();
        }

        /// <summary>
        /// Keeps the endpoint and the model. The key has its own buttons, so saving the rest of the
        /// Settings never touches a stored key.
        /// </summary>
        private void SaveProviderSettings(string endpoint, string model) {
            ProviderSettings settings = ProviderSettings.Parse(endpoint, model, apiKey: null);

            _endpointSetting.Value = settings.Endpoint;
            _modelSetting.Value    = settings.Model;

            _savedProviders.SetModel(settings.Endpoint, settings.Model);
            _savedProvidersSetting.Value = _savedProviders.ToJson();

            _capabilityStore?.ForgetFailure(settings.Endpoint, settings.Model);
        }

        /// <summary>
        /// Stores the key the Player pasted for the endpoint in the window, or removes the stored
        /// key when the Player clears it. The key is never written to a Setting, logged or shown.
        /// </summary>
        private void SaveProviderKey(string endpoint, string key) {
            // Refuses a key for an endpoint that is not a URL, so a half-typed endpoint cannot
            // leave a key file behind that nothing will ever read again.
            ProviderSettings.ParseEndpoint(endpoint);

            _keyStore.Save(endpoint, key);
        }

        /// <summary>
        /// The one line the settings view may show about the key: whether one is saved and its last
        /// four characters, never the key itself.
        /// </summary>
        private string DescribeProviderKey(string endpoint) {
            return ProviderKeySummary.Describe(_keyStore.Load(endpoint));
        }

        private void NotifyProviderChanged() {
            _chatWindow?.RefreshProvider();
        }

        private string DescribeProvider() {
            string endpoint = _endpointSetting.Value;

            return ProviderSummary.Describe(endpoint, _modelSetting.Value, _keyStore.HasKey(endpoint));
        }

        /// <summary>
        /// The identity message the Chat puts ahead of the Player's words. It is built from the same
        /// Settings as the Provider client, so the model and the host it names are the ones the next
        /// Request really uses.
        /// </summary>
        private string BuildSystemPrompt() {
            return SystemPrompt.For(_modelSetting.Value, _endpointSetting.Value);
        }

        /// <summary>
        /// Removes a saved custom Provider: deletes its stored key, forgets its saved model,
        /// drops its capability records, and if it was the one currently in use, clears the
        /// endpoint and model Settings so the Chat shows "Connect a Provider first".
        /// </summary>
        public void RemoveSavedProvider(string endpoint) {
            if (string.IsNullOrWhiteSpace(endpoint)) return;

            string normalized = ProviderEndpointSuggestions.Normalize(endpoint);

            _keyStore?.Remove(endpoint);

            _savedProviders?.Remove(endpoint);
            if (_savedProvidersSetting != null && _savedProviders != null) {
                _savedProvidersSetting.Value = _savedProviders.ToJson();
            }

            _capabilityStore?.Clear(endpoint);

            if (ProviderEndpointSuggestions.Normalize(_endpointSetting?.Value).Equals(normalized, StringComparison.OrdinalIgnoreCase)) {
                if (_endpointSetting != null) _endpointSetting.Value = string.Empty;
                if (_modelSetting != null)    _modelSetting.Value    = string.Empty;
            }
        }
    }
}
