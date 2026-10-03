using System;
using System.Collections.Generic;
using System.Linq;
using TurnTelemetry.Core.Lap;
using Xunit;

namespace TurnTelemetry.Tests
{
    public class SectorMapTests
    {
        [Fact]
        public void Learns_boundaries_from_index_steps_and_ignores_the_lap_wrap()
        {
            var map = new SectorMap();
            Assert.Equal(0, map.Count);
            map.Observe(0, 0.10, false);
            map.Observe(1, 0.33, false);
            map.Observe(1, 0.50, false);
            map.Observe(2, 0.68, false);
            map.Observe(0, 0.001, false);   // wrap to sector 1: not a boundary
            Assert.Equal(new[] { 0.33, 0.68 }, map.Boundaries);
            Assert.Equal(3, map.Count);
            Assert.Equal(1, map.SectorAt(0.2));
            Assert.Equal(2, map.SectorAt(0.4));
            Assert.Equal(3, map.SectorAt(0.9));
            Assert.Equal(0.68, map.Start(3));
            Assert.Equal(1, map.End(3));
            Assert.Equal(-1, map.Start(4));
        }

        [Fact]
        public void Jumps_and_discontinuities_are_not_boundaries()
        {
            var map = new SectorMap();
            map.Observe(1, 0.2, false);
            map.Observe(3, 0.5, false);   // skipped a sector
            map.Observe(4, 0.6, true);    // teleport
            Assert.Equal(0, map.Count);
        }

        [Fact]
        public void Saved_boundaries_round_trip_and_live_crossings_replace_them()
        {
            var map = new SectorMap();
            map.Observe(1, 0.1, false);
            map.Observe(2, 0.331, false);
            map.Observe(3, 0.687, false);
            Assert.True(map.Dirty);
            var text = map.Serialize();
            Assert.False(map.Dirty);

            var loaded = new SectorMap();
            loaded.Load(text);
            Assert.Equal(new[] { 0.331, 0.687 }, loaded.Boundaries);
            loaded.Observe(1, 0.2, false);
            loaded.Observe(2, 0.335, false);
            Assert.Equal(new[] { 0.335, 0.687 }, loaded.Boundaries);
        }

        [Fact]
        public void Duplicate_saved_boundaries_are_merged_and_resaved_and_live_ones_replace_them()
        {
            // The file written by the first build at Monza (ACC): one boundary saved twice.
            var map = new SectorMap();
            map.Load("0.33721\n0.66535\n0.66535");
            Assert.Equal(3, map.Count);
            Assert.True(map.Dirty);

            map.Observe(0, 0.1, false);
            map.Observe(1, 0.33721, false);
            map.Observe(2, 0.66535, false);
            Assert.Equal(new[] { 0.33721, 0.66535 }, map.Boundaries);
        }
    }

    public class SectorTimerTests
    {
        private static SectorMap Thirds()
        {
            var map = new SectorMap();
            map.Load("0.33333\n0.66667");
            return map;
        }

        /// <summary>Drives one lap with the given sector times; the sim index steps 0 → 1 → 2.</summary>
        private static void DriveLap(SectorTimer timer, SectorMap map, LapTrace lap, double[] sectors, Func<int, double> fastest)
        {
            var t = 0.0;
            for (var step = 0; step < 300; step++)
            {
                var pos = step / 300.0 + 1e-6;
                var sector = Math.Min(2, (int)(pos * 3));
                var within = pos * 3 - sector;
                t = sectors.Take(sector).Sum() + within * sectors[sector];
                timer.Update(lap, map, pos, t, sector, false, fastest);
            }
        }

        [Fact]
        public void Sectors_rate_purple_green_yellow_and_show_last_lap_until_reached()
        {
            var map = Thirds();
            var timer = new SectorTimer();
            Func<int, double> none = n => double.NaN;

            DriveLap(timer, map, new LapTrace(100), new[] { 30.0, 40.0, 35.0 }, none);
            Assert.Equal(PaceState.PersonalBest, timer.State(1));   // first time on a valid lap
            Assert.Equal(30.0, timer.Time(1), 2);

            var lap2 = new LapTrace(100);
            DriveLap(timer, map, lap2, new[] { 29.5, 40.5, 35.0 }, none);
            // Lap 1's last sector closed when lap 2 began.
            Assert.Equal(PaceState.PersonalBest, timer.State(1));
            Assert.Equal(PaceState.Slower, timer.State(2));
            Assert.False(timer.FromLastLap(2));

            // New lap: sector 3 of lap 2 closes; sectors 1-2 now show lap 2's results, dimmed, until reached.
            var lap3 = new LapTrace(100);
            timer.Update(lap3, map, 0.001, 0.01, 0, false, none);
            Assert.True(timer.FromLastLap(1));
            Assert.Equal(PaceState.PersonalBest, timer.State(1));
            Assert.Equal(PaceState.Slower, timer.State(3));   // 35.0 equals the best: not an improvement
        }

