using System;
using System.ComponentModel.Composition;
using System.Net.Http;
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
        private ProviderKeyStore     _keyStore;
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

            _keyStore  = new ProviderKeyStore(DirectoriesManager.GetFullDirectoryPath("aigoalplanner"));
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            _chat = new Chat(CreateChatClient);
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
        }

        /// <summary>
        /// Builds the Provider client for the current Settings. A change in Settings takes effect on
        /// the next Request; the Chat is not rebuilt, so its history stays.
        /// </summary>
        private IChatClient CreateChatClient() {
            string endpoint = _endpointSetting.Value;

            ProviderSettings settings = ProviderSettings.Parse(endpoint, _modelSetting.Value, _keyStore?.Load(endpoint));

            return new OpenAiCompatibleChatClient(settings, _httpClient);
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
                    endpoint => _keyStore.HasKey(endpoint),
                    SaveProviderSettings,
                    ForgetProviderKey,
                    NotifyProviderChanged);
            }

            _providerWindow.Show();
        }

        /// <summary>
        /// Keeps what the Player saved: endpoint and model go into Settings, the key goes into the
        /// key store. A blank key leaves a stored key alone, so re-saving the same Provider does not
        /// silently disconnect it; removing a key is what the Forget key button is for.
        /// </summary>
        private void SaveProviderSettings(string endpoint, string model, string apiKey) {
            ProviderSettings settings = ProviderSettings.Parse(endpoint, model, apiKey);

            _endpointSetting.Value = settings.Endpoint;
            _modelSetting.Value    = settings.Model;

            if (settings.ApiKey.Length > 0) {
                _keyStore.Save(settings.Endpoint, settings.ApiKey);
            }
        }

        private void ForgetProviderKey(string endpoint) {
            _keyStore.Remove(endpoint);
        }

        private void NotifyProviderChanged() {
            _chatWindow?.RefreshProvider();
        }

        private string DescribeProvider() {
            string endpoint = _endpointSetting.Value;

            return ProviderSummary.Describe(endpoint, _modelSetting.Value, _keyStore.HasKey(endpoint));
        }
    }
}
