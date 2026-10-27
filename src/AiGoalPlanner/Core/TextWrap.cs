using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// Breaks Chat text into lines that fit a width, so a message can be drawn entire in the message
    /// list.
    /// </summary>
    /// <remarks>
    /// The Chat wraps its own text instead of using <c>Label.WrapText</c>: Blish HUD's wrapping
    /// measures every word on its own, and each of those measurements comes out a little short of
    /// the line that is then drawn, so a line packed against the width is drawn wider than the label
    /// and loses its last characters. This measures the whole candidate line, and breaks a word that
    /// is wider than a line by itself - a link, a path, or text written without spaces - so no
    /// character is hidden. The measuring is handed in, so the wrapping is testable without Blish
    /// HUD's fonts.
    /// </remarks>
    public static class TextWrap
    {
        /// <summary>
        /// Returns <paramref name="text"/> with a line break wherever a line would be wider than
        /// <paramref name="width"/>. Line breaks that are already in the text are kept.
        /// </summary>
        /// <param name="measure">How wide a string is drawn, in the same units as <paramref name="width"/>.</param>
        public static string Wrap(string text, Func<string, float> measure, int width) {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (measure == null) throw new ArgumentNullException(nameof(measure));

            var wrapped = new StringBuilder();
            var line    = new StringBuilder();

            bool firstParagraph = true;

            foreach (string paragraph in text.Split('\n')) {
                if (!firstParagraph) wrapped.Append('\n');

                firstParagraph = false;

                foreach (string word in paragraph.Split(' ')) {
                    if (line.Length == 0) {
                        AppendWord(line, word, measure, width, wrapped);

                        continue;
                    }

                    if (measure(line.ToString() + " " + word) <= width) {
                        line.Append(' ').Append(word);

                        continue;
                    }

                    // The word does not fit after what is on the line, so the line is done.
                    wrapped.Append(line).Append('\n');

                    line.Clear();

                    AppendWord(line, word, measure, width, wrapped);
                }

                wrapped.Append(line);

                line.Clear();
            }

            return wrapped.ToString();
        }

        /// <summary>
        /// Puts a word on the empty line it starts, ending the line early when the word has to be
        /// broken to fit.
        /// </summary>
        private static void AppendWord(StringBuilder      line,
                                       string             word,
                                       Func<string, float> measure,
                                       int                width,
                                       StringBuilder      wrapped) {
            bool firstPiece = true;

            foreach (string piece in Fit(word, measure, width)) {
                // Every piece but the first is too wide to join the piece before it.
                if (!firstPiece) wrapped.Append(line).Append('\n');

                line.Clear().Append(piece);

                firstPiece = false;
            }
        }

        /// <summary>
        /// The pieces a word is drawn as: the word itself when it fits a line, otherwise as many
        /// pieces as it takes, cut between text elements so a surrogate pair (an emoji) or a letter
        /// with its accents is never split.
        /// </summary>
        /// <remarks>
        /// A piece always holds at least one text element, even when that element is wider than
        /// <paramref name="width"/> on its own, so a word always produces a piece and the caller
        /// always makes progress.
        /// </remarks>
        private static IEnumerable<string> Fit(string word, Func<string, float> measure, int width) {
            if (word.Length == 0 || measure(word) <= width) {
                yield return word;

                yield break;
            }

            var piece = new StringBuilder();

            foreach (string element in TextElements(word)) {
                if (piece.Length > 0 && measure(piece.ToString() + element) > width) {
                    yield return piece.ToString();

                    piece.Clear();
                }

                piece.Append(element);
            }

            if (piece.Length > 0) yield return piece.ToString();
        }

        /// <summary>
        /// The characters of <paramref name="word"/> as a reader sees them, which keeps a surrogate
        /// pair and a letter with its combining marks together.
        /// </summary>
        private static IEnumerable<string> TextElements(string word) {
            TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(word);

            while (elements.MoveNext()) {
                yield return (string)elements.Current;
            }
        }
    }
}
