using System;
using System.Collections.Generic;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// How Preset buttons share their rows in the Chat window: the pure widths, so the window
    /// stays thin and the policy is tested. Each preset keeps its natural width while the row
    /// fits; one that would overflow takes what is left while that is still a usable button, and
    /// the rest wrap onto further rows — so a preset added as data can never remove an existing
    /// Preset's button.
    /// </summary>
    public static class PresetLayout
    {
        public static IReadOnlyList<IReadOnlyList<int>> FitRows(IReadOnlyList<int> naturalWidths, int availableWidth,
                                                               int gap, int minWidth) {
            var rows = new List<IReadOnlyList<int>>();

            if (naturalWidths == null) return rows;

            var row = new List<int>();
            int left = availableWidth;

            foreach (int naturalWidth in naturalWidths) {
                if (row.Count > 0) {
                    left -= gap;

                    if (left < minWidth) {
                        rows.Add(row);
                        row  = new List<int>();
                        left = availableWidth;
                    }
                }

                int width = Math.Min(naturalWidth, left);

                // The row is narrower than a usable button: a preset that starts one still shows,
                // narrowed to what there is, rather than disappearing.
                if (row.Count == 0) width = Math.Max(0, width);

                row.Add(width);
                left -= width;
            }

            if (row.Count > 0) rows.Add(row);

            return rows;
        }
    }
}
