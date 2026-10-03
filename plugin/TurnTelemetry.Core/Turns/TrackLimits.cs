using System;
using System.Collections.Generic;
using System.Linq;
using TurnTelemetry.Core.Lap;

namespace TurnTelemetry.Core.Turns
{
    /// <summary>
    /// Counts track-limit excursions per turn, this lap and this session. An excursion is the sim's tyres-out count
    /// reaching its limit (AC: 3 tyres, ACC: all 4), or, for sims that don't report tyres out (and as a safety net),
    /// the lap being invalidated. Each one is pinned to the nearest turn.
    /// </summary>
    public sealed class TrackLimits
    {
        /// <summary>Back on track for this long before another excursion counts (kerb hopping isn't several).</summary>
        public const double RearmSeconds = 1.0;
        /// <summary>The sim may flag the lap at the line as it resets; ignore invalidations this early in a lap.</summary>
        public const double InvalidationGraceSeconds = 1.0;
        /// <summary>An invalidation this soon after a tyres-out excursion is the same incident.</summary>
        private const double SameIncidentSeconds = 3.0;

        private readonly Dictionary<TurnDefinition, int> _lap = new Dictionary<TurnDefinition, int>();
        private readonly Dictionary<TurnDefinition, int> _session = new Dictionary<TurnDefinition, int>();
        private IReadOnlyList<TurnDefinition> _turns;
        private LapTrace _currentLap;
        private bool _out;
        private double _backOnSince = double.NegativeInfinity;
        private double _lastExcursion = double.NegativeInfinity;
        private bool _wasInvalid;

        public int SessionTotal { get; private set; }
        /// <summary>Every excursion since the session started (even with no turn data); sectors use it to tell clean from not.</summary>
        public int Incidents { get; private set; }
        public string Source { get; private set; } = "none";
        public TurnDefinition LastTurn { get; private set; }

        public int ThisLap(TurnDefinition turn) => turn != null && _lap.TryGetValue(turn, out var n) ? n : 0;
        public int ThisSession(TurnDefinition turn) => turn != null && _session.TryGetValue(turn, out var n) ? n : 0;

        public void Reset()
        {
            _lap.Clear();
            _session.Clear();
            _turns = null;
            _currentLap = null;
            _out = false;
            _wasInvalid = false;
            _backOnSince = _lastExcursion = double.NegativeInfinity;
            SessionTotal = 0;
            Incidents = 0;
            Source = "none";
            LastTurn = null;
        }

        /// <param name="tyresOut">Tyres outside the limits, or null when the sim doesn't report it.</param>
        /// <param name="limit">Tyres out that make an excursion (0: not applicable).</param>
        /// <param name="lapTime">Seconds into the current lap.</param>
        /// <param name="now">Monotonic seconds.</param>
        public void Update(LapTrace lap, IReadOnlyList<TurnDefinition> turns, double lapPos, int? tyresOut, int limit,
            bool lapInvalidated, double lapTime, double now)
        {
            if (!ReferenceEquals(turns, _turns))
            {
                // New turn data: counts belong to the old turns.
                _lap.Clear();
                _session.Clear();
                SessionTotal = 0;
                LastTurn = null;
                _turns = turns;
            }
            if (!ReferenceEquals(lap, _currentLap))
            {
                _lap.Clear();
                _currentLap = lap;
                _wasInvalid = false;
            }

            if (tyresOut.HasValue && limit > 0)
            {
                var off = tyresOut.Value >= limit;
                if (off && !_out && now - _backOnSince >= RearmSeconds)
                    Record(lapPos, now, "tyres out");
                if (!off && _out) _backOnSince = now;
                _out = off;
            }

            var rising = lapInvalidated && !_wasInvalid;
            _wasInvalid = lapInvalidated;
            if (rising && lapTime > InvalidationGraceSeconds && now - _lastExcursion > SameIncidentSeconds)
                Record(lapPos, now, "lap invalidated");
        }

        private void Record(double lapPos, double now, string source)
        {
            _lastExcursion = now;
            Incidents++;
            var turn = Nearest(_turns, lapPos);
            if (turn == null) return;
            _lap[turn] = ThisLap(turn) + 1;
            _session[turn] = ThisSession(turn) + 1;
            SessionTotal++;
            LastTurn = turn;
            Source = source;
        }

        /// <summary>The turn containing <paramref name="lapPos"/>, or the closest one (around the lap) otherwise.</summary>
        public static TurnDefinition Nearest(IReadOnlyList<TurnDefinition> turns, double lapPos)
        {
            if (turns == null || turns.Count == 0) return null;
            return turns.OrderBy(t => Distance(t, lapPos)).First();
        }

        private static double Distance(TurnDefinition t, double p)
        {
            if (t.Contains(p)) return 0;
            return Math.Min(Around(t.Start, p), Around(t.End, p));
        }

        private static double Around(double a, double b)
        {
            var d = Math.Abs(a - b);
            return Math.Min(d, 1 - d);
        }
    }
}
