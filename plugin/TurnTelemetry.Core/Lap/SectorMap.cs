using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TurnTelemetry.Core.Lap
{
    /// <summary>
    /// Sector boundaries as lap positions. SimHub reports only the index of the sector the car is in, so the boundaries
    /// are learned from where that index steps up, and saved per track so the next session knows them from the start.
    /// </summary>
    public sealed class SectorMap
    {
        public const int MaxSectors = 6;
        /// <summary>Index changes this close to the line are the lap wrap (or noise), not a sector boundary.</summary>
        private const double LineMargin = 0.01;
        /// <summary>A boundary seen again within this distance is the same one; it isn't re-saved.</summary>
        private const double SameBoundary = 0.002;

        /// <summary>Boundaries learned this session, keyed by the sim's sector index.</summary>
        private readonly SortedDictionary<int, double> _starts = new SortedDictionary<int, double>();
        /// <summary>Boundaries from the saved file, until a live crossing replaces them.</summary>
        private readonly List<double> _loaded = new List<double>();
        private int? _previousIndex;

        /// <summary>Lap positions where sectors 2..n start, ascending.</summary>
        public IReadOnlyList<double> Boundaries { get; private set; } = Array.Empty<double>();

        /// <summary>Number of sectors once at least one boundary is known; 0 until then.</summary>
        public int Count => Boundaries.Count == 0 ? 0 : Boundaries.Count + 1;

        /// <summary>True when a boundary was learned or moved since the last save.</summary>
        public bool Dirty { get; private set; }

        public void Reset()
        {
            _starts.Clear();
            _loaded.Clear();
            _previousIndex = null;
            Boundaries = Array.Empty<double>();
            Dirty = false;
        }

        /// <param name="index">The sim's current sector index (any base; only steps of +1 are used).</param>
        public void Observe(int index, double lapPos, bool discontinuity)
        {
            var previous = _previousIndex;
            _previousIndex = index;
            if (previous == null || discontinuity || index != previous + 1) return;
            if (lapPos < LineMargin || lapPos > 1 - LineMargin) return;
            if (_starts.TryGetValue(index, out var known) && Math.Abs(known - lapPos) < SameBoundary) return;
            if (!_starts.ContainsKey(index) && _starts.Count >= MaxSectors - 1) return;
            // A different index at (nearly) the same place as a learned boundary is the sim re-numbering, not a new one.
            foreach (var key in _starts.Where(p => p.Key != index && Math.Abs(p.Value - lapPos) < Nearby).Select(p => p.Key).ToList())
                _starts.Remove(key);
            _starts[index] = lapPos;
            Rebuild();
            Dirty = true;
        }

        /// <summary>1-based sector containing <paramref name="lapPos"/>, or 0 when no boundaries are known.</summary>
        public int SectorAt(double lapPos)
        {
            if (Count == 0) return 0;
            var n = 1;
            foreach (var b in Boundaries)
                if (lapPos >= b) n++;
            return n;
        }

        /// <summary>Start / end lap position of 1-based sector <paramref name="n"/>, or -1 when it doesn't exist.</summary>
        public double Start(int n) => n < 1 || n > Count ? -1 : n == 1 ? 0 : Boundaries[n - 2];
        public double End(int n) => n < 1 || n > Count ? -1 : n == Count ? 1 : Boundaries[n - 1];

        /// <summary>One boundary per line, invariant culture.</summary>
        public string Serialize()
        {
            Dirty = false;
            return string.Join("\n", Boundaries.Select(b => b.ToString("0.00000", CultureInfo.InvariantCulture)));
        }

        public void Load(string text)
        {
            Reset();
            if (string.IsNullOrWhiteSpace(text)) return;
            var values = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => double.TryParse(l.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN)
                .Where(v => v > 0 && v < 1).OrderBy(v => v);
            foreach (var v in values)
                if (!_loaded.Any(x => Math.Abs(x - v) < Nearby) && _loaded.Count < MaxSectors - 1) _loaded.Add(v);
            Rebuild();
            Dirty = _loaded.Count != values.Count();   // re-save a file that had duplicates
        }

        /// <summary>A learned boundary within this distance replaces a saved one; closer saved values are duplicates.</summary>
        private const double Nearby = 0.05;

        private void Rebuild()
        {
            var live = _starts.Values.ToList();
            var all = live.Concat(_loaded.Where(v => !live.Any(x => Math.Abs(x - v) < Nearby)));
            Boundaries = all.OrderBy(v => v).Take(MaxSectors - 1).ToList();
        }
    }
}
