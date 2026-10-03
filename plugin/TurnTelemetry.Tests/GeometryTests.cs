using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json.Linq;
using TurnTelemetry.Core.Engine;
using TurnTelemetry.Core.Turns;
using Xunit;

namespace TurnTelemetry.Tests
{
    public class GeometryTests
    {
        /// <summary>
        /// Walks a path made of straights and constant-radius arcs. Positive degrees turn the heading in the +atan2(dz, dx)
        /// direction, which SimHub's left-handed coordinates make a right-hander.
        /// </summary>
        private static List<MapPoint> Path(params (double length, double degrees)[] segments)
        {
            var pts = new List<(double x, double z)>();
            double x = 0, z = 0, heading = 0;
            foreach (var (length, degrees) in segments)
            {
                var steps = (int)Math.Ceiling(length / 1.0);
                var turn = degrees * Math.PI / 180 / steps;
                for (var i = 0; i < steps; i++)
                {
                    heading += turn;
                    x += Math.Cos(heading) * length / steps;
                    z += Math.Sin(heading) * length / steps;
                    pts.Add((x, z));
                }
            }
            return pts.Select((p, i) => new MapPoint { P = i / (double)pts.Count, X = p.x, Z = p.z }).ToList();
        }

        [Fact]
        public void Stadium_has_two_hairpins_in_the_same_direction()
        {
            // 600 m straights and two 180-degree hairpins of radius ~64 m (200 m of arc).
            var map = Path((600, 0), (200, 180), (600, 0), (200, 180));
            var corners = TrackGeometry.Detect(map, out var length);

            Assert.InRange(length, 1550, 1650);
            Assert.Equal(2, corners.Count);
            Assert.All(corners, c => Assert.True(c.Right));
            Assert.All(corners, c => Assert.InRange(c.Degrees, 160, 200));
            Assert.InRange(corners[0].Apex, 0.40, 0.48);
        }

        [Fact]
        public void Chicane_is_split_and_gentle_bends_are_ignored()
        {
            var map = Path((500, 0), (60, 50), (60, -50), (500, 0), (400, 10) /* 10 deg over 400 m: not a turn */,
                (300, 0), (150, 170), (300, 0), (150, 190 - 10));
            var corners = TrackGeometry.Detect(map, out _);

            // The synthetic path does not close exactly, so only check the regions under test (total 2420 m).
            var chicane = corners.Where(c => c.Apex > 500 / 2420.0 - 0.02 && c.Apex < 620 / 2420.0 + 0.02).ToList();
            Assert.Equal(new[] { true, false }, chicane.Select(c => c.Right));
            Assert.DoesNotContain(corners, c => c.Apex > 1080 / 2420.0 && c.Apex < 1440 / 2420.0);
        }

        [Fact]
        public void Candidates_are_numbered_from_start_finish()
        {
            var map = Path((600, 0), (200, 180), (600, 0), (200, 180));
            var list = TrackGeometry.ToCandidates(TrackGeometry.Detect(map, out _));
            Assert.Equal(new[] { "1", "2" }, list.Select(c => c.Label));
            Assert.StartsWith("right", list[0].Name);
        }

        private static void WriteMap(string path, IEnumerable<MapPoint> points)
        {
            var json = new JObject
            {
                ["CarCoordinates"] = new JArray(points.Select(p => new JObject
                    { ["Value"] = new JArray(p.X, 0.0, p.Z), ["p"] = p.P })),
            };
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            using (var file = File.Create(path))
            using (var gzip = new GZipStream(file, CompressionMode.Compress))
            using (var writer = new StreamWriter(gzip))
                writer.Write(json.ToString());
        }

        [Fact]
        public void Map_lookup_prefers_cloud_and_does_not_match_longer_track_ids()
        {
            using (var dir = new TempDir())
            {
                var game = System.IO.Path.Combine(dir.Path, "PluginsData", "AssettoCorsa");
                var map = Path((600, 0), (200, 180), (600, 0), (200, 180));
                WriteMap(System.IO.Path.Combine(game, "MapRecords", "ks_silverstone.shtl"), map);
                WriteMap(System.IO.Path.Combine(game, "MapRecordsCloud", "ks_silverstone-gp-5802.1.shtl"), map);

                // "ks_silverstone-gp-5802.1" belongs to track "ks_silverstone-gp", not "ks_silverstone".
                Assert.EndsWith(System.IO.Path.Combine("MapRecords", "ks_silverstone.shtl"),
                    TrackMapFile.Find(dir.Path, "AssettoCorsa", "ks_silverstone"));
                Assert.EndsWith("ks_silverstone-gp-5802.1.shtl", TrackMapFile.Find(dir.Path, "AssettoCorsa", "ks_silverstone-gp"));
                Assert.Equal(map.Count, TrackMapFile.Load(TrackMapFile.Find(dir.Path, "AssettoCorsa", "ks_silverstone")).Count);
            }
        }

        [Fact]
        public void Track_without_turn_files_gets_an_automatic_base_from_the_simhub_map()
        {
            using (var dir = new TempDir())
            {
                WriteMap(System.IO.Path.Combine(dir.Path, "PluginsData", "AssettoCorsaCompetizione", "MapRecordsCloud", "monza-5793.shtl"),
                    Path((600, 0), (200, 180), (600, 0), (200, 180)));
                var engine = new TelemetryEngine(System.IO.Path.Combine(dir.Path, "user"), new FakeBundle(),
                    new EngineOptions { SimHubRoot = dir.Path });
                engine.Update(EngineTestsAccess.Snapshot("monza", 0.05), new FakeRaw(), 0);

                Assert.Equal(TurnSource.AutoMap, engine.Catalog.Source);
                Assert.Equal(2, engine.Catalog.Turns.Count);
                Assert.Equal("NEXT", engine.TurnStateId);
                Assert.Equal("1", engine.Focal.Label);
            }
        }

        // Parity with tools/trackmap.py on the real SimHub maps of this machine (skipped elsewhere).
        [Theory]
        [InlineData("AssettoCorsaCompetizione", "Silverstone", 14)]
        [InlineData("AssettoCorsa", "ks_silverstone-gp", 16)]
        public void Matches_the_python_prototype_on_real_maps(string game, string track, int expected)
        {
            var path = TrackMapFile.Find(@"C:\Program Files (x86)\SimHub", game, track);
            if (path == null) return;
            Assert.Equal(expected, TrackGeometry.Detect(TrackMapFile.Load(path), out _).Count);
        }
    }

    internal static class EngineTestsAccess
    {
        public static Core.Input.GameSnapshot Snapshot(string track, double pos) => new Core.Input.GameSnapshot
        {
            GameName = Core.Sims.SimAdapters.AssettoCorsaCompetizione,
            TrackIdWithConfig = track,
            CarId = "car",
            SessionTypeName = "PRACTICE",
            TrackPositionPercent = pos,
        };
    }
}
