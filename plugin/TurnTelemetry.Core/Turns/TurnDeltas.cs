using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TurnTelemetry.Core.Lap;

namespace TurnTelemetry.Core.Turns
{
    public enum TurnDeltaState
    {
        None,
        /// <summary>As fast as or faster than your best through this corner (green).</summary>
        Gain,
        /// <summary>Slower than your best through this corner (yellow).</summary>
        Loss,
    }

    /// <summary>
    /// Time gained or lost in each corner, the way engineers split a lap in MoTeC or Pi. The lap is partitioned into one
    /// segment per turn (from midway after the previous corner to midway before the next; the first and last turns reach
    /// the timing line), so nothing is missed between corners. Each corner is compared with your best time through that corner on
    /// any lap where you stayed inside track limits in it: an engineer's theoretical best (user decision 2026-10-03), so
    /// a cut elsewhere on the lap doesn't throw away the clean corners. A corner with an excursion (or the pit lane) gets
    /// no colour and doesn't set a best. A corner's delta is known once the car leaves its segment; corners not reached
    /// yet this lap keep last lap's result.
    /// </summary>
    public sealed class TurnDeltas
    {
        /// <summary>A segment taking longer than this (s) is a stop (pits, a long off): it never becomes a best.</summary>
        public const double MaxSegmentSeconds = 120;
        /// <summary>Deltas this big (s) or a stop are shown as "&gt;1m" rather than a number (user request 2026-10-03).</summary>
        public const double LongDelta = 60;
        /// <summary>How far past a segment's end (lap fraction) still counts as "just left it".</summary>
        private const double CompleteWindow = 0.25;

        private IReadOnlyList<TurnDefinition> _turns;
        private double[] _start = new double[0], _end = new double[0];
        private double[] _delta = new double[0], _lastDelta = new double[0], _best = new double[0];
        private bool[] _done = new bool[0], _started = new bool[0], _dirty = new bool[0];
        private int[] _incidentsAtStart = new int[0];
        private LapTrace _lap, _previousLap;
        /// <summary>For the segment that ends at the line: was it clean when the lap ended (its time settles later)?</summary>
        private bool _previousLastClean;

        public int Count => _delta.Length;

        public void Reset()
        {
            _turns = null;
            _start = _end = _delta = _lastDelta = _best = new double[0];
            _done = _started = _dirty = new bool[0];
            _incidentsAtStart = new int[0];
            _lap = _previousLap = null;
        }

        /// <summary>Delta (s, negative = gained) for turn <paramref name="i"/> (index in the turn list), this lap or last lap.</summary>
        public double Delta(int i) => i < 0 || i >= Count ? double.NaN : !double.IsNaN(_delta[i]) ? _delta[i] : _lastDelta[i];
        public bool FromLastLap(int i) => i >= 0 && i < Count && double.IsNaN(_delta[i]) && !double.IsNaN(_lastDelta[i]);

        /// <summary>Your best clean time through turn <paramref name="i"/>'s segment (s), or NaN.</summary>
        public double Best(int i) => i >= 0 && i < Count ? _best[i] : double.NaN;

        public TurnDeltaState State(int i)
        {
            var d = Delta(i);
            return double.IsNaN(d) ? TurnDeltaState.None : d <= 0 ? TurnDeltaState.Gain : TurnDeltaState.Loss;
        }

