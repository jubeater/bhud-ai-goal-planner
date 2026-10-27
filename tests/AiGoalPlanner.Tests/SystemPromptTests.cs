using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.Tests.Fakes;
using Microsoft.Extensions.AI;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    /// <summary>
    /// Tests the system prompt the Chat sends ahead of the Player's words: it says what the model
    /// is part of, where it runs, what it may talk about, that it only proposes and never changes
    /// anything, and that it answers in the Player's language.
    /// </summary>
    public class SystemPromptTests
    {
        [Fact]
        public void It_says_which_module_the_model_is_running_in() {
            string prompt = SystemPrompt.For("beam:cloud", "https://ollama.com/v1");

            Assert.StartsWith("You are AI Goal Planner, an assistant inside the Blish HUD module of the same name.",
                              prompt);
        }

        [Fact]
        public void It_names_the_model_and_the_host_the_Chat_talks_to() {
            string prompt = SystemPrompt.For("beam:cloud", "https://ollama.com/v1");

            Assert.Contains("You are running on model beam:cloud via ollama.com.", prompt);
        }

        [Fact]
        public void It_keeps_the_port_of_a_Provider_on_the_Players_own_machine() {
            string prompt = SystemPrompt.For("qwen3.5:9b", " http://localhost:11434/v1 ");

            Assert.Contains("You are running on model qwen3.5:9b via localhost:11434.", prompt);
        }

        [Fact]
        public void It_tells_the_model_not_to_claim_to_be_ChatGPT_or_another_product() {
            string prompt = SystemPrompt.For("beam:cloud", "https://ollama.com/v1");

            Assert.Contains("Don't claim to be ChatGPT or any other product.", prompt);
        }

        [Fact]
        public void It_confines_the_model_to_Blish_HUD_and_the_game() {
            string prompt = SystemPrompt.For("beam:cloud", "https://ollama.com/v1");

            Assert.Contains("Blish HUD", prompt);
            Assert.Contains("Guild Wars 2", prompt);
        }

        [Fact]
        public void It_tells_the_model_what_submit_plan_is_for() {
            string prompt = SystemPrompt.For("beam:cloud", "https://ollama.com/v1");

            // From the ticket's second comment: a real model said "I've enabled the Pathing module"
            // when it had only proposed it, so the prompt has to name the tool that proposes.
            Assert.Contains("submit_plan", prompt);
        }

        [Fact]
        public void It_names_every_Lookup_the_model_can_call() {
            var registry = Lookups.For(new FakeWorld());

            string prompt = SystemPrompt.For("beam:cloud", "https://ollama.com/v1");

            foreach (AIFunctionDeclaration lookup in registry.Declarations) {
                Assert.Contains(lookup.Name, prompt);
            }
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("", "")]
        [InlineData("   ", "   ")]
        [InlineData("deepseek-flash", "not a url")]
        public void Settings_that_were_never_filled_in_are_described_instead_of_left_as_a_gap(string model, string endpoint) {
            string prompt = SystemPrompt.For(model, endpoint);

            Assert.DoesNotContain("  ", prompt);
            Assert.DoesNotContain("..", prompt);
            Assert.Contains("via your Provider.", prompt);
        }

        [Fact]
        public void A_model_that_was_never_filled_in_is_not_named_as_a_blank() {
            string prompt = SystemPrompt.For(string.Empty, "https://ollama.com/v1");

            Assert.Contains("You are running via ollama.com.", prompt);
            Assert.DoesNotContain("model .", prompt);
        }

        [Fact]
        public void It_asks_for_plain_text_replies() {
            string prompt = SystemPrompt.For("beam:cloud", "https://ollama.com/v1");

            Assert.Contains("plain text", prompt);
            Assert.Contains("bold", prompt);
            Assert.Contains("headings", prompt);
        }
    }
}
