using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TurnTelemetry.Core.Turns
{
    /// <summary>One point of a SimHub track map: world X/Z (metres) at lap fraction P.</summary>
    public struct MapPoint
    {
        public double P;
        public double X;
        public double Z;
    }

    /// <summary>
    /// SimHub track maps: PluginsData/{GameName}/MapRecords[Cloud]/{TrackIdWithConfig}[-{length}].shtl, gzipped JSON whose
    /// CarCoordinates are world positions tagged with "p", the lap fraction in that game's own TrackPositionPercent scale.
    /// </summary>
    public static class TrackMapFile
    {
        /// <summary>Prefers the cloud map (averaged over many laps) over the locally recorded one.</summary>
        public static string Find(string simHubRoot, string game, string track)
        {
            if (string.IsNullOrEmpty(simHubRoot) || string.IsNullOrEmpty(game) || string.IsNullOrEmpty(track)) return null;
            var root = Path.Combine(simHubRoot, "PluginsData", game);
            var cloudDir = Path.Combine(root, "MapRecordsCloud");
            if (Directory.Exists(cloudDir))
            {
                // Cloud files can be empty (seen: AC mugello-5196.84.shtl, 0 bytes); skip them.
                var cloud = Directory.GetFiles(cloudDir, track + "-*.shtl")
                    .Where(f => IsLengthSuffix(Path.GetFileNameWithoutExtension(f).Substring(track.Length + 1)))
                    .Where(f => new FileInfo(f).Length > 0)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (cloud != null) return cloud;
            }
            var local = Path.Combine(root, "MapRecords", track + ".shtl");
            return File.Exists(local) ? local : null;
        }

        // "Silverstone-5891" is Silverstone's cloud map; "ks_silverstone-gp-5802.1" must not match track "ks_silverstone".
        private static bool IsLengthSuffix(string suffix) =>
            double.TryParse(suffix, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);

        public static List<MapPoint> Load(string path)
        {
            using (var file = File.OpenRead(path))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            using (var reader = new JsonTextReader(new StreamReader(gzip)))
            {
                var root = JObject.Load(reader);
                var points = new List<MapPoint>();
                foreach (var c in root["CarCoordinates"] ?? new JArray())
                {
                    var v = c["Value"] as JArray;
                    if (v == null || v.Count < 3 || c["p"] == null) continue;
                    points.Add(new MapPoint { P = (double)c["p"], X = (double)v[0], Z = (double)v[2] });
                }
                return points.OrderBy(p => p.P).ToList();
            }
        }
    }

    /// <summary>
    /// Finds corners from track-map geometry: curvature along the lap with hysteresis, split at direction changes and at
    /// clear dips between two apexes. Port of tools/trackmap.py (tuned on AC and ACC Silverstone; keep the two in step).
    /// </summary>
    public static class TrackGeometry
    {
        public const double StepM = 2.0;
        public const double SmoothM = 24.0;
        public const double EnterRadiusM = 350.0;
        public const double ExitRadiusM = 700.0;
        public const double MinTurnDeg = 18.0;
        public const double MergeGapM = 25.0;
        public const double SplitRatio = 0.45;
        public const double SplitMinM = 40.0;

        public sealed class Corner
        {
            public double Start, End, Apex;
            /// <summary>True for a right-hander. SimHub world coordinates are left-handed (validated on AC Silverstone).</summary>
            public bool Right;
            public double Degrees;
            public double MinRadiusM;
        }

        public static List<Corner> Detect(IReadOnlyList<MapPoint> points, out double lengthM)
        {
            lengthM = 0;
            var result = new List<Corner>();
            if (points == null || points.Count < 50) return result;

            var samples = Resample(points, out lengthM);
            var n = samples.Count;
            var k = Curvature(samples);

            double enter = 1 / EnterRadiusM, leave = 1 / ExitRadiusM;
            var regions = new List<int[]>();
            for (var i = 0; i < n;)
            {
                if (Math.Abs(k[i]) <= enter)
                {
                    i++;
                    continue;
                }
                var sign = Math.Sign(k[i]);
                var a = i;
                while (a > 0 && k[a - 1] * sign > leave && (regions.Count == 0 || a - 1 > regions[regions.Count - 1][1])) a--;
                var b = i;
                while (b + 1 < n && k[b + 1] * sign > leave) b++;
                regions.Add(new[] { a, b, sign });
                i = b + 1;
            }

            var gap = (int)(MergeGapM / StepM);
            var merged = new List<int[]>();
            foreach (var r in regions)
            {
                var last = merged.LastOrDefault();
                if (last != null && last[2] == r[2] && r[0] - last[1] <= gap) last[1] = r[1];
                else merged.Add(r);
            }

            var minLen = (int)(SplitMinM / StepM);
            foreach (var r in merged.SelectMany(r => SplitDoubleApex(k, r[0], r[1], r[2], minLen)))
            {
                int a = r.Item1, b = r.Item2;
                double turn = 0, peak = 0;
                var apex = a;
                for (var j = a; j <= b; j++)
                {
                    turn += k[j];
                    if (Math.Abs(k[j]) > peak)
                    {
                        peak = Math.Abs(k[j]);
                        apex = j;
                    }
                }
                var degrees = Math.Abs(turn) * StepM * 180 / Math.PI;
                if (degrees < MinTurnDeg) continue;
                result.Add(new Corner
                {
                    Start = samples[a].P, End = samples[b].P, Apex = samples[apex].P,
                    Right = r.Item3 > 0, Degrees = degrees, MinRadiusM = 1 / peak,
                });
            }
            return result;
        }

        /// <summary>Corners as editor candidates, numbered 1..n from start/finish.</summary>
        public static List<TurnCandidate> ToCandidates(IEnumerable<Corner> corners)
        {
            var list = corners.Select(c => new TurnCandidate
            {
                Start = c.Start, End = c.End,
                Name = $"{(c.Right ? "right" : "left")} {c.Degrees:0}°",
            }).ToList();
            TurnNumbering.AutoNumber(list, 0);
            return list;
        }

        private static List<MapPoint> Resample(IReadOnlyList<MapPoint> pts, out double total)
        {
            var dist = new double[pts.Count];
            for (var i = 1; i < pts.Count; i++)
                dist[i] = dist[i - 1] + Math.Sqrt(Sq(pts[i].X - pts[i - 1].X) + Sq(pts[i].Z - pts[i - 1].Z));
            total = dist[pts.Count - 1] + Math.Sqrt(Sq(pts[0].X - pts[pts.Count - 1].X) + Sq(pts[0].Z - pts[pts.Count - 1].Z));

            var output = new List<MapPoint>();
            var j = 0;
            for (var s = 0.0; s < dist[pts.Count - 1]; s += StepM)
            {
                while (dist[j + 1] < s) j++;
                var t = (s - dist[j]) / Math.Max(1e-9, dist[j + 1] - dist[j]);
                MapPoint a = pts[j], b = pts[j + 1];
                output.Add(new MapPoint { P = a.P + (b.P - a.P) * t, X = a.X + (b.X - a.X) * t, Z = a.Z + (b.Z - a.Z) * t });
            }
            return output;
        }

        private static double[] Curvature(List<MapPoint> s)
        {
            var n = s.Count;
            var heading = new double[n];
            for (var i = 0; i < n; i++)
            {
                MapPoint a = s[(i - 1 + n) % n], b = s[(i + 1) % n];
                heading[i] = Math.Atan2(b.Z - a.Z, b.X - a.X);
            }
            var raw = new double[n];
            for (var i = 0; i < n; i++)
            {
                var d = heading[(i + 1) % n] - heading[(i - 1 + n) % n];
                d = ((d + Math.PI) % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI) - Math.PI;
                raw[i] = d / (2 * StepM);
            }
            var w = Math.Max(1, (int)(SmoothM / StepM / 2));
            var k = new double[n];
            for (var i = 0; i < n; i++)
            {
                double sum = 0;
                for (var j = -w; j <= w; j++) sum += raw[((i + j) % n + n) % n];
                k[i] = sum / (2 * w + 1);
            }
            return k;
        }

        private static IEnumerable<Tuple<int, int, int>> SplitDoubleApex(double[] k, int a, int b, int sign, int minLen)
        {
            if (b - a < 2 * minLen) return new[] { Tuple.Create(a, b, sign) };
            int best = -1;
            for (var m = a + minLen; m <= b - minLen; m++)
            {
                double left = 0, right = 0;
                for (var j = a; j < m; j++) left = Math.Max(left, Math.Abs(k[j]));
                for (var j = m + 1; j <= b; j++) right = Math.Max(right, Math.Abs(k[j]));
                var dip = Math.Abs(k[m]);
                if (dip < SplitRatio * Math.Min(left, right) && (best < 0 || dip < Math.Abs(k[best]))) best = m;
            }
            if (best < 0) return new[] { Tuple.Create(a, b, sign) };
            return SplitDoubleApex(k, a, best, sign, minLen).Concat(SplitDoubleApex(k, best + 1, b, sign, minLen));
        }

        private static double Sq(double v) => v * v;
    }
}
