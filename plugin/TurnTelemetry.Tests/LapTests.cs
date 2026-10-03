using System;
using TurnTelemetry.Core.Input;
using TurnTelemetry.Core.Lap;
using TurnTelemetry.Core.Sims;
using Xunit;

namespace TurnTelemetry.Tests
{
    /// <summary>Drives a FrameNormaliser + LapStore pair the way the engine does.</summary>
    internal sealed class LapDriver
    {
        private readonly FrameNormaliser _normaliser = new FrameNormaliser();
        private readonly GameSnapshot _s = new GameSnapshot();
        private readonly ISimAdapter _adapter = new GenericAdapter();

        public LapDriver(int bins = 100) => Store = new LapStore(bins);

        public LapStore Store { get; }
        public double Time { get; private set; }
        public int Completed { get => _s.CompletedLaps; set => _s.CompletedLaps = value; }
        public bool Invalidated { get => _s.LapInvalidated; set => _s.LapInvalidated = value; }
        public bool InPit { get => _s.InPitLane; set => _s.InPitLane = value; }
        public TimeSpan LastLapTime { get => _s.LastLapTime; set => _s.LastLapTime = value; }

        public Frame Step(double pos, double dt = 0.05, double throttle = 50, double lapTime = -1)
        {
            Time += dt;
            _s.TrackPositionPercent = pos;
            _s.Throttle = throttle;
            _s.CurrentLapTime = TimeSpan.FromSeconds(lapTime >= 0 ? lapTime : _s.CurrentLapTime.TotalSeconds + dt);
            var frame = _normaliser.Normalise(_s, _adapter, null, Time);
            Store.Add(frame);
            return frame;
        }

        /// <summary>Drives from <paramref name="from"/> to just before 1.0 in steps, then crosses the line.</summary>
        public void DriveToLine(double from, double step = 0.004, bool countFirst = false)
        {
            for (var p = from; p < 0.999; p += step) Step(p);
            if (countFirst)
            {
                Completed++;
                Step(0.9995, lapTime: 0);
            }
            else
            {
                Completed++;
            }
            _s.CurrentLapTime = TimeSpan.Zero;
            Step(0.001, lapTime: 0);
        }
    }

    public class FrameNormaliserTests
    {
        [Fact]
        public void Detects_percent_scale_readers()
        {
            var n = new FrameNormaliser();
            var s = new GameSnapshot { TrackPositionPercent = 42 };
            var f = n.Normalise(s, new GenericAdapter(), null, 0);
            Assert.True(n.PercentScale);
            Assert.Equal(0.42, f.LapPos, 6);
        }

        [Fact]
        public void Flags_backward_and_large_forward_jumps_but_not_the_line()
        {
            var n = new FrameNormaliser();
            var a = new GenericAdapter();
            Frame At(double p) => n.Normalise(new GameSnapshot { TrackPositionPercent = p }, a, null, 0);

            At(0.50);
            Assert.False(At(0.51).Discontinuity);
            Assert.True(At(0.40).Discontinuity);   // rewind
            Assert.True(At(0.60).Discontinuity);   // teleport
            At(0.98);
            var cross = At(0.01);
            Assert.True(cross.Wrapped);
            Assert.False(cross.Discontinuity);
        }

        [Fact]
        public void Pedals_are_scaled_to_unit_range()
        {
            var f = new FrameNormaliser().Normalise(new GameSnapshot { Throttle = 80, Brake = 150 }, new GenericAdapter(), null, 0);
            Assert.Equal(0.8, f.Throttle, 6);
            Assert.Equal(1.0, f.Brake, 6);
            Assert.True(double.IsNaN(f.Steer));
        }
    }

    public class LapTraceTests
    {
        [Fact]
        public void Skipped_bins_are_interpolated_but_not_counted_as_visited()
        {
            var d = new LapDriver(bins: 100);
            d.Step(0.100, throttle: 0);
            d.Step(0.140, throttle: 100); // skips bins 11..13

            var lap = d.Store.Current;
            Assert.Equal(2, lap.VisitedBins);
            Assert.True(lap.HasValue(12));
            Assert.False(lap.Visited(12));
            Assert.Equal(0.5, lap.Value(Channel.Throttle, 12), 6);
            Assert.True(double.IsNaN(lap.Value(Channel.Throttle, 50)));
        }

