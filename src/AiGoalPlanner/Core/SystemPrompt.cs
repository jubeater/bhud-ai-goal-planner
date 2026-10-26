using System;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// The message the Chat sends ahead of the Player's words, so the model knows which product it is
    /// part of instead of introducing itself as some other assistant.
    /// </summary>
    /// <remarks>
    /// This is the identity message, not the whole system prompt: the planning instructions and the
    /// Lookups arrive with the ticket that adds them. It is built from the same Settings the Provider
    /// client uses, so it names the model and the host the Chat actually talks to, and a change of
    /// endpoint or model is named correctly from the next Request on. It is kept out of
    /// <see cref="Chat.Messages"/>: the Player reads the conversation, not the instructions.
    /// </remarks>
    public static class SystemPrompt
    {
        /// <summary>
        /// The one message the Chat puts first on every Request.
        /// </summary>
        public static string For(string model, string endpoint) {
            return "You are AI Goal Planner, an assistant inside the Blish HUD module of the same name. "
                 + WhereItRuns(model, endpoint)
                 + " Don't claim to be ChatGPT or any other product.";
        }

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
