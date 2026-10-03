using System.Collections.Generic;
using TurnTelemetry.Core.Lap;
using TurnTelemetry.Core.Turns;
using Xunit;

namespace TurnTelemetry.Tests
{
    public class TrackLimitsTests
    {
        private static readonly List<TurnDefinition> Turns = new List<TurnDefinition>
        {
            new TurnDefinition { Label = "1", Start = 0.10, End = 0.15 },
            new TurnDefinition { Label = "2", Start = 0.40, End = 0.45 },
            new TurnDefinition { Label = "3", Start = 0.95, End = 0.02 },   // wraps the line
        };

        [Fact]
        public void Tyres_out_excursions_are_counted_per_turn_with_rearm()
        {
            var limits = new TrackLimits();
            var lap = new LapTrace(100);
            limits.Update(lap, Turns, 0.12, 0, 4, false, 10, 10);
            limits.Update(lap, Turns, 0.12, 4, 4, false, 10.1, 10.1);    // off: T1
            limits.Update(lap, Turns, 0.13, 2, 4, false, 10.2, 10.2);    // back on
            limits.Update(lap, Turns, 0.13, 4, 4, false, 10.4, 10.4);    // off again within rearm: same excursion
            limits.Update(lap, Turns, 0.14, 0, 4, false, 10.5, 10.5);
            limits.Update(lap, Turns, 0.43, 4, 4, false, 30, 30);        // T2
            Assert.Equal(1, limits.ThisLap(Turns[0]));
            Assert.Equal(1, limits.ThisLap(Turns[1]));
            Assert.Equal(2, limits.SessionTotal);
            Assert.Equal("tyres out", limits.Source);

            var lap2 = new LapTrace(100);
            limits.Update(lap2, Turns, 0.01, 0, 4, false, 0.5, 100);
            Assert.Equal(0, limits.ThisLap(Turns[0]));
            Assert.Equal(1, limits.ThisSession(Turns[0]));
        }

        [Fact]
        public void Three_tyres_is_not_an_excursion_in_acc_but_is_in_ac()
        {
            var acc = new TrackLimits();
            var ac = new TrackLimits();
            var lap = new LapTrace(100);
            acc.Update(lap, Turns, 0.12, 3, 4, false, 10, 10);
            ac.Update(lap, Turns, 0.12, 3, 3, false, 10, 10);
            Assert.Equal(0, acc.SessionTotal);
            Assert.Equal(1, ac.SessionTotal);
        }

        [Fact]
        public void Lap_invalidation_is_the_fallback_and_is_not_double_counted()
        {
            var limits = new TrackLimits();
            var lap = new LapTrace(100);
            limits.Update(lap, Turns, 0.30, null, 0, false, 20, 20);
            limits.Update(lap, Turns, 0.30, null, 0, true, 20.1, 20.1);   // invalidated nearer T2 than T1
            Assert.Equal(1, limits.ThisLap(Turns[1]));
            Assert.Equal("lap invalidated", limits.Source);

            // With tyres-out data, the invalidation that follows an excursion is the same incident.
            var both = new TrackLimits();
            both.Update(lap, Turns, 0.12, 4, 4, false, 10, 10);
            both.Update(lap, Turns, 0.12, 4, 4, true, 10.3, 10.3);
            Assert.Equal(1, both.SessionTotal);
        }

        [Fact]
        public void Invalidation_at_the_line_is_ignored_and_wrapping_turns_are_nearest()
        {
            var limits = new TrackLimits();
            var lap = new LapTrace(100);
            limits.Update(lap, Turns, 0.001, null, 0, true, 0.2, 5);
            Assert.Equal(0, limits.SessionTotal);
            Assert.Same(Turns[2], TrackLimits.Nearest(Turns, 0.04));
            Assert.Same(Turns[2], TrackLimits.Nearest(Turns, 0.99));
        }
    }
}
