using Xunit;
using jubeater.aigoalplanner.Core;

namespace jubeater.aigoalplanner.Tests
{
#if DEBUG
    /// <summary>
    /// Tests the developer channel's gates: it runs only in a Debug build (which is where this code
    /// exists at all), started with --debug, and with the setting on. With any gate missing nothing
    /// is read, and the setting says what is needed.
    /// </summary>
    public class DevGateTests
    {
        [Fact]
        public void Open_when_blish_started_with_debug_and_the_setting_is_on() {
            Assert.True(DevGate.IsOpen(blishDebug: true, settingOn: true));
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public void Closed_when_any_gate_is_missing(bool blishDebug, bool settingOn) {
            Assert.False(DevGate.IsOpen(blishDebug, settingOn));
        }

        [Fact]
        public void The_setting_names_the_debug_flag_when_blish_started_without_it() {
            string text = DevGate.Describe(blishDebug: false, settingOn: true);

            Assert.Contains("--debug", text);
        }

        [Fact]
        public void The_setting_names_itself_when_it_is_off() {
            string text = DevGate.Describe(blishDebug: true, settingOn: false);

            Assert.Contains("turn this setting on", text);
        }

        [Fact]
        public void The_setting_says_all_three_gates_even_when_closed() {
            foreach (bool settingOn in new[] { true, false }) {
                string text = DevGate.Describe(blishDebug: false, settingOn: settingOn);

                Assert.Contains("--debug", text);
                Assert.Contains("setting", text);
                Assert.Contains("Release", text);
            }
        }

        [Fact]
        public void The_setting_says_open_when_both_gates_are_met() {
            string text = DevGate.Describe(blishDebug: true, settingOn: true);

            Assert.Contains("open", text);
        }
    }
#endif
}
