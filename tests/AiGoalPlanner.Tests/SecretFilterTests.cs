using jubeater.aigoalplanner.Core;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    /// <summary>
    /// Tests how a Setting is recognized as a secret by its name, one table of names and whether
    /// they hide. The filter may hide a harmless Setting, but it may never let one that looks like
    /// a key, token or password through.
    /// </summary>
    public class SecretFilterTests
    {
        [Theory]
        [InlineData(true,  "refreshtoken",    null)]
        [InlineData(true,  "userpassword",    null)]
        [InlineData(true,  "ClientSecret",    null)]
        [InlineData(true,  "bearer",          null)]
        [InlineData(true,  "myapikey",        null)]
        [InlineData(true,  "APIKey",          null)]
        [InlineData(true,  "ApiKey2",         null)]
        [InlineData(true,  "API_KEY2",        null)]
        [InlineData(true,  "Token2",          null)]
        [InlineData(true,  "authToken_3",     null)]
        [InlineData(true,  "user_pwd",        null)]
        [InlineData(true,  "Key",             null)]
        [InlineData(false, "Hotkey",          null)]
        [InlineData(false, "HotkeyToggleMarkers", null)]
        [InlineData(false, "keybind",         null)]
        [InlineData(false, "UseKeybindings",  null)]
        [InlineData(false, "Author",          null)]
        [InlineData(false, "MonkeyMode",      null)]
        public void A_Setting_is_hidden_exactly_when_its_name_reads_as_a_secret(bool hidden, string key, string displayName) {
            Assert.Equal(hidden, SecretFilter.LooksLikeSecret(key, displayName));
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
