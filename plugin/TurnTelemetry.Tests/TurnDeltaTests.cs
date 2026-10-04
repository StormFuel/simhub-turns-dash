using System;
using System.Collections.Generic;
using TurnTelemetry.Core.Turns;
using Xunit;

namespace TurnTelemetry.Tests
{
    public class TurnDeltaTests
    {
        private static readonly List<TurnDefinition> Turns = new List<TurnDefinition>
        {
            new TurnDefinition { Label = "1", Start = 0.10, End = 0.15 },
            new TurnDefinition { Label = "2", Start = 0.40, End = 0.45 },
            new TurnDefinition { Label = "3", Start = 0.70, End = 0.75 },
        };

        /// <summary>
        /// Drives one lap (0.004 of a lap per frame; <paramref name="dt"/> gives the seconds per frame by position) and
        /// crosses the line. <paramref name="offAt"/>: a track-limits excursion happens at that lap position.
        /// </summary>
        private static void Lap(LapDriver d, TurnDeltas deltas, ref int incidents, Func<double, double> dt,
            double offAt = -1, Action<double> during = null)
        {
            var lapTime = 0.0;
            for (var p = 0.001; p < 0.999; p += 0.004)
            {
                if (offAt >= 0 && Math.Abs(p - offAt) < 0.002) incidents++;
                lapTime += dt(p);
                d.Step(p, dt(p), lapTime: lapTime);
                deltas.Update(Turns, d.Store.Current, p, incidents);
                during?.Invoke(p);
            }
            d.Completed++;
            d.Step(0.001, lapTime: 0);
            deltas.Update(Turns, d.Store.Current, 0.001, incidents);
            // The sim publishes the official time (distinct per lap, as real laps are): the lap settles.
            d.LastLapTime = TimeSpan.FromSeconds(lapTime + 0.001 * d.Completed);
            d.Step(0.002, lapTime: 0.05);
            deltas.Update(Turns, d.Store.Current, 0.002, incidents);
        }

        [Fact]
        public void Segments_partition_the_lap_between_corners()
        {
            var deltas = new TurnDeltas();
            deltas.Update(Turns, null, 0);
            Assert.Equal(0, deltas.SegmentStart(0));
            Assert.Equal(0.275, deltas.SegmentEnd(0), 6);      // midway between T1 end and T2 start
            Assert.Equal(0.275, deltas.SegmentStart(1), 6);
            Assert.Equal(0.575, deltas.SegmentEnd(1), 6);
            Assert.Equal(1, deltas.SegmentEnd(2));               // the last turn runs to the line
        }

        [Fact]
        public void Each_corner_is_compared_with_its_best_clean_time()
        {
            var d = new LapDriver();
            var deltas = new TurnDeltas();
            var incidents = 0;

            // Lap 1 (the out-lap, invalid as a lap): its clean corners still set the bests. No colours yet.
            Lap(d, deltas, ref incidents, p => 0.05);
            Assert.Equal(TurnDeltaState.None, deltas.State(0));
            Assert.False(double.IsNaN(deltas.Best(1)));

            // Lap 2: slower through Turn 2's segment, faster through Turn 3's.
            Func<double, double> dt = p => p > 0.3 && p < 0.55 ? 0.06 : p > 0.6 && p < 0.95 ? 0.045 : 0.05;
            Lap(d, deltas, ref incidents, dt, during: p =>
            {
                if (Math.Abs(p - 0.601) < 0.002)
                {
                    Assert.Equal(TurnDeltaState.Gain, deltas.State(0));    // matched the best
                    Assert.Equal(TurnDeltaState.Loss, deltas.State(1));
                    Assert.StartsWith("+0.", deltas.Text(1));
                    Assert.False(deltas.FromLastLap(1));
                }
            });
            // New lap: lap 2's results show (dimmed) until each corner is driven again; T3 (ends at the line) has settled.
            Assert.True(deltas.FromLastLap(1));
            Assert.Equal(TurnDeltaState.Loss, deltas.State(1));
            Assert.Equal(TurnDeltaState.Gain, deltas.State(2));
            Assert.StartsWith("-", deltas.Text(2));
        }

        [Fact]
        public void A_cut_only_discards_that_corner()
        {
            var d = new LapDriver();
            var deltas = new TurnDeltas();
            var incidents = 0;
            Lap(d, deltas, ref incidents, p => 0.05);
            var bestTurn2 = deltas.Best(1);

            // Lap 2: much faster through Turn 2 but off track there (excursion at 0.42); Turn 3 clean and faster.
            Func<double, double> dt = p => p > 0.3 && p < 0.55 ? 0.03 : p > 0.6 && p < 0.95 ? 0.045 : 0.05;
            Lap(d, deltas, ref incidents, dt, offAt: 0.421);

            Assert.Equal(TurnDeltaState.None, deltas.State(1));          // the cut corner gets no colour
            Assert.Equal(bestTurn2, deltas.Best(1), 6);                  // and doesn't become the best
            Assert.Equal(TurnDeltaState.Gain, deltas.State(2));          // the rest of the lap still counts
            Assert.True(deltas.Best(2) < 0.05 * 0.426 / 0.004);           // T3's best improved
        }

        [Fact]
        public void Short_text_fits_narrow_bands()
        {
            var d = new LapDriver();
            var deltas = new TurnDeltas();
            var incidents = 0;
            Lap(d, deltas, ref incidents, p => 0.05);
            Lap(d, deltas, ref incidents, p => p > 0.3 && p < 0.55 ? 0.06 : 0.05);
            Assert.Equal("+", deltas.Sign(1));
            Assert.Matches(@"^\d\.\d$|^\.\d\d$", deltas.ShortDigits(1));   // e.g. ".62" or "1.2"
            Assert.True(deltas.ShortDigits(1).Length <= 3);
            Assert.Equal("", new TurnDeltas().ShortDigits(0));
            Assert.Equal("", new TurnDeltas().Sign(0));
        }

        [Fact]
        public void Long_losses_and_stops_read_as_more_than_a_minute_and_never_become_the_best()
        {
            var d = new LapDriver();
            var deltas = new TurnDeltas();
            var incidents = 0;
            Lap(d, deltas, ref incidents, p => 0.05);
            var best = deltas.Best(1);

            // Lap 2: a long stop in Turn 2's segment (about 150 s), e.g. a spin and a slow recovery.
            Lap(d, deltas, ref incidents, p => Math.Abs(p - 0.401) < 0.002 ? 150 : 0.05);
            Assert.Equal(TurnDeltaState.Loss, deltas.State(1));
            Assert.Equal(">1m", deltas.Text(1));
            Assert.Equal(">", deltas.Sign(1));
            Assert.Equal("1m", deltas.ShortDigits(1));
            Assert.Equal(best, deltas.Best(1), 6);

            // Lap 3: a 12 s loss reads with one decimal; the narrow form drops the decimals.
            Lap(d, deltas, ref incidents, p => Math.Abs(p - 0.401) < 0.002 ? 12.05 : 0.05);
            Assert.Equal("+12.0", deltas.Text(1));
            Assert.Equal("12", deltas.ShortDigits(1));
            Assert.Equal("+", deltas.Sign(1));
        }

        [Fact]
        public void Unknown_states_and_resets()
        {
            var deltas = new TurnDeltas();
            Assert.Equal(TurnDeltaState.None, deltas.State(0));
            Assert.Equal("", deltas.Text(5));
            deltas.Update(Turns, null, 0.5);
            deltas.Reset();
            Assert.Equal(0, deltas.Count);
        }
    }
}