        [Theory]
        [InlineData(29.0, 30.0, 29.0, PaceState.SessionBest)]
        [InlineData(29.5, 30.0, 29.0, PaceState.PersonalBest)]
        [InlineData(30.5, 30.0, 29.0, PaceState.Slower)]
        [InlineData(30.5, double.NaN, double.NaN, PaceState.PersonalBest)]
        [InlineData(30.5, double.NaN, 30.5, PaceState.SessionBest)]
        [InlineData(30.5, double.NaN, 29.0, PaceState.PersonalBest)]
        public void Judge(double time, double personalBest, double sessionFastest, PaceState expected)
        {
            Assert.Equal(expected, SectorTimer.Judge(time, personalBest, sessionFastest));
        }

        [Fact]
        public void A_sector_with_an_excursion_is_red_and_does_not_set_a_best()
        {
            Assert.Equal(PaceState.Invalid, SectorTimer.Judge(28, double.NaN, 29, clean: false));

            var map = Thirds();
            var timer = new SectorTimer();
            Func<int, double> none = n => double.NaN;
            // Lap 1: off track in S2 only (fast because of it), clean elsewhere.
            var incidents = 0;
            DriveLapWith(timer, map, new LapTrace(100), new[] { 30.0, 38.0, 35.0 }, offInSector: 2, ref incidents);
            Assert.Equal(PaceState.PersonalBest, timer.State(1));
            Assert.Equal(PaceState.Invalid, timer.State(2));
            // Lap 2, a slow lap: S2 has no clean best yet, so 41.0 is green; S1 is slower than lap 1's 30.0.
            var lap2 = new LapTrace(100);
            lap2.Invalidate("invalidated by sim");   // lap validity no longer matters per sector
            DriveLapWith(timer, map, lap2, new[] { 32.0, 41.0, 37.0 }, offInSector: 0, ref incidents);
            Assert.Equal(PaceState.Slower, timer.State(1));
            Assert.Equal(PaceState.PersonalBest, timer.State(2));
            // Lap 3: lap 2's 41.0 is now the S2 best.
            DriveLapWith(timer, map, new LapTrace(100), new[] { 31.0, 42.0, 36.0 }, offInSector: 0, ref incidents);
            Assert.Equal(PaceState.Slower, timer.State(2));
        }

        /// <summary>Drives one lap; one track-limit excursion happens mid-way through sector <paramref name="offInSector"/>.</summary>
        private static void DriveLapWith(SectorTimer timer, SectorMap map, LapTrace lap, double[] sectors, int offInSector,
            ref int incidents)
        {
            for (var step = 0; step < 300; step++)
            {
                var pos = step / 300.0 + 1e-6;
                var sector = Math.Min(2, (int)(pos * 3));
                var t = sectors.Take(sector).Sum() + (pos * 3 - sector) * sectors[sector];
                if (sector + 1 == offInSector && step % 100 == 50) incidents++;
                timer.Update(lap, map, pos, t, sector, false, n => double.NaN, null, incidents);
            }
        }

        [Fact]
        public void Game_bests_are_used_and_snapshotted_at_sector_entry()
        {
            var map = Thirds();
            var timer = new SectorTimer();
            // The game already knows a 29.9 best for S1 and 39.0 for S2; someone else has 29.0 / 38.0.
            var gameBest = new Dictionary<int, double> { { 1, 29.9 }, { 2, 39.0 }, { 3, double.NaN } };
            var fastest = new Dictionary<int, double> { { 1, 29.0 }, { 2, 38.0 }, { 3, double.NaN } };
            var lap = new LapTrace(100);
            var t = 0.0;
            for (var step = 0; step < 300; step++)
            {
                var pos = step / 300.0 + 1e-6;
                var sector = Math.Min(2, (int)(pos * 3));
                t = new[] { 29.5, 40.0, 35.0 }.Take(sector).Sum() + (pos * 3 - sector) * new[] { 29.5, 40.0, 35.0 }[sector];
                // The game updates your best the moment you finish S1; the rating must still use the old 29.9.
                if (sector >= 1) gameBest[1] = 29.5;
                timer.Update(lap, map, pos, t, sector, false, n => fastest[n], n => gameBest[n]);
            }
            Assert.Equal(PaceState.PersonalBest, timer.State(1));
            Assert.Equal(PaceState.Slower, timer.State(2));
        }
    }
}