        [Fact]
        public void Bin_values_are_means()
        {
            var d = new LapDriver(bins: 100);
            d.Step(0.101, throttle: 20);
            d.Step(0.102, throttle: 40);
            Assert.Equal(0.3, d.Store.Current.Value(Channel.Throttle, 10), 6);
        }
    }

    public class LapStoreTests
    {
        private static LapDriver WithOutLap()
        {
            var d = new LapDriver();
            d.DriveToLine(0.5); // partial out-lap, closes at the line
            return d;
        }

        [Fact]
        public void Out_lap_is_never_the_best()
        {
            var d = WithOutLap();
            d.Step(0.002, dt: 4); // let the lap time settle
            Assert.NotNull(d.Store.Last);
            Assert.False(d.Store.Last.Valid);
            Assert.Null(d.Store.Best);
            Assert.Equal(ReferenceKind.Last, d.Store.ReferenceKind);
        }

        [Fact]
        public void Full_valid_lap_becomes_best_using_official_time_when_close_to_ours()
        {
            var d = WithOutLap();
            d.DriveToLine(0.002);
            d.LastLapTime = TimeSpan.FromSeconds(12.6);
            d.Step(0.003);

            Assert.NotNull(d.Store.Best);
            Assert.True(d.Store.Best.Coverage >= LapStore.MinCoverage);
            Assert.Equal(12.6, d.Store.Best.LapTimeSeconds, 3);
            Assert.Equal(ReferenceKind.Best, d.Store.ReferenceKind);
        }

        [Fact]
        public void Falls_back_to_own_timing_when_sim_does_not_update_last_lap_time()
        {
            var d = WithOutLap();
            d.DriveToLine(0.002);
            d.Step(0.003, dt: LapStore.LapTimeSettleSeconds + 0.1);
            Assert.NotNull(d.Store.Best);
            Assert.InRange(d.Store.Best.LapTimeSeconds, 12, 13);
        }

        [Fact]
        public void Only_a_faster_lap_replaces_the_best()
        {
            var d = WithOutLap();
            d.DriveToLine(0.002);
            d.LastLapTime = TimeSpan.FromSeconds(12.5);
            d.Step(0.003);
            var first = d.Store.Best;

            d.DriveToLine(0.004);
            d.LastLapTime = TimeSpan.FromSeconds(12.9);
            d.Step(0.005);

            Assert.Same(first, d.Store.Best);
            Assert.NotSame(first, d.Store.Last);
        }

        [Fact]
        public void Invalidated_lap_is_not_best()
        {
            var d = WithOutLap();
            for (var p = 0.002; p < 0.5; p += 0.004) d.Step(p);
            d.Invalidated = true;
            d.Step(0.5);
            d.Invalidated = false;
            d.DriveToLine(0.504);
            d.Step(0.003, dt: 4);
            Assert.Equal("invalidated by sim", d.Store.Last.InvalidReason);
            Assert.Null(d.Store.Best);
        }

        [Fact]
        public void Stale_invalid_flag_at_lap_start_is_ignored()
        {
            var d = WithOutLap();
            d.Invalidated = true; // flag from the previous lap still set for a moment
            d.Step(0.002);
            d.Invalidated = false;
            d.DriveToLine(0.006);
            d.Step(0.003, dt: 4);
            Assert.True(d.Store.Last.Valid);
        }

        [Fact]
        public void Teleport_invalidates_the_lap()
        {
            var d = WithOutLap();
            for (var p = 0.002; p < 0.3; p += 0.004) d.Step(p);
            d.Step(0.6); // reset to track
            Assert.Equal("position jump", d.Store.Current.InvalidReason);
        }

        [Fact]
        public void Counter_before_wrap_closes_once_and_keeps_end_of_lap_samples_out_of_the_new_lap()
        {
            var d = WithOutLap();
            d.DriveToLine(0.002, countFirst: true);
            var lapNumbers = d.Store.Last.LapNumber;
            d.Step(0.003, dt: 4);

            Assert.True(d.Store.Last.Valid, d.Store.Last.InvalidReason);
            Assert.Equal(lapNumbers, d.Store.Last.LapNumber);
            Assert.False(d.Store.Current.Visited(d.Store.Current.BinOf(0.9995)));
        }

        [Fact]
        public void Pit_lane_invalidates_lap()
        {
            var d = WithOutLap();
            d.InPit = true;
            d.Step(0.002);
            Assert.False(d.Store.Current.Valid);
        }
    }
}
