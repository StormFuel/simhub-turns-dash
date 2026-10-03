using System.IO;
using System.Linq;
using TurnTelemetry.Core.Engine;
using TurnTelemetry.Core.Turns;
using Xunit;

namespace TurnTelemetry.Tests
{
    public class CuratedDataTests
    {
        private static readonly EmbeddedBundledData Bundle = new EmbeddedBundledData(typeof(TurnFile).Assembly);

        public static TheoryData<string, string, int> Curated => new TheoryData<string, string, int>
        {
            { "AssettoCorsaCompetizione", "Silverstone", 18 },
            { "AssettoCorsaCompetizione", "monza", 11 },
            { "AssettoCorsa", "ks_silverstone-gp", 18 },
            { "AssettoCorsa", "monza", 11 },
            { "AssettoCorsa", "fn_spa-gp", 19 },
            { "AssettoCorsa", "imola", 19 },
            { "AssettoCorsa", "ks_nurburgring-layout_gp_a", 15 },
            { "AssettoCorsa", "ks_barcelona-layout_gp", 16 },
            { "AssettoCorsa", "ks_laguna_seca", 12 },
            { "AssettoCorsa", "fn_lagunaseca", 12 },
            { "AssettoCorsa", "mugello", 15 },
            { "AssettoCorsa", "acu_unitedstates-a", 20 },
        };

        [Theory]
        [MemberData(nameof(Curated))]
        public void Curated_files_are_embedded_and_resolve_ahead_of_automatic_sources(string game, string track, int turns)
        {
            using (var dir = new TempDir())
            {
                var result = new TurnCatalog(dir.Path, Bundle).Resolve(game, track);

                Assert.Equal(TurnSource.Curated, result.Source);
                Assert.Equal(turns, result.Turns.Count);
                Assert.Equal(turns, result.Turns.Select(t => t.Label).Distinct().Count());
                Assert.All(result.Turns, t => Assert.False(string.IsNullOrEmpty(t.Name)));
            }
        }

        [Fact]
        public void Acc_silverstone_lap_starts_before_copse_turn_9()
        {
            using (var dir = new TempDir())
            {
                var engine = new TelemetryEngine(dir.Path, Bundle);
                engine.Update(EngineTestsAccess.Snapshot("Silverstone", 0.005), new FakeRaw(), 0);

                Assert.Equal(TurnSource.Curated, engine.Catalog.Source);
                Assert.Equal("NEXT", engine.TurnStateId);
                Assert.Equal("9", engine.Focal.Label);
                Assert.Equal("Copse", engine.Focal.Name);
                Assert.Equal("8", engine.Turn.Last.Label); // Woodcote, just before the National straight
            }
        }

        [Fact]
        public void Curation_specs_are_not_embedded()
        {
            Assert.Null(Bundle.Read("data/curation/AssettoCorsa/monza.json"));
        }

        [Fact]
        public void Empty_cloud_map_falls_back_to_the_local_map()
        {
            using (var dir = new TempDir())
            {
                var game = Path.Combine(dir.Path, "PluginsData", "AssettoCorsa");
                Directory.CreateDirectory(Path.Combine(game, "MapRecordsCloud"));
                Directory.CreateDirectory(Path.Combine(game, "MapRecords"));
                File.WriteAllBytes(Path.Combine(game, "MapRecordsCloud", "mugello-5196.84.shtl"), new byte[0]);
                File.WriteAllBytes(Path.Combine(game, "MapRecords", "mugello.shtl"), new byte[] { 1 });

                Assert.EndsWith(Path.Combine("MapRecords", "mugello.shtl"), TrackMapFile.Find(dir.Path, "AssettoCorsa", "mugello"));
            }
        }
    }
}

namespace TurnTelemetry.Tests
{
    public class TemplateTests
    {
        private const string SimHub = @"C:\Program Files (x86)\SimHub";
        private static readonly Core.Turns.EmbeddedBundledData Bundle = new Core.Turns.EmbeddedBundledData(typeof(Core.Turns.TurnFile).Assembly);

        [Fact]
        public void Templates_are_embedded()
        {
            var templates = Core.Turns.CircuitTemplates.LoadAll(Bundle);
            Assert.Contains(templates, t => t.Id == "spa" && t.Turns.Count == 19);
            Assert.True(templates.Count >= 8);
        }

        // On this machine's real maps: the AC template must re-create the hand-curated ACC file (skipped elsewhere).
        [Theory]
        [InlineData("Silverstone", "silverstone-gp")]
        [InlineData("monza", "monza")]
        public void Ac_template_recreates_curated_acc_turns(string accTrack, string templateId)
        {
            var path = Core.Turns.TrackMapFile.Find(SimHub, "AssettoCorsaCompetizione", accTrack);
            if (path == null) return;
            var corners = Core.Turns.TrackGeometry.Detect(Core.Turns.TrackMapFile.Load(path), out var length);
            var templates = Core.Turns.CircuitTemplates.LoadAll(Bundle);

            var match = Core.Turns.CircuitTemplates.Match(templates, corners, length);

            Assert.NotNull(match);
            Assert.Equal(templateId, match.Template.Id);
            var curated = new Core.Turns.TurnCatalog(System.IO.Path.GetTempPath(), Bundle)
                .Resolve("AssettoCorsaCompetizione", accTrack).Turns.ToDictionary(t => t.Label);
            foreach (var t in match.Turns)
            {
                var c = curated[t.Label];
                Assert.True(System.Math.Abs(Cyc(Mid(t) - Mid(c))) < 0.01, $"turn {t.Label}: {Mid(t):0.000} vs curated {Mid(c):0.000}");
            }
        }

        [Fact]
        public void Cota_template_matches_a_second_cota_mod_with_no_turn_data()
        {
            var path = Core.Turns.TrackMapFile.Find(SimHub, "AssettoCorsa", "cota_2022-gt");
            if (path == null) return;
            var corners = Core.Turns.TrackGeometry.Detect(Core.Turns.TrackMapFile.Load(path), out var length);
            var match = Core.Turns.CircuitTemplates.Match(Core.Turns.CircuitTemplates.LoadAll(Bundle), corners, length);
            Assert.NotNull(match);
            Assert.Equal("cota", match.Template.Id);
            Assert.Equal(20, match.Turns.Count);
            Assert.True(match.Score > 0.95, $"score {match.Score:0.00}");
        }

        [Fact]
        public void Wrong_circuit_of_the_same_length_is_rejected()
        {
            var path = Core.Turns.TrackMapFile.Find(SimHub, "AssettoCorsaCompetizione", "Silverstone");
            if (path == null) return;
            var corners = Core.Turns.TrackGeometry.Detect(Core.Turns.TrackMapFile.Load(path), out var length);
            var monzaOnly = Core.Turns.CircuitTemplates.LoadAll(Bundle).Where(t => t.Id == "monza");
            Assert.Null(Core.Turns.CircuitTemplates.Match(monzaOnly, corners, length));
        }

        private static double Mid(Core.Turns.TurnDefinition t) => (t.Start + ((t.End - t.Start + 1) % 1) / 2) % 1;
        private static double Cyc(double d) => ((d + 0.5) % 1 + 1) % 1 - 0.5;
    }
}
