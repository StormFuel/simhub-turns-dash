using System;
using System.Linq;

namespace TurnTelemetry.Core.Lap
{
    /// <summary>Timing-screen rating of a sector (published upper-case, e.g. "PERSONALBEST").</summary>
    public enum PaceState
    {
        /// <summary>Not driven yet this lap (or its entry time is unknown).</summary>
        None,
        /// <summary>Track limits exceeded (or pit lane) in this sector: the time doesn't count (red).</summary>
        Invalid,
        /// <summary>Slower than your best (yellow).</summary>
        Slower,
        /// <summary>A personal best this session (green).</summary>
        PersonalBest,
        /// <summary>The fastest by anyone this session (purple).</summary>
        SessionBest,
    }

    /// <summary>
    /// Times each sector from the sim's lap timer at the points where its sector index steps up (boundaries from
    /// <see cref="SectorMap"/>) and rates it the way timing screens do: purple for the session's fastest by anyone,
    /// green for a personal best, yellow for slower, red when track limits were exceeded in it. Sectors not reached
    /// yet this lap show last lap's result.
    /// </summary>
    /// <remarks>
    /// Whether a sector counts is decided per sector, not per lap: a clean sector on a lap cut elsewhere is a real
    /// time (2026-10-03: whole-lap validity left bests unset after track-limit laps, so a slow lap rated green).
    /// </remarks>
    public sealed class SectorTimer
    {
        private const int Max = SectorMap.MaxSectors;
        /// <summary>Times within this of the session's fastest count as the fastest (sims round differently).</summary>
        private const double SameTime = 0.0005;

        private readonly double[] _ownBest = new double[Max + 1];
        private readonly double[] _gameBestSeen = new double[Max + 1];
        private readonly double[] _fastestSeen = new double[Max + 1];
        private readonly PaceState[] _current = new PaceState[Max + 1];
        private readonly PaceState[] _last = new PaceState[Max + 1];
        private readonly double[] _currentTime = new double[Max + 1];
        private readonly double[] _lastTime = new double[Max + 1];
        private int? _previousIndex;
        private double _entry = double.NaN;
        /// <summary>A clock that keeps running across the line: the sim's lap timer plus every lap it has reset from.</summary>
        private double _clockOffset;
        private double _previousLapTime = double.NaN;
        /// <summary>Lowest sector index the sim has reported (0 for sims that count from 0).</summary>
        private int _baseIndex = int.MaxValue;
        /// <summary>1-based sector being driven, counted by index steps since the line; 0 when unknown.</summary>
        private int _sector;
        /// <summary>Game-reported bests captured as the sector started, so the new time can't be compared to itself.</summary>
        private double _gameBestAtEntry = double.NaN;
        private double _fastestAtEntry = double.NaN;
        private int _incidentsAtEntry;
        private bool _pitInSector;

        /// <summary>The last rating and what it was based on, for the settings-tab diagnostics.</summary>
        public string LastRating { get; private set; } = "none yet";
        public bool HasRating => LastRating != "none yet";

        public SectorTimer()
        {
            Reset();
        }

        public void Reset()
        {
            _previousIndex = null;
            _entry = double.NaN;
            _clockOffset = 0;
            _previousLapTime = double.NaN;
            _baseIndex = int.MaxValue;
            _sector = 0;
            _gameBestAtEntry = _fastestAtEntry = double.NaN;
            _incidentsAtEntry = 0;
            _pitInSector = false;
            LastRating = "none yet";
            for (var n = 0; n <= Max; n++)
            {
                _ownBest[n] = _gameBestSeen[n] = _fastestSeen[n] = double.NaN;
                _current[n] = _last[n] = PaceState.None;
                _currentTime[n] = _lastTime[n] = double.NaN;
            }
        }

        /// <summary>This lap's rating for 1-based sector <paramref name="n"/>, or last lap's until it's completed.</summary>
        public PaceState State(int n) => _current[n] != PaceState.None ? _current[n] : _last[n];
        public bool FromLastLap(int n) => _current[n] == PaceState.None && _last[n] != PaceState.None;
        public double Time(int n) => _current[n] != PaceState.None ? _currentTime[n] : _lastTime[n];

        /// <summary>Per-sector bests (own clean timing / game / session fastest), for diagnostics.</summary>
        public string Bests(int count) => count <= 0 ? "-" : string.Join("   ", Enumerable.Range(1, Math.Min(count, Max)).Select(n =>
            $"S{n} own={Show(_ownBest[n])} game={Show(_gameBestSeen[n])} fastest={Show(_fastestSeen[n])}"));

