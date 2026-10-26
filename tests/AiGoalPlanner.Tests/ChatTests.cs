using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    /// <summary>
    /// Tests the seam the rest of the module is built on: the Chat. Everything here is driven the
    /// way a Player drives it and checked the way a Player would read it back.
    /// </summary>
    public class ChatTests
    {
        [Fact]
        public async Task Sending_a_Request_shows_it_and_an_answer_in_the_Chat() {
            var chat = new Chat();

            await chat.SendAsync("I want to finish the hero points on this map");

            Assert.Collection(chat.Messages,
                              request => {
                                  Assert.Equal(ChatMessageAuthor.Player, request.Author);
                                  Assert.Equal("I want to finish the hero points on this map", request.Text);
                              },
                              answer => {
                                  Assert.Equal(ChatMessageAuthor.Ai, answer.Author);
                                  Assert.Equal("I want to finish the hero points on this map", answer.Text);
                              });
        }

        [Fact]
        public async Task A_second_Request_keeps_the_rest_of_the_Chat() {
            var chat = new Chat();

            await chat.SendAsync("Get mastery points");
            await chat.SendAsync("Leave Mounts alone");

            Assert.Equal(4, chat.Messages.Count);
            Assert.Equal("Get mastery points",    chat.Messages[0].Text);
            Assert.Equal("Leave Mounts alone",    chat.Messages[2].Text);
            Assert.Equal(ChatMessageAuthor.Player, chat.Messages[2].Author);
        }

        [Fact]
        public async Task New_chat_clears_what_the_Player_said_before() {
            var chat = new Chat();
            await chat.SendAsync("Show only world bosses in the event table");

            chat.StartNewChat();

            Assert.Empty(chat.Messages);
        }

        [Fact]
        public async Task A_Request_after_New_chat_starts_a_fresh_Chat() {
            var chat = new Chat();
            await chat.SendAsync("Finish the hero points on this map");

            chat.StartNewChat();
            await chat.SendAsync("Level an alt");

            Assert.Equal(2, chat.Messages.Count);
            Assert.Equal(ChatMessageAuthor.Player, chat.Messages[0].Author);
            Assert.Equal("Level an alt",           chat.Messages[0].Text);
        }

        [Fact]
        public async Task Sending_nothing_adds_nothing_to_the_Chat() {
            var chat = new Chat();

            await chat.SendAsync(string.Empty);
            await chat.SendAsync("   ");

            Assert.Empty(chat.Messages);
        }

        [Fact]
        public async Task A_Request_is_shown_without_surrounding_whitespace() {
            var chat = new Chat();

            await chat.SendAsync("  Get mastery points  ");

            Assert.Equal("Get mastery points", chat.Messages[0].Text);
        }
    }
}
