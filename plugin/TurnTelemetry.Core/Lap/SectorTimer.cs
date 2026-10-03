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
        private LapTrace _lap;
        private int? _previousIndex;
        private double _entry = double.NaN;
        private double _lastLapTime;
        /// <summary>1-based sector being driven, counted by index steps since the line; 0 when unknown.</summary>
        private int _sector;
        /// <summary>Game-reported bests captured as the sector started, so the new time can't be compared to itself.</summary>
        private double _gameBestAtEntry = double.NaN;
        private double _fastestAtEntry = double.NaN;
        private int _incidentsAtEntry;
        private bool _pitInSector;

        /// <summary>The last rating and what it was based on, for the settings-tab diagnostics.</summary>
        public string LastRating { get; private set; } = "none yet";

        public SectorTimer()
        {
            Reset();
        }

        public void Reset()
        {
            _lap = null;
            _previousIndex = null;
            _entry = double.NaN;
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

        /// <param name="sessionFastest">Fastest best time for 1-based sector n by any driver, you included (NaN when unknown).</param>
        /// <param name="gameBest">Your best time for sector n as the game reports it (NaN when unknown).</param>
        /// <param name="incidents">Running count of track-limit excursions (TrackLimits.Incidents).</param>
        public void Update(LapTrace lap, SectorMap map, double lapPos, double lapTime, int simIndex, bool discontinuity,
            Func<int, double> sessionFastest, Func<int, double> gameBest = null, int incidents = 0, bool inPit = false)
        {
            if (lap == null) return;
            if (!ReferenceEquals(lap, _lap))
            {
                if (_lap != null) Close(_sector > 0 ? _sector : map.Count, _lastLapTime, incidents);
                for (var n = 0; n <= Max; n++)
                {
                    _last[n] = _current[n];
                    _lastTime[n] = _currentTime[n];
                    _current[n] = PaceState.None;
                    _currentTime[n] = double.NaN;
                }
                _lap = lap;
                // A lap that starts at the line times sector 1 from zero; one joined mid-track can't.
                var atLine = lapPos < 0.05;
                _entry = atLine ? 0 : double.NaN;
                _sector = atLine ? 1 : map.SectorAt(lapPos);
                StartSector(sessionFastest, gameBest, incidents);
            }
            else if (discontinuity)
            {
                _entry = double.NaN;
            }
            else if (_previousIndex.HasValue && simIndex == _previousIndex + 1)
            {
                // Count steps rather than reading the position: a saved boundary can sit a hair off the sim's.
                var completed = _sector > 0 ? _sector : map.SectorAt(lapPos) - 1;
                if (completed >= 1) Close(completed, lapTime, incidents);
                _sector = completed >= 1 ? completed + 1 : 0;
                _entry = lapTime;
                StartSector(sessionFastest, gameBest, incidents);
            }
            if (inPit) _pitInSector = true;
            _previousIndex = simIndex;
            _lastLapTime = lapTime;
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
