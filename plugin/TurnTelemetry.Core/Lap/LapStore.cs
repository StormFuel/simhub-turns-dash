using System;

namespace TurnTelemetry.Core.Lap
{
    public enum ReferenceKind
    {
        None,
        Best,
        Last,
    }

    /// <summary>
    /// Samples frames into the current lap, closes laps at start/finish and keeps the last and
    /// session-best laps (docs/architecture.md §4).
    /// </summary>
    /// <remarks>
    /// Sims disagree on whether the lap counter or the position wraps first at the line, so a lap closes on
    /// whichever arrives first and the other is ignored for <see cref="CloseDedupeSeconds"/>. Lap time comes from
    /// the sim's LastLapTime when it updates shortly after closing, otherwise from the highest CurrentLapTime seen.
    /// </remarks>
    public sealed class LapStore
    {
        public const double CloseDedupeSeconds = 3;
        public const double LapTimeSettleSeconds = 3;
        public const double InvalidFlagGraceSeconds = 1;
        public const double MinCoverage = 0.95;
        private const double MaxOfficialTimeMismatch = 1.0;

        private enum CloseTrigger { None, Wrap, Count }

        private int _bins;
        private bool _hasPrevious;
        private Frame _previous;
        private bool _hasPreviousInLap;
        private int _previousBin;
        private double _lapStartTime;
        private double _maxLapTimeSeconds;
        private bool _awaitingWrap;

        private CloseTrigger _lastTrigger;
        private double _lastCloseTime;

        private LapTrace _pending;
        private double _pendingSince;
        private TimeSpan _lastLapTimeBeforeClose;
        private double _pendingOwnTime;

        public LapStore(int bins)
        {
            Reset(bins);
        }

        public LapTrace Current { get; private set; }
        public LapTrace Last { get; private set; }
        public LapTrace Best { get; private set; }
        public LapTrace Reference => Best ?? Last;
        public ReferenceKind ReferenceKind => Best != null ? ReferenceKind.Best : Last != null ? ReferenceKind.Last : ReferenceKind.None;
        public int Bins => _bins;

        /// <summary>Raised when a lap is closed and its time has settled.</summary>
        public event Action<LapTrace> LapCompleted;

        public void Reset(int bins)
        {
            _bins = bins;
            Current = null;
            Last = null;
            Best = null;
            _pending = null;
            _hasPrevious = false;
            _hasPreviousInLap = false;
            _awaitingWrap = false;
            _lastTrigger = CloseTrigger.None;
            _lastCloseTime = double.NegativeInfinity;
        }

        public void ClearReference()
        {
            Best = null;
            Last = null;
        }

        public void Add(in Frame f)
        {
            if (_pending != null) TrySettle(f);

            if (Current == null)
            {
                StartLap(f, firstLap: true);
            }
            else if (_hasPrevious && f.CompletedLaps < _previous.CompletedLaps)
            {
                // Lap counter went backwards: restart without a new session id. Keep the best lap.
                StartLap(f, firstLap: true);
            }
            else
            {
                var recentlyClosed = f.Time - _lastCloseTime < CloseDedupeSeconds;
                var countIncreased = _hasPrevious && f.CompletedLaps > _previous.CompletedLaps;

                if (f.Wrapped && !(recentlyClosed && _lastTrigger == CloseTrigger.Count))
                    Close(f, CloseTrigger.Wrap);
                else if (countIncreased && !(recentlyClosed && _lastTrigger == CloseTrigger.Wrap))
                    Close(f, CloseTrigger.Count);
                else if (f.Wrapped)
                    _awaitingWrap = false; // the wrap that belongs to a count-triggered close
            }

            if (_awaitingWrap && f.LapPos < FrameNormaliser.WrapHigh) _awaitingWrap = false;
            if (!_awaitingWrap) Sample(f);

            _previous = f;
            _hasPrevious = true;
        }

        private void Sample(in Frame f)
        {
            var lap = Current;
            if (f.LapInvalidated && f.Time - _lapStartTime > InvalidFlagGraceSeconds) lap.Invalidate("invalidated by sim");
            if (f.InPitLane) lap.Invalidate("pit lane");
            if (f.Discontinuity) lap.Invalidate("position jump");

            var bin = lap.BinOf(f.LapPos);
            lap.Add(bin, f);
            if (_hasPreviousInLap && !f.Discontinuity && bin > _previousBin + 1)
                lap.Interpolate(_previousBin, _previous, bin, f);

            var lapTime = f.CurrentLapTime.TotalSeconds;
            if (lapTime > _maxLapTimeSeconds) _maxLapTimeSeconds = lapTime;

            _previousBin = bin;
            _hasPreviousInLap = true;
        }

        private void Close(in Frame f, CloseTrigger trigger)
        {
            var lap = Current;
            lap.LapNumber = _previous.CompletedLaps + 1;
            if (lap.Coverage < MinCoverage) lap.Invalidate("incomplete lap");

            if (_pending != null) Settle(_pending, 0); // previous lap never settled; finalise it now
            _pending = lap;
            _pendingSince = f.Time;
            _lastLapTimeBeforeClose = _previous.LastLapTime;
            _pendingOwnTime = _maxLapTimeSeconds;
            Last = lap;

            _lastTrigger = trigger;
            _lastCloseTime = f.Time;
            // Counter ticked while the position has not wrapped yet: skip samples until it does.
            _awaitingWrap = trigger == CloseTrigger.Count && f.LapPos > FrameNormaliser.WrapHigh;

            StartLap(f, firstLap: false);
        }

        private void TrySettle(in Frame f)
        {
            var official = f.LastLapTime;
            if (official != _lastLapTimeBeforeClose && official > TimeSpan.Zero)
            {
                var seconds = official.TotalSeconds;
                var useOfficial = _pendingOwnTime <= 0 || Math.Abs(seconds - _pendingOwnTime) < MaxOfficialTimeMismatch;
                Settle(_pending, useOfficial ? seconds : _pendingOwnTime);
            }
            else if (f.Time - _pendingSince > LapTimeSettleSeconds)
            {
                Settle(_pending, _pendingOwnTime);
            }
        }

        private void Settle(LapTrace lap, double seconds)
        {
            if (seconds <= 0) seconds = _pendingOwnTime;
            lap.LapTimeSeconds = seconds;
            if (lap.Valid && lap.LapTimeSeconds > 0 && (Best == null || lap.LapTimeSeconds < Best.LapTimeSeconds))
                Best = lap;
            if (ReferenceEquals(lap, _pending)) _pending = null;
            LapCompleted?.Invoke(lap);
        }

        private void StartLap(in Frame f, bool firstLap)
        {
            Current = new LapTrace(_bins);
            if (firstLap) Current.Invalidate("out-lap");
            _lapStartTime = f.Time;
            _maxLapTimeSeconds = 0;
            _hasPreviousInLap = false;
        }
    }
}
