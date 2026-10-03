using System.Collections.Generic;
using System.Linq;
using TurnTelemetry.Core.Turns;

namespace TurnTelemetry.Core.Engine
{
    /// <summary>
    /// Dashboard "SET LINE" (docs/plan.md D15): the driver taps it where numbering should begin, normally as they cross
    /// the start/finish line, and the next corner ahead becomes Turn 1. The renumbered turns are saved as the user's
    /// turn file. The same button undoes the change for a few seconds; RESET returns to curated / template / automatic.
    /// </summary>
    public sealed class StartLineSetter
    {
        public const double UndoSeconds = 5;
        public const double MessageSeconds = 5;

        private bool _hadUserFile;
        private string _previousUserFile;

        public string Message { get; private set; } = "";
        public double MessageUntil { get; private set; }
        public double UndoUntil { get; private set; }

        public bool MessageVisible(double now) => now < MessageUntil;
        public bool UndoAvailable(double now) => now < UndoUntil;

        public void Reset()
        {
            UndoUntil = 0;
            MessageUntil = 0;
        }

        /// <summary>
        /// Renumbers <paramref name="turns"/> (in lap order) so <paramref name="first"/> becomes Turn 1.
        /// Returns the new list, or null when nothing changes.
        /// </summary>
        public static List<TurnCandidate> Renumber(IReadOnlyList<TurnDefinition> turns, TurnDefinition first)
        {
            var ordered = turns.OrderBy(t => t.Start).ToList();
            var index = ordered.FindIndex(t => ReferenceEquals(t, first) || (t.Start == first.Start && t.End == first.End));
            if (index < 0) return null;
            var candidates = ordered.Select(t => new TurnCandidate { Label = t.Label, Name = t.Name ?? "", Start = t.Start, End = t.End }).ToList();
            var before = string.Join("|", candidates.Select(c => c.Label));
            TurnNumbering.AutoNumber(candidates, index);
            return string.Join("|", candidates.Select(c => c.Label)) == before ? null : candidates;
        }

        /// <summary>Remembers the user file as it was, so the change can be undone.</summary>
        public void BeforeSave(double now, bool hadUserFile, string previousContent)
        {
            _hadUserFile = hadUserFile;
            _previousUserFile = previousContent;
            UndoUntil = now + UndoSeconds;
        }

        public bool TryTakeUndo(double now, out bool hadUserFile, out string previousContent)
        {
            hadUserFile = _hadUserFile;
            previousContent = _previousUserFile;
            if (now >= UndoUntil) return false;
            UndoUntil = 0;
            return true;
        }

        public void Say(double now, string message, double seconds = MessageSeconds)
        {
            Message = message ?? "";
            MessageUntil = string.IsNullOrEmpty(Message) ? 0 : now + seconds;
        }
    }
}