        /// <summary>"+0.11" under 10 s, "+12.3" under a minute, "&gt;1m" for a minute or more lost (a stop, a long off).</summary>
        public string Text(int i)
        {
            var d = Delta(i);
            if (double.IsNaN(d)) return "";
            var a = Math.Abs(d);
            if (a >= LongDelta) return d > 0 ? ">1m" : "-1m+";
            return (d <= 0 ? "-" : "+") + a.ToString(a < 9.995 ? "0.00" : "0.0", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// For narrow bands the sign and the digits are stacked on two lines (user request 2026-10-03), so the digits can
        /// use the band's full width: ".11" under a second, "1.7" under ten, "12" beyond.
        /// </summary>
        public string ShortDigits(int i)
        {
            var d = Delta(i);
            if (double.IsNaN(d)) return "";
            var a = Math.Abs(d);
            if (a >= LongDelta) return "1m";
            if (a < 0.995) return a.ToString(".00", CultureInfo.InvariantCulture);
            if (a < 9.95) return a.ToString("0.0", CultureInfo.InvariantCulture);
            return Math.Round(a).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The line above the digits on narrow bands: "+" / "-", or "&gt;" over "1m" for a minute or more lost.</summary>
        public string Sign(int i)
        {
            var d = Delta(i);
            return double.IsNaN(d) ? "" : d >= LongDelta ? ">" : d <= 0 ? "-" : "+";
        }

        /// <summary>Segment of turn <paramref name="i"/> as lap positions (NaN for turns that cross the line).</summary>
        public double SegmentStart(int i) => i >= 0 && i < _start.Length ? _start[i] : double.NaN;
        public double SegmentEnd(int i) => i >= 0 && i < _end.Length ? _end[i] : double.NaN;

        /// <param name="incidents">Running count of track-limit excursions (TrackLimits.Incidents).</param>
        public void Update(IReadOnlyList<TurnDefinition> turns, LapTrace lap, double lapPos, int incidents = 0, bool inPit = false)
        {
            if (!ReferenceEquals(turns, _turns)) Segments(turns);
            if (lap == null || Count == 0) return;

            if (!ReferenceEquals(lap, _lap))
            {
                if (_lap != null)
                {
                    var last = LastSegment();
                    _previousLastClean = last >= 0 && _started[last] && !_dirty[last] && incidents == _incidentsAtStart[last];
                    _previousLap = _lap;
                    _lastDelta = (double[])_delta.Clone();
                }
                for (var i = 0; i < Count; i++)
                {
                    _delta[i] = double.NaN;
                    _done[i] = _started[i] = _dirty[i] = false;
                }
                _lap = lap;
            }

            for (var i = 0; i < Count; i++)
            {
                if (double.IsNaN(_end[i]) || _done[i]) continue;
                // Entering the segment: remember the excursion count so an excursion inside it can be told apart.
                if (!_started[i] && lapPos >= _start[i] && lapPos < _end[i])
                {
                    _started[i] = true;
                    _incidentsAtStart[i] = incidents;
                }
                if (_started[i] && inPit && lapPos >= _start[i] && lapPos < _end[i]) _dirty[i] = true;
                if (_end[i] < 1 && _started[i] && lapPos >= _end[i] && lapPos < _end[i] + CompleteWindow)
                {
                    var clean = !_dirty[i] && incidents == _incidentsAtStart[i];
                    _delta[i] = Rate(i, Span(i, _lap), clean);
                    _done[i] = true;
                }
            }

            // The turn whose segment ends at the line: rated once the finished lap's time has settled.
            if (_previousLap != null && _previousLap.LapTimeSeconds > 0)
            {
                var last = LastSegment();
                if (last >= 0) _lastDelta[last] = Rate(last, Span(last, _previousLap), _previousLastClean);
                _previousLap = null;
            }
        }

        /// <summary>Delta against the best clean time so far (NaN for the first clean pass or a dirty one); updates the best.</summary>
        private double Rate(int i, double time, bool clean)
        {
            if (double.IsNaN(time) || !clean) return double.NaN;
            var delta = double.IsNaN(_best[i]) ? double.NaN : time - _best[i];
            // A stop (pits, a long off) is shown as a loss but never becomes the corner's best.
            if (time < MaxSegmentSeconds && (double.IsNaN(_best[i]) || time < _best[i])) _best[i] = time;
            return delta;
        }

        private int LastSegment()
        {
            for (var i = 0; i < Count; i++)
                if (_end[i] >= 1) return i;
            return -1;
        }

        private double Span(int i, LapTrace lap)
        {
            var d = TimeAt(lap, _end[i]) - TimeAt(lap, _start[i]);
            return d > 0 ? d : double.NaN;
        }

        private static double TimeAt(LapTrace lap, double pos)
        {
            if (pos <= 0) return 0;
            if (pos >= 1) return lap.LapTimeSeconds > 0 ? lap.LapTimeSeconds : double.NaN;
            return lap.EntryTime(lap.BinOf(pos));
        }

        private void Segments(IReadOnlyList<TurnDefinition> turns)
        {
            _turns = turns;
            var n = turns?.Count ?? 0;
            _start = new double[n];
            _end = new double[n];
            _delta = new double[n];
            _lastDelta = new double[n];
            _best = new double[n];
            _done = new bool[n];
            _started = new bool[n];
            _dirty = new bool[n];
            _incidentsAtStart = new int[n];
            _lap = _previousLap = null;
            for (var i = 0; i < n; i++) _start[i] = _end[i] = _delta[i] = _lastDelta[i] = _best[i] = double.NaN;
            if (n == 0) return;

            // Turns that cross the line are left out; the stretch around the line belongs to the first and last turns.
            var ordered = Enumerable.Range(0, n).Where(i => turns[i].Start <= turns[i].End)
                .OrderBy(i => turns[i].Start).ToList();
            for (var k = 0; k < ordered.Count; k++)
            {
                var t = turns[ordered[k]];
                _start[ordered[k]] = k == 0 ? 0 : (turns[ordered[k - 1]].End + t.Start) / 2;
                _end[ordered[k]] = k == ordered.Count - 1 ? 1 : (t.End + turns[ordered[k + 1]].Start) / 2;
            }
        }
    }
}
