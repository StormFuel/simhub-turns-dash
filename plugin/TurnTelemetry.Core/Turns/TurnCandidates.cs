using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TurnTelemetry.Core.Lap;

namespace TurnTelemetry.Core.Turns
{
    /// <summary>
    /// A possible turn shown in the turn editor. Nothing becomes a turn until the user reviews the list and saves it
    /// (docs/plan.md D3, amended 2026-10-03).
    /// </summary>
    public sealed class TurnCandidate
    {
        public bool Include = true;
        /// <summary>Turn number shown on the dashboard, e.g. "7" or "7A"; empty until numbered.</summary>
        public string Label = "";
        public string Name = "";
        public double Start;
        public double End;

        public double Length => End >= Start ? End - Start : 1 - Start + End;
    }

    public static class TurnNumbering
    {
        /// <summary>
        /// Numbers included candidates 1, 2, 3... in lap order, starting at <paramref name="firstIndex"/> and wrapping
        /// past start/finish. Excluded candidates get no number. Candidates must be sorted by Start.
        /// </summary>
        public static void AutoNumber(IList<TurnCandidate> candidates, int firstIndex)
        {
            if (candidates.Count == 0) return;
            firstIndex = Math.Max(0, Math.Min(candidates.Count - 1, firstIndex));
            var number = 1;
            for (var k = 0; k < candidates.Count; k++)
            {
                var c = candidates[(firstIndex + k) % candidates.Count];
                c.Label = c.Include ? (number++).ToString() : "";
            }
        }

        public static TurnFile ToFile(IEnumerable<TurnCandidate> candidates, TurnFile template)
        {
            template.Turns = candidates
                .Where(c => c.Include && !string.IsNullOrWhiteSpace(c.Label))
                .Select(c => new TurnFile.TurnEntry
                {
                    Label = c.Label.Trim(),
                    Name = string.IsNullOrWhiteSpace(c.Name) ? null : c.Name.Trim(),
                    Start = Math.Round(c.Start, 4),
                    End = Math.Round(c.End, 4),
                })
                .OrderBy(t => t.Start)
                .ToList();
            return template;
        }
    }

    /// <summary>Every section of an AC sections.ini as an editor candidate (names kept, numbers optional).</summary>
    public static class AcSectionCandidates
    {
        /// <summary>Sections whose names say they are not corners; unticked by default, the user can re-tick them.</summary>
        /// <remarks>Multilingual: AC track names are often local (Monza's main straight is "Rettifilo Partenza").</remarks>
        public static readonly Regex NotACorner = new Regex(
            @"straight|pit|start|finish|grid|rettifilo|partenza|arrivo|gerade|recta|ligne droite|reta\b|rechte",
            RegexOptions.IgnoreCase);

        private const double LineTolerance = 0.005;

        /// <summary>Sections in lap order; included ones are numbered 1..n from start/finish as a starting point.</summary>
        public static List<TurnCandidate> FromIni(string ini)
        {
            var list = AcSectionsReader.ParseAll(ini)
                .Select(s => new TurnCandidate
                {
                    Name = s.Text ?? "",
                    Label = TurnLabel.Parse(s.Text) ?? "",
                    Start = s.Start,
                    End = s.End,
                    Include = !NotACorner.IsMatch(s.Text ?? ""),
                })
                .OrderBy(c => c.Start)
                .ToList();
            MergeAcrossLine(list);
            if (list.All(c => string.IsNullOrEmpty(c.Label))) TurnNumbering.AutoNumber(list, 0);
            return list;
        }

        /// <summary>AC files often split a section at start/finish (e.g. "Main Straight" 0.997–1 and 0–0.058).</summary>
        private static void MergeAcrossLine(List<TurnCandidate> list)
        {
            var head = list.FirstOrDefault(c => c.Start <= LineTolerance);
            var tail = list.LastOrDefault(c => c.End >= 1 - LineTolerance);
            if (head == null || tail == null || ReferenceEquals(head, tail)) return;
            if (!string.Equals(head.Name.Trim(), tail.Name.Trim(), StringComparison.OrdinalIgnoreCase)) return;
            tail.End = head.End;
            list.Remove(head);
        }
    }

