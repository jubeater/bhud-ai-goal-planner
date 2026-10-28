namespace jubeater.aigoalplanner.Core
{
#if DEBUG
    /// <summary>
    /// The gates the developer channel runs behind. The first gate is compile time: this code only
    /// exists in Debug builds, so a release <c>.bhm</c> holds no channel at all. The other two are
    /// checked on every poll: Blish HUD started with <c>--debug</c>, and the module setting
    /// <c>Developer channel (for testing only)</c> on. With any gate missing nothing is read from
    /// the inbox, and <see cref="Describe"/> says what is needed.
    /// </summary>
    public static class DevGate
    {
        /// <summary>
        /// Whether the channel may run: Blish HUD started with <c>--debug</c> and the Player turned
        /// the setting on. The module reads the flag from its own command line and hands it in.
        /// </summary>
        public static bool IsOpen(bool blishDebug, bool settingOn) {
            return blishDebug && settingOn;
        }

        /// <summary>
        /// What the setting shows the Player: whether the channel is open, and which gates are still
        /// missing when it is not. It names all three gates, so the setting explains what is needed
        /// even when the flag is what is missing.
        /// </summary>
        public static string Describe(bool blishDebug, bool settingOn) {
            if (IsOpen(blishDebug, settingOn)) {
                return "Developer channel open: request files in dev\\inbox run against this Chat (for testing only).";
            }

            if (!settingOn) {
                return "Developer channel closed: turn this setting on to run request files in dev\\inbox against this Chat. "
                     + "Blish HUD must be started with --debug (scripts\\dev-run.ps1 does this), "
                     + "and Release builds have no developer channel.";
            }

            return "Developer channel closed: start Blish HUD with --debug (scripts\\dev-run.ps1 does this) to run "
                 + "request files in dev\\inbox against this Chat, with this setting on. "
                 + "Release builds have no developer channel.";
        }
    }
#endif
}
