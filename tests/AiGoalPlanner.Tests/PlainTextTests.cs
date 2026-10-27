using jubeater.aigoalplanner.Core;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    /// <summary>
    /// Tests stripping Markdown markup from AI replies so they draw cleanly as plain text in the Chat window.
    /// </summary>
    public class PlainTextTests
    {
        [Theory]
        [InlineData("bold", "**bold**")]
        [InlineData("bold", "__bold__")]
        [InlineData("Level 80:", "**Level 80:**")]
        [InlineData("\"Teh's Trails - Hero Points\"", "**\"Teh's Trails - Hero Points\"**")]
        [InlineData("first and second", "**first** and **second**")]
        [InlineData("first and second", "__first__ and __second__")]
        public void Bold_markup_is_stripped_to_plain_text(string expected, string input) {
            Assert.Equal(expected, PlainText.Strip(input));
        }

        [Fact]
        public void Bold_italic_with_triple_markers_is_stripped() {
            Assert.Equal("bold italic", PlainText.Strip("***bold italic***"));
            Assert.Equal("bold italic", PlainText.Strip("___bold italic___"));
        }

        [Fact]
        public void Nested_bold_and_italic_markers_are_both_stripped() {
            Assert.Equal("a b c", PlainText.Strip("**a *b* c**"));
        }

        [Fact]
        public void Unbalanced_markers_are_left_alone() {
            Assert.Equal("**x", PlainText.Strip("**x"));
            Assert.Equal("*x", PlainText.Strip("*x"));
            Assert.Equal("_x", PlainText.Strip("_x"));
        }

        [Theory]
        [InlineData("italic", "*italic*")]
        [InlineData("italic", "_italic_")]
        [InlineData("italic with spaces", "*italic with spaces*")]
        [InlineData("italic with spaces", "_italic with spaces_")]
        [InlineData("first and second", "*first* and *second*")]
        [InlineData("first and second", "_first_ and _second_")]
        public void Italic_markup_is_stripped_to_plain_text(string expected, string input) {
            Assert.Equal(expected, PlainText.Strip(input));
        }

        [Theory]
        [InlineData("snake_case")]
        [InlineData("get_module_settings")]
        [InlineData("list_installed_modules")]
        [InlineData("user_password_hash")]
        [InlineData("Hero_Challenge_points")]
        [InlineData("first_word and second_word")]
        public void Snake_case_words_are_not_broken_by_italic_stripping(string word) {
            Assert.Equal(word, PlainText.Strip(word));
        }

        [Fact]
        public void Italics_around_a_snake_case_word_are_stripped_while_keeping_the_identifier() {
            Assert.Equal("magic_find", PlainText.Strip("*magic_find*"));
            Assert.Equal("magic_find", PlainText.Strip("_magic_find_"));
        }

        [Theory]
        [InlineData("5 * 4 = 20")]
        [InlineData("pointer *ptr")]
        [InlineData("Use * as wildcard")]
        [InlineData("a * b * c")]
        public void A_lone_asterisk_is_not_broken_by_italic_stripping(string text) {
            Assert.Equal(text, PlainText.Strip(text));
        }

        [Theory]
        [InlineData("2*3*4")]
        [InlineData("x*y and a*b")]
        public void Asterisks_between_characters_are_not_eaten_as_italics(string text) {
            Assert.Equal(text, PlainText.Strip(text));
        }

        [Fact]
        public void Underscores_in_file_names_are_not_eaten() {
            Assert.Equal("__init__.py", PlainText.Strip("__init__.py"));
        }

        [Fact]
        public void Underscores_in_bare_urls_are_not_eaten() {
            Assert.Equal("https://x.com/_private_/y", PlainText.Strip("https://x.com/_private_/y"));
        }

        [Theory]
        [InlineData("code", "`code`")]
        [InlineData("get_module_settings", "`get_module_settings`")]
        [InlineData("Call func() now", "Call `func()` now")]
        [InlineData("first and second", "`first` and `second`")]
        public void Inline_code_backticks_are_stripped(string expected, string input) {
            Assert.Equal(expected, PlainText.Strip(input));
        }

        [Fact]
        public void Inline_code_content_is_preserved_exactly_without_further_emphasis_stripping() {
            Assert.Equal("a*b*c", PlainText.Strip("`a*b*c`"));
            Assert.Equal("__init__", PlainText.Strip("`__init__`"));
        }

        [Fact]
        public void Fenced_code_blocks_drop_the_fence_lines_and_keep_the_content() {
            string input = "Here is code:\n```csharp\nvar x = 1;\nvar y = 2;\n```\nDone.";
            string expected = "Here is code:\nvar x = 1;\nvar y = 2;\nDone.";

            Assert.Equal(expected, PlainText.Strip(input));
        }

        [Fact]
        public void Fenced_code_blocks_without_language_tag_drop_fence_lines() {
            string input = "```\nint a = 42;\n```";
            string expected = "int a = 42;";

            Assert.Equal(expected, PlainText.Strip(input));
        }

        [Theory]
        [InlineData("Heading 1", "# Heading 1")]
        [InlineData("Heading 2", "## Heading 2")]
        [InlineData("Subheading", "### Subheading")]
        [InlineData("Indented Heading", "   # Indented Heading")]
        [InlineData("Heading with closing", "## Heading with closing ##")]
        public void Leading_heading_markers_are_stripped_to_the_heading_text(string expected, string input) {
            Assert.Equal(expected, PlainText.Strip(input));
        }

        [Fact]
        public void Issue_numbers_at_the_start_of_a_line_are_not_treated_as_headings() {
            Assert.Equal("#21 is the issue", PlainText.Strip("#21 is the issue"));
        }

        [Fact]
        public void Tags_and_issue_numbers_are_left_alone() {
            Assert.Equal("#tag and #21 left alone", PlainText.Strip("#tag and #21 left alone"));
        }

        [Theory]
        [InlineData("Verdant Brink", "[Verdant Brink](https://wiki.guildwars2.com/wiki/Verdant_Brink)")]
        [InlineData("See this link for details.", "See [this link](https://example.com) for details.")]
        [InlineData("first and second", "[first](http://a) and [second](http://b)")]
        public void Links_keep_only_the_link_text(string expected, string input) {
            Assert.Equal(expected, PlainText.Strip(input));
        }

        [Fact]
        public void Links_with_balanced_parentheses_in_url_keep_only_link_text() {
            Assert.Equal("wiki", PlainText.Strip("[wiki](https://wiki.guildwars2.com/wiki/Foo_(bar))"));
        }

        [Fact]
        public void Images_are_converted_to_alt_text() {
            Assert.Equal("alt", PlainText.Strip("![alt](https://example.com/image.png)"));
            Assert.Equal("alt", PlainText.Strip("![alt](https://example.com/image_(1).png)"));
        }

        [Theory]
        [InlineData("- Already dash", "- Already dash")]
        [InlineData("- Asterisk marker", "* Asterisk marker")]
        [InlineData("- Plus marker", "+ Plus marker")]
        [InlineData("  - Indented asterisk", "  * Indented asterisk")]
        [InlineData("  - Indented plus", "  + Indented plus")]
        [InlineData("  - Indented dash", "  - Indented dash")]
        public void List_markers_keep_dash_and_turn_asterisk_and_plus_into_dash(string expected, string input) {
            Assert.Equal(expected, PlainText.Strip(input));
        }

        [Fact]
        public void Plus_and_asterisk_without_spaces_are_not_treated_as_list_markers() {
            Assert.Equal("+1 toughness", PlainText.Strip("+1 toughness"));
            Assert.Equal("italic", PlainText.Strip("*italic*"));
        }

        [Fact]
        public void Numbered_lists_are_preserved() {
            string input = "1. First step\n2. Second step";
            Assert.Equal(input, PlainText.Strip(input));
        }

        [Fact]
        public void Line_breaks_and_paragraphs_are_kept() {
            string input = "Paragraph 1 line 1\nParagraph 1 line 2\n\nParagraph 2\n\n- item 1\n- item 2";
            Assert.Equal(input, PlainText.Strip(input));
        }

        [Fact]
        public void Windows_line_breaks_are_kept() {
            string input = "Line 1\r\nLine 2\r\n\r\nLine 3";
            Assert.Equal(input, PlainText.Strip(input));
        }

        [Fact]
        public void A_reply_that_mixes_all_markup_rules_is_stripped_cleanly() {
            string input =
                "# Quest Guide\n"
              + "\n"
              + "Here is what you need to do:\n"
              + "- **Level 80:** Go to [Verdant Brink](https://wiki.guildwars2.com/wiki/Verdant_Brink) and talk to the scout.\n"
              + "* Use `get_module_settings` to check your current config.\n"
              + "+ Remember that *magic_find* is not the only stat; your `base_power` matters!\n"
              + "\n"
              + "```csharp\n"
              + "var plan = submit_plan();\n"
              + "```";

            string expected =
                "Quest Guide\n"
              + "\n"
              + "Here is what you need to do:\n"
              + "- Level 80: Go to Verdant Brink and talk to the scout.\n"
              + "- Use get_module_settings to check your current config.\n"
              + "- Remember that magic_find is not the only stat; your base_power matters!\n"
              + "\n"
              + "var plan = submit_plan();";

            Assert.Equal(expected, PlainText.Strip(input));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Null_or_empty_text_returns_empty(string input) {
            Assert.Equal(string.Empty, PlainText.Strip(input));
        }
    }
}
