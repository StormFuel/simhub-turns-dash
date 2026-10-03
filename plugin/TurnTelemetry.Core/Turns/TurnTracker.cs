using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TurnTelemetry.Core.Turns
{
    public sealed class TurnDefinition
    {
        public string Label;
        public string Name;
        /// <summary>Lap fraction 0..1. Start greater than End means the turn wraps across start/finish.</summary>
        public double Start;
        public double End;

        public bool Contains(double progress) =>
            Start <= End ? progress >= Start && progress < End : progress >= Start || progress < End;
    }

    public static class TurnLabel
    {
        private static readonly Regex Pattern = new Regex(
            @"^(?:turn|corner|t)\s*[-#]?\s*(\d+)\s*([a-z]?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Extracts a stable turn number from an explicitly numbered label, mirroring TurnState.parseTurnLabel
        /// from the Assetto Corsa Turns app: "Turn 7", "Turn 7A", "T7", "T7 - The Kink", "Corner 7".
        /// Name-only labels return null; turn numbers are never guessed.
        /// </summary>
        public static string Parse(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return null;
            var match = Pattern.Match(label.Trim());
            if (!match.Success) return null;
            var suffix = match.Groups[2].Value;
            // A letter that starts a word ("Turn 7 Abbey") is a name, not a 7A suffix.
            var rest = label.Trim().Substring(match.Length);
            if (rest.Length > 0 && char.IsLetter(rest[0])) suffix = string.Empty;
            return int.Parse(match.Groups[1].Value).ToString() + suffix.ToUpperInvariant();
        }
    }

    public sealed class TurnState
    {
        public static readonly TurnState Empty = new TurnState(null, null, null);

        public TurnState(TurnDefinition last, TurnDefinition current, TurnDefinition next)
        {
            Last = last;
            Current = current;
            Next = next;
        }

        public TurnDefinition Last { get; }
        public TurnDefinition Current { get; }
        public TurnDefinition Next { get; }
    }

    /// <summary>
    /// Stateless last/current/next lookup: port of TurnState.atProgress. Recomputed from position every frame,
    /// so it recovers after teleports and restarts without event history.
    /// </summary>
    public static class TurnTracker
    {
        public static TurnState At(IReadOnlyList<TurnDefinition> turns, double progress)
        {
            if (turns == null || turns.Count == 0 || progress < 0 || double.IsNaN(progress)) return TurnState.Empty;
            progress %= 1.0;

            var currentIndex = -1;
            for (var i = 0; i < turns.Count; i++)
            {
                if (turns[i].Contains(progress))
                {
                    currentIndex = i;
                    break;
                }
            }

            int lastIndex = -1, nextIndex = -1;
            double lastDistance = double.MaxValue, nextDistance = double.MaxValue;
            var skipCurrent = turns.Count > 1;

            for (var i = 0; i < turns.Count; i++)
            {
                if (skipCurrent && i == currentIndex) continue;

                var behind = Mod1(progress - turns[i].End);
                if (behind < lastDistance)
                {
                    lastDistance = behind;
                    lastIndex = i;
                }

                var ahead = Mod1(turns[i].Start - progress);
                if (ahead < nextDistance)
                {
                    nextDistance = ahead;
                    nextIndex = i;
                }
            }

            return new TurnState(
                lastIndex >= 0 ? turns[lastIndex] : null,
                currentIndex >= 0 ? turns[currentIndex] : null,
                nextIndex >= 0 ? turns[nextIndex] : null);
        }

        // Lua's % is floored (always non-negative for a positive divisor); C#'s is truncated.
        private static double Mod1(double v)
        {
            var r = v % 1.0;
            return r < 0 ? r + 1.0 : r;
        }
    }
}
