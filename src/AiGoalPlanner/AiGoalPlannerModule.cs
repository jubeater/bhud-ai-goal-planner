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

            _chat = null;

            Logger.Info("AI Goal Planner unloaded.");
        }

        private void CreateCornerIcon() {
            _cornerIcon = new CornerIcon {
                Icon             = this.ContentsManager.GetTexture("icon.png"),
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
                this.ContentsManager.GetTexture("icon.png"));
        }

        private void OnCornerIconClicked(object sender, MouseEventArgs e) {
            _chatWindow?.Toggle();
        }
    }
}
