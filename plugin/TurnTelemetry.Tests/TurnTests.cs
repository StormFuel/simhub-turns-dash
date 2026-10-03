using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TurnTelemetry.Core.Lap;
using TurnTelemetry.Core.Sims;
using TurnTelemetry.Core.Turns;
using Xunit;

namespace TurnTelemetry.Tests
{
    public class TurnLabelTests
    {
        [Theory]
        [InlineData("Turn 7", "7")]
        [InlineData("Turn 7A", "7A")]
        [InlineData("turn 7a", "7A")]
        [InlineData("T7", "7")]
        [InlineData("T7 - The Kink", "7")]
        [InlineData("Corner 7", "7")]
        [InlineData("Turn #12", "12")]
        [InlineData("T-3", "3")]
        [InlineData("Turn 07", "7")]
        [InlineData("  Turn 4  ", "4")]
        [InlineData("Turn 7 Abbey", "7")]
        public void Parses_explicitly_numbered_labels(string label, string expected)
        {
            Assert.Equal(expected, TurnLabel.Parse(label));
        }

        [Theory]
        [InlineData("La Source")]
        [InlineData("Eau Rouge")]
        [InlineData("Tamburello")]
        [InlineData("Kemmel Straight")]
        [InlineData("")]
        [InlineData(null)]
        public void Never_guesses_numbers_for_names(string label)
        {
            Assert.Null(TurnLabel.Parse(label));
        }
    }

    public class TurnTrackerTests
    {
        private static readonly List<TurnDefinition> Turns = new List<TurnDefinition>
        {
            new TurnDefinition { Label = "1", Start = 0.10, End = 0.15 },
            new TurnDefinition { Label = "2", Start = 0.40, End = 0.45 },
            new TurnDefinition { Label = "3", Start = 0.95, End = 0.02 }, // wraps across start/finish
        };

        [Fact]
        public void Before_first_turn_next_is_turn_1_and_last_is_the_wrapping_turn()
        {
            var s = TurnTracker.At(Turns, 0.05);
            Assert.Null(s.Current);
            Assert.Equal("1", s.Next.Label);
            Assert.Equal("3", s.Last.Label);
        }

        [Fact]
        public void Inside_a_turn_it_is_current_and_excluded_from_last_and_next()
        {
            var s = TurnTracker.At(Turns, 0.12);
            Assert.Equal("1", s.Current.Label);
            Assert.Equal("2", s.Next.Label);
            Assert.Equal("3", s.Last.Label);
        }

        [Theory]
        [InlineData(0.97)]
        [InlineData(0.01)]
        public void Wrapping_turn_is_current_on_both_sides_of_the_line(double pos)
        {
            Assert.Equal("3", TurnTracker.At(Turns, pos).Current.Label);
        }

        [Fact]
        public void Range_end_is_exclusive()
        {
            var s = TurnTracker.At(Turns, 0.15);
            Assert.Null(s.Current);
            Assert.Equal("1", s.Last.Label);
        }

        [Fact]
        public void No_turns_gives_empty_state()
        {
            var s = TurnTracker.At(new List<TurnDefinition>(), 0.5);
            Assert.Null(s.Current);
            Assert.Null(s.Next);
            Assert.Null(s.Last);
        }

        [Fact]
        public void Single_turn_is_both_last_and_next_when_outside_it()
        {
            var one = new List<TurnDefinition> { new TurnDefinition { Label = "1", Start = 0.3, End = 0.4 } };
            var s = TurnTracker.At(one, 0.6);
            Assert.Equal("1", s.Next.Label);
            Assert.Equal("1", s.Last.Label);
        }
    }

    public class AcSectionsReaderTests
    {
        // Excerpt of the real assettocorsa/content/tracks/spa/data/sections.ini: names only.
        private const string Spa = "[SECTION_0]\r\nIN=0.038\r\nOUT=0.068\r\nTEXT=La Source\r\n\r\n[SECTION_1]\r\nIN=0.137\r\nOUT=0.154\r\nTEXT=Eau Rouge\r\n";

        private const string Numbered = @"
; comment
[SECTION_0]
IN=0.50
OUT=0.55
TEXT=Turn 2

[SECTION_1]
IN=0.10
OUT=0.15
TEXT=T1 - Hairpin

[SECTION_2]
IN=0.70
OUT=0.80
TEXT=Back Straight
";

        [Fact]
        public void Name_only_sections_produce_no_turns()
        {
            var result = AcSectionsReader.Parse(Spa);
            Assert.Equal(2, result.SectionCount);
            Assert.Empty(result.Turns);
        }

