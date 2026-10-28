namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// The part of the System Prompt that tells the model how a Plan is shaped, so a model that has
    /// never seen this module's tools still proposes Actions the Review can carry out.
    /// </summary>
    public static partial class SystemPrompt
    {
        /// <summary>
        /// The shape of a Plan: the kinds of Action there are, what each of them names, and that every
        /// Action says why. A model that is not told which kinds exist invents one, and the Review has
        /// to refuse it.
        /// </summary>
        private const string HowToPropose =
            "In a plan, the actions are enable_module or disable_module, which name a module by its "
          + "namespace; set_setting, which names the module, the setting key and the new value as text, "
          + "written the way get_module_settings showed it; download_pack, which names a marker pack from list_marker_packs to download; and advice, for what you cannot do yourself. "
          + "Every action carries a reason the Player can read, and is_side_effect, which is false only "
          + "when the Player asked for that change: send it every time. Leave out the fields a kind "
          + "does not use: a module action has no setting key, and an advice has no module. A disabled "
          + "module's settings cannot be read, so propose enabling it on its own, and configure it once "
          + "the Player has applied that. After downloading a marker pack, use find_marker_categories in the continuation to find relevant marker categories and advise the Player which category display path to turn on in Pathing.";
    }
}
