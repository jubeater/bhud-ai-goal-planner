using System;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// The kinds of failure the Provider layer can report, so the Chat can explain them to the
    /// Player without knowing anything about HTTP status codes.
    /// </summary>
    public enum ProviderFailure
    {
        /// <summary>The Player has not connected a Provider yet.</summary>
        NotConfigured,

        /// <summary>The endpoint is not an absolute http or https URL.</summary>
        InvalidEndpoint,

        /// <summary>The Provider rejected the API key.</summary>
        InvalidKey,

        /// <summary>The Provider does not have the model the Player named.</summary>
        UnknownModel,

        /// <summary>The Provider is rate limiting requests.</summary>
        RateLimited,

        /// <summary>The Provider could not be reached over the network.</summary>
        Network,

        /// <summary>The Provider failed in a way the Chat has no specific explanation for.</summary>
        Unexpected
    }

    /// <summary>
    /// A failure to talk to the Provider, with enough detail for the Chat to explain it and for
    /// the log to show what happened. It never carries the API key or request headers.
    /// </summary>
    public sealed class ProviderException : Exception
    {
        public ProviderException(ProviderFailure failure) : base(DefaultMessage(failure)) {
            Failure = failure;
        }

        public ProviderException(ProviderFailure failure, string message) : base(message) {
            Failure = failure;
        }

        public ProviderException(ProviderFailure failure, string message, Exception innerException) : base(message, innerException) {
            Failure = failure;
        }

        public ProviderFailure Failure { get; }

        private static string DefaultMessage(ProviderFailure failure) {
            switch (failure) {
                case ProviderFailure.NotConfigured:  return "No Provider is configured.";
                case ProviderFailure.InvalidEndpoint: return "The Provider endpoint is not valid.";
                case ProviderFailure.InvalidKey:      return "The Provider rejected the API key.";
                case ProviderFailure.UnknownModel:    return "The Provider does not have that model.";
                case ProviderFailure.RateLimited:     return "The Provider is rate limiting requests.";
                case ProviderFailure.Network:         return "The Provider could not be reached.";
                default:                              return "The Provider request failed.";
            }
        }
    }
}
