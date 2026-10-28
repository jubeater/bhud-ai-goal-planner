using System;
using System.Globalization;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// What the Provider settings' two price fields show. Prices belong to the endpoint in the
    /// window: whenever that endpoint changes — picked from the list or typed — both fields reload
    /// from what is saved for it (empty when nothing is), whatever they held. The fields always
    /// describe the endpoint they belong to, so saving can never write one Provider's prices to
    /// another. What the Player types stays until the endpoint changes.
    /// </summary>
    public sealed class TokenPriceFields
    {
        public string InputText { get; private set; } = string.Empty;

        public string OutputText { get; private set; } = string.Empty;

        /// <summary>
        /// Reloads both fields from what is saved for <paramref name="endpoint"/>, replacing
        /// whatever they held — including prices the Player typed but never saved for another
        /// endpoint.
        /// </summary>
        public void EndpointChanged(string endpoint, Func<string, TokenPrice> readPrice) {
            TokenPrice price = readPrice?.Invoke(endpoint);

            InputText  = FormatPrice(price?.InputPerMillion);
            OutputText = FormatPrice(price?.OutputPerMillion);
        }

        /// <summary>
        /// Remembers what the Player typed, which stays until the endpoint changes.
        /// </summary>
        public void Edited(string inputText, string outputText) {
            InputText  = inputText ?? string.Empty;
            OutputText = outputText ?? string.Empty;
        }

        private static string FormatPrice(decimal? price) {
            return price?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }
}
