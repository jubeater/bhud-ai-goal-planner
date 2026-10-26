using System;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// What the Player enters in the Provider settings: the endpoint, the model and, if the
    /// Provider needs one, the API key.
    /// </summary>
    /// <remarks>
    /// Settings are checked here, once, so the settings view and the Provider client cannot
    /// disagree about what a usable Provider is. The key is kept out of <see cref="ToString"/>
    /// because that is what ends up in a log.
    /// </remarks>
    public sealed class ProviderSettings
    {
        private ProviderSettings(string endpoint, Uri endpointUri, string model, string apiKey) {
            Endpoint    = endpoint;
            EndpointUri = endpointUri;
            Model       = model;
            ApiKey      = apiKey;
        }

        /// <summary>
        /// The endpoint exactly as the Player typed it, without surrounding whitespace.
        /// </summary>
        public string Endpoint { get; }

        /// <summary>
        /// The endpoint as a URL, already checked to be absolute http or https.
        /// </summary>
        public Uri EndpointUri { get; }

        public string Model { get; }

        /// <summary>
        /// The API key, or an empty string when the Provider does not need one. Never logged.
        /// </summary>
        public string ApiKey { get; }

        /// <summary>
        /// Reads an endpoint on its own, for the actions that change the key without touching the
        /// rest of the Settings.
        /// </summary>
        public static Uri ParseEndpoint(string endpoint) {
            string trimmedEndpoint = (endpoint ?? string.Empty).Trim();

            if (trimmedEndpoint.Length == 0) {
                throw new ProviderException(ProviderFailure.NotConfigured);
            }

            if (!Uri.TryCreate(trimmedEndpoint, UriKind.Absolute, out Uri endpointUri)
             || (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps)) {
                throw new ProviderException(ProviderFailure.InvalidEndpoint);
            }

            return endpointUri;
        }

        /// <summary>
        /// Reads what the Player typed, throwing a <see cref="ProviderException"/> that says what
        /// is wrong with it.
        /// </summary>
        public static ProviderSettings Parse(string endpoint, string model, string apiKey) {
            string trimmedEndpoint = (endpoint ?? string.Empty).Trim();
            string trimmedModel    = (model    ?? string.Empty).Trim();
            string trimmedKey      = (apiKey   ?? string.Empty).Trim();

            if (trimmedEndpoint.Length == 0 || trimmedModel.Length == 0) {
                throw new ProviderException(ProviderFailure.NotConfigured);
            }

            Uri endpointUri = ParseEndpoint(trimmedEndpoint);

            return new ProviderSettings(trimmedEndpoint, endpointUri, trimmedModel, trimmedKey);
        }

        public override string ToString() {
            return Endpoint + " (" + Model + ")";
        }
    }
}
