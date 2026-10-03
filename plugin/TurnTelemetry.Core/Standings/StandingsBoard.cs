using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TurnTelemetry.Core.Standings
{
    /// <summary>One car in the session, copied from SimHub's opponent list (the player included).</summary>
    public sealed class DriverEntry
    {
        public int Position;
        public string Name;
        public bool IsPlayer;
        /// <summary>Best lap in seconds; 0 or less when the driver has no lap yet.</summary>
        public double BestLap;
        /// <summary>Best individual sector times in seconds (any lap); null when not set.</summary>
        public double? BestS1, BestS2, BestS3;
        /// <summary>Gap to the leader in seconds, and whole laps behind the leader.</summary>
        public double? GapToLeader;
        public int? LapsToLeader;
    }

    /// <summary>A dashboard row, pre-formatted so the dashboard only binds text and flags.</summary>
    public sealed class StandingsRow
    {
        public bool Visible;
        public int Position;
        public string Name = "";
        public bool IsPlayer;
        public string[] Sectors = { "", "", "" };
        /// <summary>True when this driver's best sector is the session's fastest (purple).</summary>
        public bool[] SectorFastest = new bool[3];
        public string Best = "";
        public bool BestFastest;
        /// <summary>Best lap against the session's fastest best lap.</summary>
        public string BestDelta = "";
        public string Gap = "";
    }

    /// <summary>
    /// A fixed-size standings window for the dashboard: the top of the order while the player is near the front,
    /// otherwise the leader followed by the cars around the player.
    /// </summary>
    public sealed class StandingsBoard
    {
        public const int RowCount = 6;

        public StandingsRow[] Rows { get; } = Enumerable.Range(0, RowCount).Select(_ => new StandingsRow()).ToArray();
        public int DriverCount { get; private set; }

        private readonly double[] _fastestSector = new double[3];
        private readonly double[] _playerSector = new double[3];

        /// <summary>Your best time for 1-based sector <paramref name="n"/> as the game reports it, or NaN when unknown.</summary>
        public double PlayerBestSector(int n) => n >= 1 && n <= 3 && _playerSector[n - 1] > 0 ? _playerSector[n - 1] : double.NaN;

        /// <summary>Fastest best time for 1-based sector <paramref name="n"/> by any driver, or NaN when unknown.</summary>
        public double FastestSector(int n) => n >= 1 && n <= 3 && _fastestSector[n - 1] > 0 ? _fastestSector[n - 1] : double.NaN;

        public void Clear()
        {
            DriverCount = 0;
            Array.Clear(_fastestSector, 0, 3);
            Array.Clear(_playerSector, 0, 3);
            foreach (var row in Rows) row.Visible = false;
        }

        public void Update(IReadOnlyList<DriverEntry> drivers)
        {
            var order = (drivers ?? Array.Empty<DriverEntry>()).Where(d => d != null && d.Position > 0)
                .OrderBy(d => d.Position).ToList();
            DriverCount = order.Count;

            var fastestLap = order.Where(d => d.BestLap > 0).Select(d => d.BestLap).DefaultIfEmpty(0).Min();
            var fastestSector = _fastestSector;
            for (var s = 0; s < 3; s++)
                fastestSector[s] = order.Select(d => Seconds(Sector(d, s))).Where(v => v > 0).DefaultIfEmpty(0).Min();
            var player = order.FirstOrDefault(d => d.IsPlayer);
            for (var s = 0; s < 3; s++)
                _playerSector[s] = player == null ? 0 : Seconds(Sector(player, s));

            var window = Window(order);
            for (var i = 0; i < RowCount; i++)
            {
                var row = Rows[i];
                row.Visible = i < window.Count;
                if (!row.Visible) continue;
                var d = window[i];
                row.Position = d.Position;
                row.Name = (d.Name ?? "").Trim().ToUpperInvariant();
                row.IsPlayer = d.IsPlayer;
                for (var s = 0; s < 3; s++)
                {
                    var t = Seconds(Sector(d, s));
                    row.Sectors[s] = t > 0 ? FormatTime(t) : "";
                    row.SectorFastest[s] = t > 0 && Math.Abs(t - fastestSector[s]) < 0.0005;
                }
                row.Best = d.BestLap > 0 ? FormatTime(d.BestLap) : "";
                row.BestFastest = d.BestLap > 0 && Math.Abs(d.BestLap - fastestLap) < 0.0005;
                row.BestDelta = d.BestLap <= 0 ? "" : row.BestFastest ? "FASTEST" : "+" + (d.BestLap - fastestLap).ToString("0.000", CultureInfo.InvariantCulture);
                row.Gap = FormatGap(d, order[0]);
            }
        }

        /// <summary>Top rows while the player is among them; otherwise the leader plus the cars around the player.</summary>
        public static List<DriverEntry> Window(List<DriverEntry> order)
        {
            var player = order.FindIndex(d => d.IsPlayer);
            if (player < RowCount) return order.Take(RowCount).ToList();
            var around = RowCount - 1;
            var start = Math.Max(1, Math.Min(player - around / 2, order.Count - around));
            return new[] { order[0] }.Concat(order.Skip(start).Take(around)).ToList();
        }

        public static string FormatTime(double seconds)
        {
            var minutes = (int)(seconds / 60);
            var rest = seconds - minutes * 60;
            return minutes > 0
                ? minutes + ":" + rest.ToString("00.000", CultureInfo.InvariantCulture)
                : rest.ToString("0.000", CultureInfo.InvariantCulture);
        }

        private static string FormatGap(DriverEntry d, DriverEntry leader)
        {
            if (d == leader) return "LEADER";
            if (d.LapsToLeader >= 1) return "+" + d.LapsToLeader + (d.LapsToLeader == 1 ? " LAP" : " LAPS");
            return d.GapToLeader > 0 ? "+" + d.GapToLeader.Value.ToString("0.0", CultureInfo.InvariantCulture) : "";
        }

        private static double? Sector(DriverEntry d, int s) => s == 0 ? d.BestS1 : s == 1 ? d.BestS2 : d.BestS3;

        /// <summary>SimHub reports sectors in seconds; a value over 1000 can only be milliseconds, so convert it.</summary>
        private static double Seconds(double? value) => value == null || value <= 0 ? 0 : value > 1000 ? value.Value / 1000 : value.Value;
    }
}
