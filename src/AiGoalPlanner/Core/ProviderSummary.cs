namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// The one line under the Chat's input row that says which Provider the Chat will talk to, so a
    /// missing or mistyped endpoint is visible before the Player gets an error back.
    /// </summary>
    /// <remarks>
    /// This reports the same Settings the Provider client uses; it never reports the key itself,
    /// only whether one is stored. What counts as a usable Provider is decided by
    /// <see cref="ProviderSettings.Parse"/> rather than repeated here, so the summary and the
    /// client cannot disagree.
    /// </remarks>
    public static class ProviderSummary
    {
        public static string Describe(string endpoint, string model, bool hasKey) {
            ProviderSettings settings;

            try {
                settings = ProviderSettings.Parse(endpoint, model, string.Empty);
            } catch (ProviderException exception) when (exception.Failure == ProviderFailure.NotConfigured) {
                return "No Provider yet. Click Provider settings to connect one.";
            } catch (ProviderException) {
                return "The endpoint URL is not valid. Check it in Provider settings.";
            }

            return "Using " + settings.Model + " at " + settings.EndpointUri.Authority
                 + (hasKey ? " with an API key." : " without an API key.");
        }
    }
}
