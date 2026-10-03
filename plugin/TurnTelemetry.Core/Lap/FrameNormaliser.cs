using System;
using TurnTelemetry.Core.Input;
using TurnTelemetry.Core.Sims;

namespace TurnTelemetry.Core.Lap
{
    /// <summary>One normalised telemetry sample.</summary>
    public struct Frame
    {
        public double Time;
        /// <summary>0..1</summary>
        public double LapPos;
        public int CompletedLaps;
        public bool LapInvalidated;
        public bool InPitLane;
        public double SpeedKmh;
        /// <summary>0..1</summary>
        public double Throttle;
        /// <summary>0..1</summary>
        public double Brake;
        /// <summary>-1..+1, NaN when the sim cannot report it.</summary>
        public double Steer;
        public bool Tc;
        public bool Abs;
        public TimeSpan CurrentLapTime;
        public TimeSpan LastLapTime;
        /// <summary>Position jumped (teleport, reset, rewind): this frame must not be joined to the previous one.</summary>
        public bool Discontinuity;
        /// <summary>Position wrapped across start/finish since the previous frame.</summary>
        public bool Wrapped;
    }

    /// <summary>Turns a snapshot plus adapter output into a <see cref="Frame"/> and flags discontinuities.</summary>
    public sealed class FrameNormaliser
    {
        public const double WrapHigh = 0.9;
        public const double WrapLow = 0.1;
        public const double MaxBackwards = 0.01;
        public const double MaxForward = 0.05;

        private bool _hasPrevious;
        private double _previousPos;

        /// <summary>True once any position above 1.5 was seen: the reader reports 0..100.</summary>
        public bool PercentScale { get; private set; }

        public void Reset()
        {
            _hasPrevious = false;
            PercentScale = false;
        }

        public Frame Normalise(GameSnapshot s, ISimAdapter adapter, IRawData raw, double time)
        {
            var pos = s.TrackPositionPercent;
            if (pos > 1.5) PercentScale = true;
            if (PercentScale) pos /= 100.0;
            pos = Clamp01(pos);

            var frame = new Frame
            {
                Time = time,
                LapPos = pos,
                CompletedLaps = s.CompletedLaps,
                LapInvalidated = s.LapInvalidated,
                InPitLane = s.InPitLane,
                SpeedKmh = s.SpeedKmh,
                Throttle = Clamp01(s.Throttle / 100.0),
                Brake = Clamp01(s.Brake / 100.0),
                Steer = adapter.ReadSteer(raw),
                Tc = s.TcActive,
                Abs = s.AbsActive,
                CurrentLapTime = s.CurrentLapTime,
                LastLapTime = s.LastLapTime,
            };

            if (_hasPrevious)
            {
                if (_previousPos > WrapHigh && pos < WrapLow)
                {
                    frame.Wrapped = true;
                }
                else
                {
                    var delta = pos - _previousPos;
                    frame.Discontinuity = delta < -MaxBackwards || delta > MaxForward;
                }
            }

            _hasPrevious = true;
            _previousPos = pos;
            return frame;
        }

        private static double Clamp01(double v) => double.IsNaN(v) ? 0 : Math.Max(0, Math.Min(1, v));
    }
}
