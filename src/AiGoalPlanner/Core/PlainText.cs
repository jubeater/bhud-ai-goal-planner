using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// Strips common Markdown markup from AI replies so they draw cleanly as plain text in the Chat
    /// window.
    /// </summary>
    /// <remarks>
    /// Two choices are deliberate:
    ///
    /// Emphasis markers are only recognized at word boundaries — after the start of a line, whitespace
    /// or punctuation, and before the end of a line, whitespace or punctuation — so snake_case names,
    /// __init__ and multiplication like 2*3 survive untouched.
    ///
    /// Fenced code blocks drop the fence lines (``` or ```csharp) but keep the content inside, so
    /// code and structured text remain readable in plain text without raw fence markers.
    /// </remarks>
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
            new Regex(@"!?\[([^\]\r\n]*)\]\(\s*(?:[^()\s]+|\([^()\s]*\))*(?:\s+[""'][^""']*[""'])?\s*\)", RegexOptions.Compiled);

        private static readonly Regex BareUrlRegex =
            new Regex(@"https?://\S+", RegexOptions.Compiled);

        private static readonly Regex InlineCodeRegex =
            new Regex(@"`([^`\r\n]+)`", RegexOptions.Compiled);

        private static readonly Regex EscapeRegex =
            new Regex(@"\\([\\_*`#\[\]()\-+.\!|{}<>~""])", RegexOptions.Compiled);

        private static readonly Regex BoldItalicAsteriskRegex =
            new Regex(@"(?<![\p{L}\p{N}\*])\*{3}(?!\s|\*)([^\r\n]+?)(?<!\s|\*)\*{3}(?=$|\s|[^\p{L}\p{N}\*](?:[^\p{L}\p{N}]|$))", RegexOptions.Compiled);

        private static readonly Regex BoldItalicUnderscoreRegex =
            new Regex(@"(?<![\p{L}\p{N}_])_{3}(?!\s|_)([^\r\n]+?)(?<!\s|_)_{3}(?=$|\s|[^\p{L}\p{N}_](?:[^\p{L}\p{N}]|$))", RegexOptions.Compiled);

        private static readonly Regex BoldAsteriskRegex =
            new Regex(@"(?<![\p{L}\p{N}\*])\*{2}(?!\s|\*)([^\r\n]+?)(?<!\s|\*)\*{2}(?=$|\s|[^\p{L}\p{N}\*](?:[^\p{L}\p{N}]|$))", RegexOptions.Compiled);

        private static readonly Regex BoldUnderscoreRegex =
            new Regex(@"(?<![\p{L}\p{N}_])_{2}(?!\s|_)([^\r\n]+?)(?<!\s|_)_{2}(?=$|\s|[^\p{L}\p{N}_](?:[^\p{L}\p{N}]|$))", RegexOptions.Compiled);

        private static readonly Regex ItalicAsteriskRegex =
            new Regex(@"(?<![\p{L}\p{N}\*])\*(?!\s|\*)([^\r\n*]+?)(?<!\s|\*)\*(?=$|\s|[^\p{L}\p{N}\*](?:[^\p{L}\p{N}]|$))", RegexOptions.Compiled);

        private static readonly Regex ItalicUnderscoreRegex =
            new Regex(@"(?<![\p{L}\p{N}_])_(?!\s|_)([^\r\n]+?)(?<!\s|_)_(?=$|\s|[^\p{L}\p{N}_](?:[^\p{L}\p{N}]|$))", RegexOptions.Compiled);

        /// <summary>
        /// Strips common Markdown markup from text, preserving line breaks and list structure.
        /// </summary>
        public static string Strip(string markdown) {
            if (string.IsNullOrEmpty(markdown)) return string.Empty;

            string newline = markdown.Contains("\r\n") ? "\r\n" : "\n";
            string[] rawLines = markdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            var processedLines = new List<string>();
            bool inFencedBlock = false;

            foreach (string rawLine in rawLines) {
                // Drop fenced code block lines (``` or ```csharp etc.).
                if (CodeFenceRegex.IsMatch(rawLine)) {
                    inFencedBlock = !inFencedBlock;
                    continue;
                }

                if (inFencedBlock) {
                    processedLines.Add(rawLine);
                    continue;
                }

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

        private static string StripHeading(string line) {
            if (!line.TrimStart().StartsWith("#")) return line;

            string stripped = HeadingStartRegex.Replace(line, string.Empty);
            return HeadingEndRegex.Replace(stripped, string.Empty);
        }

        private static string StripInline(string text) {
            var codeSpans = new List<string>();
            text = InlineCodeRegex.Replace(text, match => {
                string token = "\u0000CODE" + codeSpans.Count + "\u0000";
                codeSpans.Add(match.Groups[1].Value);
                return token;
            });

            var escapes = new List<string>();
            text = EscapeRegex.Replace(text, match => {
                string token = "\u0000ESC" + escapes.Count + "\u0000";
                escapes.Add(match.Groups[1].Value);
                return token;
            });

            text = LinkRegex.Replace(text, "$1");

            var bareUrls = new List<string>();
            text = BareUrlRegex.Replace(text, match => {
                string token = "\u0000URL" + bareUrls.Count + "\u0000";
                bareUrls.Add(match.Value);
                return token;
            });

            text = BoldItalicAsteriskRegex.Replace(text, "$1");
            text = BoldItalicUnderscoreRegex.Replace(text, "$1");

            text = BoldAsteriskRegex.Replace(text, "$1");
            text = BoldUnderscoreRegex.Replace(text, "$1");

            text = ItalicAsteriskRegex.Replace(text, "$1");
            text = ItalicUnderscoreRegex.Replace(text, "$1");

            for (int i = 0; i < bareUrls.Count; i++) {
                text = text.Replace("\u0000URL" + i + "\u0000", bareUrls[i]);
            }

            for (int i = 0; i < escapes.Count; i++) {
                text = text.Replace("\u0000ESC" + i + "\u0000", escapes[i]);
            }

            for (int i = 0; i < codeSpans.Count; i++) {
                text = text.Replace("\u0000CODE" + i + "\u0000", codeSpans[i]);
            }

            return text;
        }
    }
}
