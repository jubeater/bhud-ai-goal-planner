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
            "In a plan, the actions are install, which names a module from search_catalog to install from "
          + "the catalog; enable_module or disable_module, which name a module by its namespace; set_setting, "
          + "which names the module, the setting key and the new value as text, written the way get_module_settings "
          + "showed it; download_pack, which names a marker pack from list_marker_packs to download; and advice, "
          + "for what you cannot do yourself. Every action carries a reason the Player can read, and is_side_effect, "
          + "which is false only when the Player asked for that change: send it every time. Leave out the fields a "
          + "kind does not use: a module action has no setting key, and an advice has no module. Use Installed "
          + "Modules first, including enabling disabled ones; only propose an install from the catalog when no "
          + "Installed Module can do the job, and name the specific Gap it fills: the missing ability no installed "
          + "module provides. Never propose an install whose function overlaps with an installed module or one "
          + "already in the plan; judge overlap from module descriptions checked with get_module_details. If an installed module looks abandoned, "
          + "mention it only as advice, never as an install. Every install action must name the gap. A disabled "
          + "module's settings cannot be read, so propose enabling it on its own, and configure it once the Player "
          + "has applied that. After installing a module, configure its settings in the continuation, and if it "
          + "requested GW2 API permissions, tell the Player to confirm them in that module's settings and restart "
          + "Blish HUD. After downloading a marker pack, use find_marker_categories in the continuation to find "
          + "relevant marker categories and advise the Player which category display path to turn on in Pathing. If "
          + "a downloaded pack appears in skipped_packs, tell the Player plainly that the pack's category list "
          + "couldn't be read, and that the Player can find it in Pathing's menu under the pack's name; do not "
          + "suggest waiting for indexing or restarting Pathing.";
    }
}
