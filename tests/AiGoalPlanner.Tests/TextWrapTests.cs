using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using jubeater.aigoalplanner.Core;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    /// <summary>
    /// Tests how Chat text is broken into lines that fit the message list: every line has to fit the
    /// width it is drawn in, and no character may be lost, added or moved. The measuring is a fake
    /// font - 7 units per character and 3 for a space - so the wrapping is checked without Blish HUD
    /// fonts and without the game running.
    /// </summary>
    public class TextWrapTests
    {
        /// <summary>
        /// Wide enough for the ordinary sentences here, so only the cases that are about long words
        /// split one.
        /// </summary>
        private const int Width = 300;

        private const string LongReply =
            "AI: I can help with Blish HUD configuration or Guild Wars 2 advice, but not with anything outside the game.";

        private const string Hint =
            "Nothing here yet. Ask for what you want to do in Guild Wars 2, then press Send.";

        private const string LongUrl =
            "https://wiki.guildwars2.com/wiki/Hero_point#List_of_hero_points";

        [Fact]
        public void An_empty_text_wraps_to_nothing() {
            Assert.Equal(string.Empty, AssertWraps(string.Empty, Width));
        }

        [Fact]
        public void A_short_message_is_left_as_it_is() {
            Assert.Equal("AI: hello", AssertWraps("AI: hello", Width));
        }

        [Fact]
        public void A_line_that_is_exactly_as_wide_as_the_wrap_is_not_broken() {
            var word = new string('x', 42);

            Assert.Equal(word, AssertWraps(word, (int)Measure(word)));
        }

        [Fact]
        public void A_long_reply_breaks_between_words() {
            string wrapped = AssertWraps(LongReply, Width);

            Assert.True(Lines(wrapped).Count() > 1);
            Assert.DoesNotContain("\n ", wrapped);
            Assert.DoesNotContain(" \n", wrapped);
        }

        [Fact]
        public void Hard_line_breaks_are_kept() {
            const string text = "first line\nsecond line\n\nlast line";

            Assert.Equal(text, AssertWraps(text, 1000));
        }

        [Fact]
        public void A_blank_line_survives_when_the_lines_around_it_have_to_wrap() {
            Assert.Equal("abc\ndef\n\nghi", AssertWraps("abc def\n\nghi", 30));
        }

        [Fact]
        public void Double_leading_and_trailing_spaces_do_not_add_words() {
            const string text = "  a  b   c ";

            string wrapped = AssertWraps(text, Width);

            Assert.Equal(new[] { "a", "b", "c" }, Words(wrapped));
        }

        [Fact]
        public void A_long_text_wraps_on_every_line() {
            string text = string.Join(" ", Enumerable.Repeat("word", 200));

            Assert.True(Lines(AssertWraps(text, Width)).Count() > 1);
        }

        [Fact]
        public void The_empty_hint_wraps_inside_the_message_list() {
            string wrapped = AssertWraps(Hint, Width);

            Assert.True(Lines(wrapped).Count() > 1);
        }

        [Fact]
        public void A_long_url_is_broken_instead_of_running_past_the_edge() {
            string wrapped = AssertWraps(LongUrl, Width);

            Assert.True(Lines(wrapped).Count() > 1);
            Assert.DoesNotContain(" ", wrapped);
        }

        [Fact]
        public void Text_written_without_spaces_is_broken() {
            const string text = "这是一段没有空格的中文文本，用来检查没有空格的文字也会折行";

            string wrapped = AssertWraps(text, 100);

            Assert.True(Lines(wrapped).Count() > 1);
            Assert.DoesNotContain(" ", wrapped);
        }

        [Fact]
        public void An_emoji_is_never_split_between_lines() {
            const int narrow = 10;

            string wrapped = AssertWraps("😀😀😀", narrow);

            Assert.Equal(new[] { "😀", "😀", "😀" }, Lines(wrapped));
        }

        [Fact]
        public void An_emoji_is_kept_whole_where_a_line_ends_before_it() {
            const int narrow = 10;

            string wrapped = AssertWraps("a😀b", narrow);

            Assert.Equal(new[] { "a", "😀", "b" }, Lines(wrapped));
        }

        [Fact]
        public void One_character_wider_than_the_wrap_is_still_drawn_whole() {
            Assert.Equal("😀", AssertWraps("😀", 1));
        }

        [Fact]
        public void A_wrap_narrower_than_one_character_holds_one_character_per_line() {
            string wrapped = AssertWraps("abcdef", 3);

            Assert.Equal(new[] { "a", "b", "c", "d", "e", "f" }, Lines(wrapped));
        }

        [Fact]
        public void A_word_too_long_for_a_line_is_broken_in_the_middle_of_a_sentence() {
            const string text = "The wiki page is at " + LongUrl + " for details.";

            string wrapped = AssertWraps(text, Width);

            Assert.StartsWith("The wiki page is at", wrapped);
            Assert.EndsWith("for details.", wrapped);
            Assert.DoesNotContain(LongUrl, wrapped);
        }

        /// <summary>
        /// Asserts what every wrap owes the Chat: no line is wider than the width unless it holds a
        /// single text element that is wider on its own, and no character is lost, added or moved.
        /// When every word fits a line by itself, no word may be split either.
        /// </summary>
        /// <returns>The wrapped text, for the caller's own checks.</returns>
        private static string AssertWraps(string text, int width) {
            string wrapped = TextWrap.Wrap(text, Measure, width);

            foreach (string line in Lines(wrapped)) {
                if (Measure(line) <= width) continue;

                Assert.True(TextElements(line).Count() == 1,
                            "A line is " + Measure(line) + " wide in a " + width + " wide wrap: \"" + line + "\"");
            }

            Assert.Equal(Squash(text), Squash(wrapped));

            if (Words(text).All(word => Measure(word) <= width)) {
                Assert.Equal(Words(text), Words(wrapped));
            }

            return wrapped;
        }

        /// <summary>
        /// The fake font: 7 units per UTF-16 character and 3 for a space.
        /// </summary>
        private static float Measure(string text) {
            return text.Sum(c => c == ' ' ? 3f : 7f);
        }

        private static IEnumerable<string> Lines(string text) {
            return text.Split('\n');
        }

        private static IEnumerable<string> Words(string text) {
            return text.Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// The text with every space and line break taken out, which is what has to survive a wrap.
        /// </summary>
        private static string Squash(string text) {
            return new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
        }

        private static IEnumerable<string> TextElements(string text) {
            TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(text);

            while (elements.MoveNext()) {
                yield return (string)elements.Current;
            }
        }
    }
}
