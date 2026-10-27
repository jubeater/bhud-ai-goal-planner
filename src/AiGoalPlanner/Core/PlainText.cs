using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// Strips common Markdown markup from AI replies so they draw cleanly as plain text in the Chat
    /// window.
    /// </summary>
    public static class PlainText
    {
        private static readonly Regex CodeFenceRegex =
            new Regex(@"^[ \t]*`{3,}[^\r\n`]*$", RegexOptions.Compiled);

        private static readonly Regex HeadingStartRegex =
            new Regex(@"^[ \t]*#+(?:[ \t]+|$)", RegexOptions.Compiled);

        private static readonly Regex HeadingEndRegex =
            new Regex(@"[ \t]+#+[ \t]*$", RegexOptions.Compiled);

        private static readonly Regex ListMarkerRegex =
            new Regex(@"^([ \t]*)[*+]([ \t]+)", RegexOptions.Compiled);

        private static readonly Regex LinkRegex =
            new Regex(@"\[([^\]\r\n]+)\]\([^\)\r\n]+\)", RegexOptions.Compiled);

        private static readonly Regex InlineCodeRegex =
            new Regex(@"`([^`\r\n]+)`", RegexOptions.Compiled);

        private static readonly Regex BoldItalicAsteriskRegex =
            new Regex(@"\*{3}(?!\s|\*)([^\r\n]+?)(?<!\s|\*)\*{3}", RegexOptions.Compiled);

        private static readonly Regex BoldItalicUnderscoreRegex =
            new Regex(@"(?<![a-zA-Z0-9_])_{3}(?!\s|_)([^\r\n]+?)(?<!\s|_)_{3}(?![a-zA-Z0-9_])", RegexOptions.Compiled);

        private static readonly Regex BoldAsteriskRegex =
            new Regex(@"\*{2}(?!\s|\*)([^\r\n]+?)(?<!\s|\*)\*{2}", RegexOptions.Compiled);

        private static readonly Regex BoldUnderscoreRegex =
            new Regex(@"(?<![a-zA-Z0-9_])_{2}(?!\s|_)([^\r\n]+?)(?<!\s|_)_{2}(?![a-zA-Z0-9_])", RegexOptions.Compiled);

        private static readonly Regex ItalicAsteriskRegex =
            new Regex(@"(?<!\*)\*(?!\s|\*)([^\r\n*]+?)(?<!\s|\*)\*(?!\*)", RegexOptions.Compiled);

        private static readonly Regex ItalicUnderscoreRegex =
            new Regex(@"(?<![a-zA-Z0-9_])_(?!\s|_)([^\r\n]+?)(?<!\s|_)_(?![a-zA-Z0-9_])", RegexOptions.Compiled);

        /// <summary>
        /// Strips common Markdown markup from text, preserving line breaks and list structure.
        /// </summary>
        public static string Strip(string markdown) {
            if (string.IsNullOrEmpty(markdown)) return string.Empty;

            string newline = markdown.Contains("\r\n") ? "\r\n" : "\n";
            string[] rawLines = markdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            var processedLines = new List<string>();

            foreach (string rawLine in rawLines) {
                // Drop fenced code block lines (``` or ```csharp etc.).
                if (CodeFenceRegex.IsMatch(rawLine)) continue;

                string line = rawLine;

                // Normalize list markers (* and + become -).
                line = ListMarkerRegex.Replace(line, "$1-$2");

                // Strip leading heading markers (# Heading -> Heading).
                line = StripHeading(line);

                // Strip inline markup on the line.
                line = StripInline(line);

                processedLines.Add(line);
            }

            return string.Join(newline, processedLines);
        }

        /// <summary>
        /// Alias for <see cref="Strip"/>.
        /// </summary>
        public static string StripMarkdown(string markdown) => Strip(markdown);

        private static string StripHeading(string line) {
            if (!line.TrimStart().StartsWith("#")) return line;

            string stripped = HeadingStartRegex.Replace(line, string.Empty);
            return HeadingEndRegex.Replace(stripped, string.Empty);
        }

        private static string StripInline(string text) {
            text = LinkRegex.Replace(text, "$1");
            text = InlineCodeRegex.Replace(text, "$1");

            text = BoldItalicAsteriskRegex.Replace(text, "$1");
            text = BoldItalicUnderscoreRegex.Replace(text, "$1");

            text = BoldAsteriskRegex.Replace(text, "$1");
            text = BoldUnderscoreRegex.Replace(text, "$1");

            text = ItalicAsteriskRegex.Replace(text, "$1");
            text = ItalicUnderscoreRegex.Replace(text, "$1");

            return text;
        }
    }
}
