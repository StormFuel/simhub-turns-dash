using System.Collections.Generic;
using System.Linq;
using TurnTelemetry.Core.Turns;
using Xunit;

namespace TurnTelemetry.Tests
{
    public class StripLayoutTests
    {
        private static TurnDefinition T(string label, double start, double end) => new TurnDefinition { Label = label, Start = start, End = end };

        /// <summary>ACC Silverstone shape: the timing line is before Turn 9 (Copse), Turn 1 (Abbey) is past half-lap.</summary>
        private static readonly List<TurnDefinition> Silverstone = new List<TurnDefinition>
        {
            T("9", 0.03, 0.06), T("10", 0.10, 0.12), T("15", 0.30, 0.34), T("18", 0.45, 0.48),
            T("1", 0.56, 0.58), T("3", 0.66, 0.70), T("8", 0.90, 0.94),
        };

        [Fact]
        public void Strip_starts_between_the_last_corner_and_turn_1_when_the_lap_starts_elsewhere()
        {
            var origin = StripLayout.Origin(Silverstone);
            Assert.Equal(0.52, origin, 6);                                     // halfway between T18 (0.48) and T1 (0.56)
            Assert.Equal(0.04, StripLayout.ToStrip(0.56, origin), 6);          // Turn 1 near the left edge
            var order = Silverstone.OrderBy(t => StripLayout.ToStrip(t.Start, origin)).Select(t => t.Label);
            Assert.Equal(new[] { "1", "3", "8", "9", "10", "15", "18" }, order);
            Assert.Equal(0.48, StripLayout.ToStrip(0.0, origin), 6);           // the car starts mid-strip at the line
        }

        [Fact]
        public void Tracks_whose_line_is_just_before_turn_1_are_unchanged()
        {
            var monza = new List<TurnDefinition> { T("1", 0.05, 0.08), T("2", 0.10, 0.12), T("11", 0.85, 0.92) };
            Assert.Equal(0, StripLayout.Origin(monza));
            Assert.Equal(1, StripLayout.EndToStrip(1.0, 0));                     // a span ending at the line ends at the right edge
        }

        [Fact]
        public void No_turn_1_means_no_rotation()
        {
            Assert.Equal(0, StripLayout.Origin(new List<TurnDefinition> { T("left 89°", 0.2, 0.25), T("right", 0.5, 0.55) }));
            Assert.Equal(0, StripLayout.Origin(new List<TurnDefinition>()));
            Assert.Equal(0.3, StripLayout.Origin(new List<TurnDefinition> { T("8", 0.1, 0.2), T("T1", 0.4, 0.45) }), 6);
        }

        [Fact]
        public void Wrapped_spans_put_their_label_in_the_larger_piece()
        {
            Assert.Equal(0.5, StripLayout.LabelCentre(0.4, 0.6), 6);
            Assert.Equal(0.85, StripLayout.LabelCentre(0.7, 0.1), 6);          // [0.7,1] is larger than [0,0.1]
            Assert.Equal(0.25, StripLayout.LabelCentre(0.9, 0.5), 6);          // [0,0.5] is larger
        }
    }
}
