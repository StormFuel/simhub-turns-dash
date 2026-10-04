using System;

namespace TurnTelemetry.Core.Lap
{
    [Flags]
    public enum LapEvents : byte
    {
        None = 0,
        Tc = 1,
        Abs = 2,
    }

    public enum Channel
    {
        Throttle,
        Brake,
        Steer,
        Speed,
    }

    /// <summary>
    /// One lap's telemetry, resampled into fixed distance bins (docs/architecture.md §4).
    /// Values are bin means; bins the car skipped are linearly interpolated and do not count toward coverage.
    /// </summary>
    public sealed class LapTrace
    {
        private const int ChannelCount = 4;

        private readonly double[,] _sum;
        private readonly int[] _count;
        private readonly double[,] _interpolated;
        private readonly bool[] _hasInterpolated;
        private readonly LapEvents[] _events;
        /// <summary>Sim lap time (s) when the car first entered each bin; NaN until it does. Used for per-turn deltas.</summary>
        private readonly double[] _entryTime;

        public LapTrace(int bins)
        {
            if (bins < 10) throw new ArgumentOutOfRangeException(nameof(bins));
            Bins = bins;
            _sum = new double[ChannelCount, bins];
            _count = new int[bins];
            _interpolated = new double[ChannelCount, bins];
            _hasInterpolated = new bool[bins];
            _events = new LapEvents[bins];
            _entryTime = new double[bins];
            for (var i = 0; i < bins; i++) _entryTime[i] = double.NaN;
        }

        /// <summary>Lap time (s) when the car entered <paramref name="bin"/>, or NaN.</summary>
        public double EntryTime(int bin) => _entryTime[bin];

        public int Bins { get; }
        public int VisitedBins { get; private set; }
        public double Coverage => VisitedBins / (double)Bins;

        /// <summary>Lap time in seconds once the lap is closed; 0 while open or unknown.</summary>
        public double LapTimeSeconds { get; internal set; }
        public bool Valid { get; internal set; } = true;
        public string InvalidReason { get; private set; }
        public int LapNumber { get; internal set; }

        public void Invalidate(string reason)
        {
            if (!Valid) return;
            Valid = false;
            InvalidReason = reason;
        }

        public int BinOf(double lapPos) => Math.Min(Bins - 1, Math.Max(0, (int)(lapPos * Bins)));

        public bool Visited(int bin) => _count[bin] > 0;
        public bool HasValue(int bin) => _count[bin] > 0 || _hasInterpolated[bin];
        public LapEvents Events(int bin) => _events[bin];

        /// <summary>Bin mean, interpolated value for skipped bins, or NaN when the bin has no data.</summary>
        public double Value(Channel channel, int bin)
        {
            var c = (int)channel;
            if (_count[bin] > 0) return _sum[c, bin] / _count[bin];
            return _hasInterpolated[bin] ? _interpolated[c, bin] : double.NaN;
        }

        internal void Add(int bin, in Frame f)
        {
            if (double.IsNaN(_entryTime[bin])) _entryTime[bin] = f.CurrentLapTime.TotalSeconds;
            if (_count[bin] == 0) VisitedBins++;
            _count[bin]++;
            _sum[(int)Channel.Throttle, bin] += f.Throttle;
            _sum[(int)Channel.Brake, bin] += f.Brake;
            _sum[(int)Channel.Steer, bin] += double.IsNaN(f.Steer) ? 0 : f.Steer;
            _sum[(int)Channel.Speed, bin] += f.SpeedKmh;
            if (f.Tc) _events[bin] |= LapEvents.Tc;
            if (f.Abs) _events[bin] |= LapEvents.Abs;
        }

        /// <summary>Fills bins strictly between <paramref name="fromBin"/> and <paramref name="toBin"/>.</summary>
        internal void Interpolate(int fromBin, in Frame from, int toBin, in Frame to)
        {
            var span = toBin - fromBin;
            for (var bin = fromBin + 1; bin < toBin; bin++)
            {
                if (_count[bin] > 0) continue;
                var t = (bin - fromBin) / (double)span;
                _interpolated[(int)Channel.Throttle, bin] = Lerp(from.Throttle, to.Throttle, t);
                _interpolated[(int)Channel.Brake, bin] = Lerp(from.Brake, to.Brake, t);
                _interpolated[(int)Channel.Steer, bin] = Lerp(Zero(from.Steer), Zero(to.Steer), t);
                _interpolated[(int)Channel.Speed, bin] = Lerp(from.SpeedKmh, to.SpeedKmh, t);
                if (double.IsNaN(_entryTime[bin]))
                    _entryTime[bin] = Lerp(from.CurrentLapTime.TotalSeconds, to.CurrentLapTime.TotalSeconds, t);
                _hasInterpolated[bin] = true;
            }
        }

        private static double Zero(double v) => double.IsNaN(v) ? 0 : v;
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }
}