        [Fact]
        public void Numbered_sections_are_parsed_and_sorted_by_start()
        {
            var result = AcSectionsReader.Parse(Numbered);
            Assert.Equal(3, result.SectionCount);
            Assert.Equal(new[] { "1", "2" }, result.Turns.Select(t => t.Label));
            Assert.Equal(0.10, result.Turns[0].Start, 6);
            Assert.Equal("T1 - Hairpin", result.Turns[0].Name);
        }
    }

    public sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tt-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }
        public void Dispose() => Directory.Delete(Path, true);
    }

    public sealed class FakeBundle : IBundledData
    {
        public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
        public string Read(string relativePath) => Files.TryGetValue(relativePath, out var v) ? v : null;
        public IEnumerable<string> List(string prefix) => Files.Keys.Where(k => k.StartsWith(prefix)).ToList();
    }

    public class TurnCatalogTests
    {
        private static string TurnJson(string label) =>
            new TurnFile { Turns = { new TurnFile.TurnEntry { Label = label, Start = 0.1, End = 0.2 } } }.ToJson();

        [Fact]
        public void User_file_beats_curated_file()
        {
            using (var dir = new TempDir())
            {
                var bundle = new FakeBundle();
                bundle.Files["data/turns/Game/track.json"] = TurnJson("curated");
                var catalog = new TurnCatalog(dir.Path, bundle);

                Assert.Equal(TurnSource.Curated, catalog.Resolve("Game", "track").Source);

                var userPath = catalog.UserFilePath("Game", "track");
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(userPath));
                File.WriteAllText(userPath, TurnJson("user"));

                var result = catalog.Resolve("Game", "track");
                Assert.Equal(TurnSource.User, result.Source);
                Assert.Equal("user", result.Turns[0].Label);
            }
        }

        [Fact]
        public void Ac_sections_used_only_for_assetto_corsa()
        {
            using (var dir = new TempDir())
            {
                var data = System.IO.Path.Combine(dir.Path, "ac", "content", "tracks", "mytrack", "gp", "data");
                Directory.CreateDirectory(data);
                File.WriteAllText(System.IO.Path.Combine(data, "sections.ini"), "[S]\nIN=0.1\nOUT=0.2\nTEXT=Turn 1\n");
                var catalog = new TurnCatalog(System.IO.Path.Combine(dir.Path, "user"), new FakeBundle());
                var folder = new TrackFolder { Track = "mytrack", Layout = "gp" };
                var acRoot = System.IO.Path.Combine(dir.Path, "ac");

                Assert.Equal(TurnSource.AcSections, catalog.Resolve(SimAdapters.AssettoCorsa, "mytrack-gp", acRoot, folder).Source);
                Assert.Equal(TurnSource.None, catalog.Resolve(SimAdapters.AssettoCorsaCompetizione, "mytrack-gp", acRoot, folder).Source);
            }
        }

        [Fact]
        public void Unknown_track_is_unsupported()
        {
            using (var dir = new TempDir())
            {
                var result = new TurnCatalog(dir.Path, new FakeBundle()).Resolve("Game", "nowhere");
                Assert.False(result.Supported);
                Assert.Equal("none", CatalogResult.SourceId(result.Source));
            }
        }

        [Fact]
        public void Track_ids_are_sanitised_for_file_names()
        {
            Assert.Equal("turns/Game/a_b_c.json", TrackKey.RelativePath("Game", "a/b:c"));
        }
    }

    public class TurnCandidateTests
    {
        // Real AC knr_silverstone_2005/gp sections.ini excerpt: names only, straights included, main straight split.
        private const string Silverstone = @"
[SECTION_0]
IN=0.997
OUT=1
TEXT=Main Straight
[SECTION_1]
IN=0
OUT=0.058
TEXT=Main Straight
[SECTION_2]
IN=0.061
OUT=0.120
TEXT=Copse Corner
[SECTION_3]
IN=0.168
OUT=0.217
TEXT=Maggots Corner
[SECTION_4]
IN=0.317
OUT=0.433
TEXT=Hangar Straight
[SECTION_5]
IN=0.440
OUT=0.492
TEXT=Stowe Corner
";

        [Fact]
        public void Ac_sections_become_candidates_with_straights_unticked_and_the_line_split_merged()
        {
            var list = AcSectionCandidates.FromIni(Silverstone);

            Assert.Equal(new[] { "Copse Corner", "Maggots Corner", "Hangar Straight", "Stowe Corner", "Main Straight" },
                list.Select(c => c.Name));
            var main = list.Single(c => c.Name == "Main Straight");
            Assert.Equal(0.997, main.Start, 6);
            Assert.Equal(0.058, main.End, 6);
            Assert.False(main.Include);
            Assert.False(list.Single(c => c.Name == "Hangar Straight").Include);
            Assert.True(list.Single(c => c.Name == "Copse Corner").Include);
        }

        [Theory]
        [InlineData("Rettifilo Partenza")]     // AC Monza: numbered as a turn on 2026-10-03 before this fix
        [InlineData("Döttinger Höhe Gerade")]
        [InlineData("Recta principal")]
        public void Straights_are_recognised_in_other_languages(string name)
        {
            Assert.Matches(AcSectionCandidates.NotACorner, name);
        }

        [Fact]
        public void Auto_number_starts_at_the_chosen_turn_skips_unticked_and_wraps()
        {
            var list = AcSectionCandidates.FromIni(Silverstone);
            TurnNumbering.AutoNumber(list, 1); // first turn = Maggots

            Assert.Equal("1", list.Single(c => c.Name == "Maggots Corner").Label);
            Assert.Equal("2", list.Single(c => c.Name == "Stowe Corner").Label);
            Assert.Equal("3", list.Single(c => c.Name == "Copse Corner").Label); // wrapped past the line
            Assert.Equal("", list.Single(c => c.Name == "Hangar Straight").Label);
        }

        [Fact]
        public void Only_included_numbered_candidates_are_saved()
        {
            var list = AcSectionCandidates.FromIni(Silverstone);
            TurnNumbering.AutoNumber(list, 0);
            list.Single(c => c.Name == "Stowe Corner").Label = "15";

            var file = TurnNumbering.ToFile(list, new TurnFile());

            Assert.Equal(new[] { "1", "2", "15" }, file.Turns.Select(t => t.Label));
            Assert.Equal("Stowe Corner", file.Turns[2].Name);
        }

        private static LapTrace SyntheticLap(Func<double, double> steer, Func<double, double> brake = null, int bins = 400)
        {
            var lap = new LapTrace(bins);
            for (var i = 0; i < bins; i++)
            {
                var pos = (i + 0.5) / bins;
                lap.Add(i, new Frame { LapPos = pos, Steer = steer(pos), Brake = brake?.Invoke(pos) ?? 0, Throttle = 0.5 });
            }
            return lap;
        }

        private static double Bump(double pos, double centre, double width, double height) =>
            Math.Abs(pos - centre) < width / 2 ? height : 0;

        [Fact]
        public void Detects_corners_splits_chicanes_and_ignores_small_corrections()
        {
            var lap = SyntheticLap(p =>
                Bump(p, 0.10, 0.04, 0.30)                          // right-hander
                + Bump(p, 0.40, 0.02, -0.25) + Bump(p, 0.42, 0.02, 0.25) // left-right chicane
                + Bump(p, 0.70, 0.002, 0.12)                       // small correction
                + Bump(p, 0.85, 0.05, -0.20)                       // long left
                + 0.005 * Math.Sin(p * 200));                      // noise

            var corners = CornerDetector.Detect(lap);

            Assert.Equal(new[] { "right", "left", "right", "left" }, corners.Select(c => c.Name));
            Assert.InRange(corners[0].Start, 0.07, 0.09);
            Assert.InRange(corners[0].End, 0.11, 0.13);
        }

        [Fact]
        public void Corner_entry_extends_back_over_the_braking_zone()
        {
            var lap = SyntheticLap(p => Bump(p, 0.50, 0.04, 0.3), p => p > 0.46 && p < 0.485 ? 0.8 : 0);
            var corner = CornerDetector.Detect(lap).Single();
            Assert.InRange(corner.Start, 0.455, 0.47);
        }

        [Fact]
        public void No_steering_means_no_candidates()
        {
            Assert.Empty(CornerDetector.Detect(SyntheticLap(p => double.NaN)));
        }
    }

    public class TurnRecorderTests
    {
        [Fact]
        public void Labels_continue_from_highest_number_and_turns_stay_sorted()
        {
            var file = new TurnFile();
            file.Turns.Add(new TurnFile.TurnEntry { Label = "7A", Start = 0.5, End = 0.55 });
            var recorder = new TurnRecorder();

            Assert.Null(recorder.MarkEnd(0.3, file)); // no start yet

            recorder.MarkStart(0.10);
            var added = recorder.MarkEnd(0.12, file);

            Assert.Equal("8", added.Label);
            Assert.Equal(new[] { "8", "7A" }, file.Turns.Select(t => t.Label));
            Assert.False(recorder.HasPendingStart);
        }
    }
}
