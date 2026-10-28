using System;
using System.Collections.Generic;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// How Preset buttons share their row in the Chat window: the pure widths, so the window
    /// stays thin and the policy is tested. Each preset keeps its natural width while the row
    /// fits; one that would overflow takes what is left while that is still a usable button, and
    /// only what cannot be shown usefully is left out — so a preset added as data still shows up.
    /// </summary>
    public static class PresetLayout
    {
        public static IReadOnlyList<int> FitWidths(IReadOnlyList<int> naturalWidths, int availableWidth, int gap, int minWidth) {
            var widths = new List<int>();

            if (naturalWidths == null) return widths;

            int left = availableWidth;

            foreach (int naturalWidth in naturalWidths) {
                if (widths.Count > 0) left -= gap;

                if (left < minWidth) break;

                widths.Add(Math.Min(naturalWidth, left));
                left -= widths[widths.Count - 1];
            }

            return widths;
        }
    }
}