        /// <summary>
        /// Driven by the sim's sector index: a step up starts the next sector, a step back down means the car crossed the
        /// line (the last sector ends, sector 1 starts, this lap's results become last lap's). Times come from a clock
        /// built on the sim's lap timer that keeps running across the line, so it doesn't matter whether the sim resets
        /// its lap timer, lap counter or lap position first (ACC resets the counter and timer a frame or two before the
        /// position wraps: 2026-10-03, the old lap-object approach then lost S3 and numbered the next lap's sectors from S4).
        /// The sim's timer stops while paused, so pauses don't count.
        /// </summary>
        /// <param name="sessionFastest">Fastest best time for 1-based sector n by any driver, you included (NaN when unknown).</param>
        /// <param name="gameBest">Your best time for sector n as the game reports it (NaN when unknown).</param>
        /// <param name="incidents">Running count of track-limit excursions (TrackLimits.Incidents).</param>
        public void Update(SectorMap map, double lapPos, double lapTime, int simIndex, bool discontinuity,
            Func<int, double> sessionFastest, Func<int, double> gameBest = null, int incidents = 0, bool inPit = false,
            bool newLap = false)
        {
            var now = Clock(lapTime);
            // SimHub numbers sectors from 1 for ACC and AC; a sim that reports 0 teaches us it counts from 0.
            if (simIndex < _baseIndex) _baseIndex = simIndex;
            var previous = _previousIndex;

            if (!previous.HasValue)
            {
                // First frame: at the line with a fresh timer, sector 1 can be timed; otherwise wait for the next boundary.
                var atLine = lapPos < 0.02 && lapTime < 1;
                _sector = Ordinal(simIndex);
                _entry = atLine && _sector == 1 ? now - lapTime : double.NaN;
                StartSector(sessionFastest, gameBest, incidents);
            }
            else if (discontinuity)
            {
                _entry = double.NaN;
            }
            else if (simIndex > previous)
            {
                // Next sector (a jump of more than one means a sector was missed: its time is unknown).
                var stepped = simIndex == previous + 1;
                if (stepped && _sector >= 1) Close(_sector, now, incidents);
                _sector = Ordinal(simIndex);
                _entry = stepped ? now : double.NaN;
                StartSector(sessionFastest, gameBest, incidents);
            }
            else if (simIndex < previous)
            {
                // Back to the first sector: the car crossed the line.
                if (_sector >= 1) Close(_sector, now, incidents);
                Roll();
                _sector = Ordinal(simIndex);
                _entry = now;
                StartSector(sessionFastest, gameBest, incidents);
            }
            else if (newLap && _sector == 1 && double.IsNaN(_entry))
            {
                // The lap counter started a lap but the sector index didn't move: on an ACC hotlap the out-lap already
                // sits on sector 1 (timing hasn't started), so the line shows up only as a new lap. Time S1 from here.
                Roll();
                _entry = now - lapTime;
                StartSector(sessionFastest, gameBest, incidents);
            }
            if (inPit) _pitInSector = true;
            _previousIndex = simIndex;
        }

        /// <summary>
        /// 1-based sector from the sim's index. Taken from the index itself rather than counted from where the lap
        /// started, so it can't drift (2026-10-03: counting from the car's position on an ACC hotlap out-lap, where the
        /// index sits on sector 1 until timing starts, labelled the next sectors "S4" and "S5").
        /// </summary>
        private int Ordinal(int simIndex) => Math.Max(1, Math.Min(Max, simIndex - Math.Min(_baseIndex, 1) + 1));

        private void Roll()
        {
            for (var n = 0; n <= Max; n++)
            {
                _last[n] = _current[n];
                _lastTime[n] = _currentTime[n];
                _current[n] = PaceState.None;
                _currentTime[n] = double.NaN;
            }
        }

        private double Clock(double lapTime)
        {
            if (!double.IsNaN(_previousLapTime) && lapTime < _previousLapTime - 0.5) _clockOffset += _previousLapTime;
            _previousLapTime = lapTime;
            return _clockOffset + lapTime;
        }

        private void StartSector(Func<int, double> sessionFastest, Func<int, double> gameBest, int incidents)
        {
            _fastestAtEntry = _sector >= 1 ? sessionFastest?.Invoke(_sector) ?? double.NaN : double.NaN;
            _gameBestAtEntry = _sector >= 1 ? gameBest?.Invoke(_sector) ?? double.NaN : double.NaN;
            _incidentsAtEntry = incidents;
            _pitInSector = false;
        }

        private void Close(int n, double exitTime, int incidents)
        {
            if (n < 1 || n > Max || double.IsNaN(_entry) || exitTime <= _entry) return;
            var t = exitTime - _entry;
            var clean = incidents == _incidentsAtEntry && !_pitInSector;
            // Your best: the game's figure when it reports one, or the best clean time this plugin has timed.
            var best = Min(_gameBestAtEntry, _ownBest[n]);
            _gameBestSeen[n] = _gameBestAtEntry;
            _fastestSeen[n] = _fastestAtEntry;
            _currentTime[n] = t;
            _current[n] = Judge(t, best, _fastestAtEntry, clean);
            LastRating = $"S{n} {t:0.000}  clean={clean}  your best={Show(best)}  session fastest={Show(_fastestAtEntry)}  -> {_current[n]}";
            if (clean && (double.IsNaN(_ownBest[n]) || t < _ownBest[n])) _ownBest[n] = t;
        }

        /// <summary>
        /// Red when the sector wasn't clean; purple when no one (you included) has been faster; green for beating
        /// your best (or a first clean time); yellow when slower.
        /// </summary>
        public static PaceState Judge(double time, double personalBest, double sessionFastest, bool clean = true)
        {
            if (!clean) return PaceState.Invalid;
            if (!double.IsNaN(sessionFastest) && time <= sessionFastest + SameTime) return PaceState.SessionBest;
            if (double.IsNaN(personalBest)) return PaceState.PersonalBest;
            return time < personalBest ? PaceState.PersonalBest : PaceState.Slower;
        }

        private static double Min(double a, double b) => double.IsNaN(a) ? b : double.IsNaN(b) ? a : Math.Min(a, b);
        private static string Show(double v) => double.IsNaN(v) ? "-" : v.ToString("0.000");
    }
}
