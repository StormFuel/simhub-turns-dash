using System.IO;
using System.Linq;
using TurnTelemetry.Core.Engine;
using TurnTelemetry.Core.Turns;
using Xunit;

namespace TurnTelemetry.Tests
{
    public class StartLineTests
    {
        // Curated-style track: four turns numbered 1-4 from the game's start/finish line.
        private static FakeBundle Curated()
        {
            var bundle = new FakeBundle();
            bundle.Files["data/turns/AssettoCorsaCompetizione/monza.json"] = new TurnFile
            {
                Turns =
                {
                    new TurnFile.TurnEntry { Label = "1", Name = "Alpha", Start = 0.10, End = 0.15 },
                    new TurnFile.TurnEntry { Label = "2", Name = "Bravo", Start = 0.30, End = 0.35 },
                    new TurnFile.TurnEntry { Label = "3", Name = "Charlie", Start = 0.50, End = 0.55 },
                    new TurnFile.TurnEntry { Label = "4", Name = "Delta", Start = 0.80, End = 0.85 },
                },
            }.ToJson();
            return bundle;
        }

        private static TelemetryEngine Engine(TempDir dir, double pos)
        {
            var engine = new TelemetryEngine(dir.Path, Curated());
            engine.Update(EngineTestsAccess.Snapshot("monza", pos), new FakeRaw(), 0);
            return engine;
        }

        private static void Move(TelemetryEngine e, double pos, double time) =>
            e.Update(EngineTestsAccess.Snapshot("monza", pos), new FakeRaw(), time);

        private static string Labels(TelemetryEngine e) => string.Join(",", e.Catalog.Turns.Select(t => t.Name + "=" + t.Label));

        [Fact]
        public void Line_set_before_a_corner_makes_that_corner_turn_1()
        {
            using (var dir = new TempDir())
            {
                var e = Engine(dir, 0.45); // on the straight before Charlie
                e.SetStartLine();

                Assert.Equal(TurnSource.User, e.Catalog.Source);
                Assert.Equal("Alpha=3,Bravo=4,Charlie=1,Delta=2", Labels(e));
                Assert.Equal("LINE SET · TURN 1 = CHARLIE", e.StartLine.Message);
                Assert.True(e.StartLine.UndoAvailable(e.Now));
            }
        }

        [Fact]
        public void Line_set_inside_a_corner_makes_the_following_corner_turn_1()
        {
            using (var dir = new TempDir())
            {
                var e = Engine(dir, 0.52); // inside Charlie: the line is here, so Delta is the first corner after it
                e.SetStartLine();
                Assert.Equal("Alpha=2,Bravo=3,Charlie=4,Delta=1", Labels(e));
            }
        }

        [Fact]
        public void Pressing_again_within_five_seconds_undoes_it()
        {
            using (var dir = new TempDir())
            {
                var e = Engine(dir, 0.45);
                e.SetStartLine();
                Move(e, 0.46, StartLineSetter.UndoSeconds - 1);
                e.SetStartLine();

                Assert.Equal(TurnSource.Curated, e.Catalog.Source);
                Assert.Equal("Alpha=1,Bravo=2,Charlie=3,Delta=4", Labels(e));
                Assert.False(File.Exists(e.UserTurnFilePath));
                Assert.Equal("UNDONE", e.StartLine.Message);
            }
        }

        [Fact]
        public void After_the_undo_window_a_new_press_sets_the_line_again()
        {
            using (var dir = new TempDir())
            {
                var e = Engine(dir, 0.45);
                e.SetStartLine();                      // Charlie = 1
                Move(e, 0.70, StartLineSetter.UndoSeconds + 1);
                e.SetStartLine();                      // Delta = 1
                Assert.Equal("Alpha=2,Bravo=3,Charlie=4,Delta=1", Labels(e));
            }
        }

        [Fact]
        public void At_the_games_own_line_nothing_changes_when_numbering_already_starts_there()
        {
            using (var dir = new TempDir())
            {
                var e = Engine(dir, 0.02);
                e.SetStartLine();
                Assert.Equal(TurnSource.Curated, e.Catalog.Source);
                Assert.StartsWith("ALREADY NUMBERED FROM HERE", e.StartLine.Message);
            }
        }

        [Fact]
        public void Reset_returns_to_curated_numbering()
        {
            using (var dir = new TempDir())
            {
                var e = Engine(dir, 0.45);
                e.SetStartLine();
                Move(e, 0.46, StartLineSetter.UndoSeconds + 1);
                e.ResetTurns();
                Assert.Equal(TurnSource.Curated, e.Catalog.Source);
                Assert.StartsWith("TURNS RESET", e.StartLine.Message);
            }
        }

        [Fact]
        public void Needs_turns_and_lap_position()
        {
            using (var dir = new TempDir())
            {
                var e = new TelemetryEngine(dir.Path, new FakeBundle());
                e.Update(EngineTestsAccess.Snapshot("nowhere", 0.2), new FakeRaw(), 0);
                e.SetStartLine();
                Assert.Equal("NO TURNS TO NUMBER YET", e.StartLine.Message);
            }
        }
    }
}
