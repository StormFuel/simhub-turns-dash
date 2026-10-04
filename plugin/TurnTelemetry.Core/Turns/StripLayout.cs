using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TurnTelemetry.Core.Turns
{
    /// <summary>
    /// Where the turns strip starts. The sim's lap (and its sectors) start at its timing line, which isn't always just
    /// before Turn 1: ACC Silverstone's line is on the National straight, so the lap runs 9 … 18, 1 … 8. The strip is
    /// rotated so it reads Turn 1 → max left to right (user decision 2026-10-03); the car marker starts wherever the
    /// lap starts and wraps from the right edge back to Turn 1. Lap and sector timing are unchanged.
    /// </summary>
    public static class StripLayout
    {
        private static readonly Regex TurnOne = new Regex(@"^\s*(?:t|turn\s*)?0*1\s*[a-z]?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Lap position where the strip begins: halfway between the corner before Turn 1 and Turn 1, or 0 (the timing
        /// line) when there's no Turn 1 or the line already lies in that gap, so most tracks are unchanged.
        /// </summary>
        public static double Origin(IReadOnlyList<TurnDefinition> turns)
        {
            if (turns == null || turns.Count == 0) return 0;
            var turnOne = turns.Where(t => t.Label != null && TurnOne.IsMatch(t.Label))
                .OrderBy(t => t.Label.Trim().Length).ThenBy(t => t.Start).FirstOrDefault();
            if (turnOne == null) return 0;

            var ordered = turns.OrderBy(t => t.Start).ToList();
            var index = ordered.IndexOf(turnOne);
            var previous = ordered[(index - 1 + ordered.Count) % ordered.Count];
            var gap = ordered.Count == 1 ? 0.02 : Wrap(turnOne.Start - previous.End);
            // The timing line already sits between the previous corner and Turn 1: keep the strip as the lap runs.
            if (ordered.Count > 1 && Wrap(0 - previous.End) <= gap) return 0;
            return Wrap(turnOne.Start - gap / 2);
        }

        /// <summary>A lap position on the strip (0..1 from its left edge); -1 stays -1 (no data).</summary>
        public static double ToStrip(double lapPos, double origin) => lapPos < 0 ? -1 : Wrap(lapPos - origin);

        /// <summary>The end of a span on the strip: an end that lands exactly on the strip's left edge is its right edge.</summary>
        public static double EndToStrip(double lapPos, double origin)
        {
            if (lapPos < 0) return -1;
            var end = Wrap(lapPos - origin);
            return end < 1e-9 ? 1 : end;
        }

        /// <summary>Where to centre a span's label: its middle, or the middle of its larger piece when it wraps.</summary>
        public static double LabelCentre(double start, double end)
        {
            if (start < 0 || end < 0) return -1;
            if (end >= start) return (start + end) / 2;
            return 1 - start >= end ? (start + 1) / 2 : end / 2;
        }

        private static double Wrap(double x) => ((x % 1) + 1) % 1;
    }
}