    /// <summary>
    /// Finds corners in a recorded lap from sustained steering (docs/architecture.md §6). Used only to propose
    /// candidates for the turn editor.
    /// </summary>
    public static class CornerDetector
    {
        /// <summary>Steering must exceed this fraction of the lap's 95th-percentile steering (and an absolute floor).</summary>
        public const double RelativeThreshold = 0.25;
        public const double MinimumSteer = 0.03;
        /// <summary>Gaps shorter than this (lap fraction) inside a corner are bridged.</summary>
        public const double MergeGap = 0.006;
        /// <summary>Shorter steering events are ignored as corrections.</summary>
        public const double MinimumLength = 0.004;
        /// <summary>How far a corner's start may extend back over the preceding braking zone.</summary>
        public const double MaxBrakeLead = 0.03;
        public const double BrakeOn = 0.1;

        public static List<TurnCandidate> Detect(LapTrace lap)
        {
            var result = new List<TurnCandidate>();
            if (lap == null) return result;
            var n = lap.Bins;

            var steer = new double[n];
            var brake = new double[n];
            var any = false;
            for (var i = 0; i < n; i++)
            {
                steer[i] = Fill(lap.Value(Channel.Steer, i));
                brake[i] = Fill(lap.Value(Channel.Brake, i));
                any |= Math.Abs(steer[i]) > 0;
            }
            if (!any) return result;
            steer = Smooth(steer, 2);

            var sorted = steer.Select(Math.Abs).OrderBy(v => v).ToArray();
            var p95 = sorted[(int)(0.95 * (n - 1))];
            var threshold = Math.Max(MinimumSteer, RelativeThreshold * p95);

            // Contiguous runs of steering above threshold, split where the direction flips (chicanes).
            var runs = new List<(int start, int end, int sign)>();
            var runStart = -1;
            var runSign = 0;
            for (var i = 0; i <= n; i++)
            {
                var active = i < n && Math.Abs(steer[i]) > threshold;
                var sign = i < n ? Math.Sign(steer[i]) : 0;
                if (runStart >= 0 && (!active || sign != runSign))
                {
                    runs.Add((runStart, i - 1, runSign));
                    runStart = -1;
                }
                if (active && runStart < 0)
                {
                    runStart = i;
                    runSign = sign;
                }
            }

            // Bridge short gaps between runs that turn the same way.
            var gap = (int)Math.Ceiling(MergeGap * n);
            var merged = new List<(int start, int end, int sign)>();
            foreach (var run in runs)
            {
                if (merged.Count > 0)
                {
                    var last = merged[merged.Count - 1];
                    if (last.sign == run.sign && run.start - last.end <= gap)
                    {
                        merged[merged.Count - 1] = (last.start, run.end, last.sign);
                        continue;
                    }
                }
                merged.Add(run);
            }

            var minBins = Math.Max(1, (int)Math.Ceiling(MinimumLength * n));
            var maxLead = (int)Math.Ceiling(MaxBrakeLead * n);
            foreach (var run in merged.Where(r => r.end - r.start + 1 >= minBins))
            {
                // Corner entry starts where braking for it starts (within MaxBrakeLead).
                var start = run.start;
                for (var k = 1; k <= maxLead && start - 1 >= 0 && brake[start - 1] > BrakeOn; k++) start--;
                result.Add(new TurnCandidate
                {
                    Start = start / (double)n,
                    End = Math.Min(1.0, (run.end + 1) / (double)n),
                    Name = run.sign > 0 ? "right" : "left",
                });
            }
            return result;
        }

        private static double Fill(double v) => double.IsNaN(v) ? 0 : v;

        private static double[] Smooth(double[] v, int radius)
        {
            var result = new double[v.Length];
            for (var i = 0; i < v.Length; i++)
            {
                double sum = 0;
                var count = 0;
                for (var k = -radius; k <= radius; k++)
                {
                    var j = i + k;
                    if (j < 0 || j >= v.Length) continue;
                    sum += v[j];
                    count++;
                }
                result[i] = sum / count;
            }
            return result;
        }
    }
}
