using System;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// The fixed system prompt the Chat sends ahead of the Player's words: which product the model
    /// is part of, what it may talk about, that it only proposes changes and never makes them, that
    /// it answers from the Player's real setup in the Player's language, and that it writes in
    /// plain text.
    /// </summary>
    /// <remarks>
    /// The prompt is fixed text so the Lookups and the rules stay the same for every Provider and
    /// every model; only the identity paragraph names the model and the host, built from the same
    /// Settings the Provider client uses, so a change of endpoint or model is named correctly from
    /// the next Request on. It is kept out of <see cref="Chat.Messages"/>: the Player reads the
    /// conversation, not the instructions.
    /// </remarks>
    public static class SystemPrompt
    {
        /// <summary>
        /// The one message the Chat puts first on every Request.
        /// </summary>
        public static string For(string model, string endpoint) {
            return Identity(model, endpoint) + "\n\n"
                 + Scope + "\n\n"
                 + AdvisoryOnly + "\n\n"
                 + LookupsAndLanguage + "\n\n"
                 + PlainTextFormat;
        }

        /// <summary>
        /// Who the model is and where it runs, so it does not introduce itself as some other
        /// assistant the Player has heard of.
        /// </summary>
        private static string Identity(string model, string endpoint) {
            return "You are AI Goal Planner, an assistant inside the Blish HUD module of the same name. "
                 + WhereItRuns(model, endpoint)
                 + " Don't claim to be ChatGPT or any other product.";
        }

        /// <summary>
        /// What the model may talk about: Blish HUD configuration first, Guild Wars 2 and Blish HUD
        /// questions as short advice, everything else declined.
        /// </summary>
        private const string Scope =
            "You help the Player configure Blish HUD: the modules they have installed and their settings. "
          + "Questions about Guild Wars 2 or Blish HUD itself get a short answer as advice; say when you "
          + "are not sure. Anything that is not about configuring Blish HUD or playing Guild Wars 2: "
          + "politely decline and say what you can help with instead.";

        /// <summary>
        /// The one rule the model must not get wrong: proposing is all it can do. A model that
        /// claims it has enabled a module sends the Player away believing something happened that
        /// did not.
        /// </summary>
        private const string AdvisoryOnly =
            "You never change anything yourself. You can only propose changes, and you deliver them "
          + "with the submit_plan tool, which proposes a plan for the Player to review and apply. "
          + "Nothing has happened until the Player applies it. Never say that you installed, enabled, "
          + "disabled or changed anything: say \"I suggest…\", \"You could…\", or \"Review the changes "
          + "below…\" instead.";

        /// <summary>
        /// How the model answers: from the Player's real setup, in the language the Player used.
        /// </summary>
        private const string LookupsAndLanguage =
            "Look things up before you answer. Call list_installed_modules to see which modules the "
          + "Player really has, and get_module_settings to read one module's settings; prefer what "
          + "the tools say over guessing. If a module is disabled, tell the Player to enable it "
          + "first. Reply in the language the Player wrote in.";

        /// <summary>
        /// How the model formats replies: plain text so the Chat window stays readable.
        /// </summary>
        private const string PlainTextFormat =
            "Write in plain text: short paragraphs and simple \"- \" lists. Do not use bold, "
          + "headings, tables or code fences.";

        private static string WhereItRuns(string model, string endpoint) {
            string modelName = (model ?? string.Empty).Trim();
            string host      = HostOf(endpoint);

            // The Chat only gets here once ProviderSettings has accepted both, so the missing model
            // is a guard rather than a state the Player can reach.
            return modelName.Length == 0
                       ? "You are running via " + host + "."
                       : "You are running on model " + modelName + " via " + host + ".";
        }

        /// <summary>
        /// The host the Player connected, taken the way <see cref="ProviderSummary"/> takes it: the
        /// authority, so a Provider on the Player's own machine keeps its port. Anything that is not
        /// an http or https URL never reaches a Request, and is described rather than echoed.
        /// </summary>
        private static string HostOf(string endpoint) {
            string trimmedEndpoint = (endpoint ?? string.Empty).Trim();

            if (Uri.TryCreate(trimmedEndpoint, UriKind.Absolute, out Uri endpointUri)
             && (endpointUri.Scheme == Uri.UriSchemeHttp || endpointUri.Scheme == Uri.UriSchemeHttps)) {
                return endpointUri.Authority;
            }

            return "your Provider";
        }
    }
}
