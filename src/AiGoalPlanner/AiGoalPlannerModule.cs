using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Blish_HUD.Modules;
using Blish_HUD.Modules.Managers;
using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.UI;
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
        /// The module's own icon, loaded once from <c>ref/</c> and shared by the corner icon and the
        /// Chat window. Content loaded from <c>ref/</c> belongs to the module, so <see cref="Unload"/>
        /// disposes it after the controls that draw it are gone.
        /// </summary>
        private Texture2D _iconTexture;

        [ImportingConstructor]
        public AiGoalPlannerModule([Import("ModuleParameters")] ModuleParameters moduleParameters) : base(moduleParameters) { /* NOOP */ }

        protected override void Initialize() {
            _chat = new Chat();
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
                _iconTexture);
        }

        private void OnCornerIconClicked(object sender, MouseEventArgs e) {
            _chatWindow?.Toggle();
        }
    }
}
