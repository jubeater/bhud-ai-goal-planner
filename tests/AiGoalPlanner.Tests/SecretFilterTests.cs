using jubeater.aigoalplanner.Core;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    /// <summary>
    /// Tests how a Setting is recognized as a secret by its name. A secret that reaches the model
    /// cannot be taken back, so the filter may hide a harmless Setting, but it may never let one
    /// that looks like a key, token or password through.
    /// </summary>
    public class SecretFilterTests
    {
        [Theory]
        [InlineData("ApiKey",              null)]
        [InlineData("api_key",             null)]
        [InlineData("APIKey",              null)]
        [InlineData("MyAPIKey",            null)]
        [InlineData("myapikey",            null)]
        [InlineData("userauthtoken",       null)]
        [InlineData("api-key",             null)]
        [InlineData("ApiKey2",             null)]
        [InlineData("API_KEY2",            null)]
        [InlineData("apikey2",             null)]
        [InlineData("Token2",              null)]
        [InlineData("Password2",           null)]
        [InlineData("authToken_3",         null)]
        [InlineData("AuthToken",           null)]
        [InlineData("token",               null)]
        [InlineData("AccessToken",         null)]
        [InlineData("clientSecret",        null)]
        [InlineData("map_password",        null)]
        [InlineData("credentials",         null)]
        [InlineData("weird_key_name",      "Paste your API key here")]
        [InlineData("settings",            "Password for the map service")]
        [InlineData("OAuth",               null)]
        public void A_Setting_named_like_a_key_token_or_password_is_a_secret(string key, string displayName) {
            Assert.True(SecretFilter.LooksLikeSecret(key, displayName));
        }

        [Theory]
        [InlineData("ShowTrails",            null)]
        [InlineData("CategoryFontSize",      null)]
        [InlineData("Hotkey",                null)]
        [InlineData("HotkeyToggleMarkers",   null)]
        [InlineData("keybind",               null)]
        [InlineData("UseKeybindings",        null)]
        [InlineData("MarkerMultiplier",      null)]
        [InlineData("author",                null)]
        [InlineData("AuthenticationTimeout", "How long to wait for the map server")]
        public void A_Setting_that_merely_contains_letters_of_secret_words_is_not_a_secret(string key, string displayName) {
            Assert.False(SecretFilter.LooksLikeSecret(key, displayName));
        }

        [Fact]
        public void A_secret_is_recognized_from_the_display_name_alone() {
            Assert.True(SecretFilter.LooksLikeSecret("gateway", "Access token for the gateway"));
        }

        [Fact]
        public void A_Setting_without_a_name_is_not_a_secret() {
            Assert.False(SecretFilter.LooksLikeSecret(null, null));
            Assert.False(SecretFilter.LooksLikeSecret("", null));
        }
    }
}
