using System;
using System.Reflection;
using SimHub.Plugins;
using TurnTelemetry.Core.Engine;
using TurnTelemetry.Core.Lap;
using TurnTelemetry.Core.Standings;
using TurnTelemetry.Core.Turns;
using TurnTelemetry.Core.Tyres;

namespace TurnTelemetryHost
{
    /// <summary>
    /// Publishes the dashboard contract (docs/architecture.md, "Property contract"). Delegates are evaluated lazily by
    /// SimHub whenever a dashboard reads them. Renaming a property is a breaking change: bump TelemetryEngine.Contract.
    /// </summary>
    internal static class PropertyPublisher
    {
        public const int MaxBands = 40;

        public static void Attach(TurnTelemetry plugin, TelemetryEngine e)
        {
            void P<T>(string name, Func<T> value) => plugin.AttachDelegate(name, value);

            // Meta
            P("Contract", () => TelemetryEngine.Contract);
            P("Version", () => Assembly.GetExecutingAssembly().GetName().Version.ToString(3));
            P("Adapter", () => e.Adapter.Id);
            P("Caps.Steering", () => e.Adapter.Capabilities.Steering);
            P("Caps.TC", () => e.Adapter.Capabilities.TcActive);
            P("Caps.ABS", () => e.Adapter.Capabilities.AbsActive);
            P("Caps.TyreWear", () => e.Adapter.Capabilities.TyreWear);
            P("Caps.LapPosition", () => e.HasLapPosition);

            // Live
            P("Live.Throttle", () => e.LastFrame.Throttle * 100);
            P("Live.Brake", () => e.LastFrame.Brake * 100);
            P<object>("Live.Steer", () => double.IsNaN(e.LastFrame.Steer) ? (object)null : e.LastFrame.Steer);
            P("Live.TC", () => e.LastFrame.Tc);
            P("Live.ABS", () => e.LastFrame.Abs);
            P("Live.LapPos", () => e.LastFrame.LapPos);
            P("Live.InTurn", () => e.Turn.Current != null);

            // Turn state
            P("Turn.Supported", () => e.Catalog.Supported);
            P("Turn.Source", () => CatalogResult.SourceId(e.Catalog.Source));
            P("Turn.Count", () => e.Catalog.Turns.Count);
            P("Turn.State", () => e.TurnStateId);
            P("Turn.Focal.Label", () => e.Focal?.Label ?? "");
            P("Turn.Focal.Name", () => e.Focal?.Name ?? "");
            P("Turn.Last.Label", () => e.Turn.Last?.Label ?? "");
            P("Turn.Upcoming.Label", () => e.Upcoming?.Label ?? "");
            P("Turn.Upcoming.Name", () => e.Upcoming?.Name ?? "");
            P("Turn.NoticeVisible", () => e.NoticeVisible);
            P("Turn.Detail", () => e.Catalog.Detail ?? "");

            for (var i = 0; i < MaxBands; i++)
            {
                var index = i;
                TurnDefinition Band() => index < e.Catalog.Turns.Count ? e.Catalog.Turns[index] : null;
                P($"Turn.Band.{index:00}.Start", () => Band()?.Start ?? -1);
                P($"Turn.Band.{index:00}.End", () => Band()?.End ?? -1);
                P($"Turn.Band.{index:00}.Label", () => Band()?.Label ?? "");
                P($"Turn.Band.{index:00}.IsCurrent", () => Band() != null && ReferenceEquals(Band(), e.Turn.Current));
                P($"Turn.Band.{index:00}.Sector", () => Band() == null ? 0 : e.Sectors.SectorAt(Band().Start));
                P($"Turn.Band.{index:00}.LimitsLap", () => e.Limits.ThisLap(Band()));
                P($"Turn.Band.{index:00}.LimitsSession", () => e.Limits.ThisSession(Band()));
            }

            // Sectors (learned boundaries; Count 0 until the car has crossed one) with this lap's pace rating
            P("Sector.Count", () => e.Sectors.Count);
            P("Sector.Current", () => e.CurrentSector);
            for (var i = 1; i <= SectorMap.MaxSectors; i++)
            {
                var n = i;
                P($"Sector.{n}.Start", () => e.Sectors.Start(n));
                P($"Sector.{n}.End", () => e.Sectors.End(n));
                P($"Sector.{n}.State", () => e.SectorTimes.State(n).ToString().ToUpperInvariant());
                P($"Sector.{n}.FromLastLap", () => e.SectorTimes.FromLastLap(n));
                P($"Sector.{n}.Time", () => double.IsNaN(e.SectorTimes.Time(n)) ? "" : StandingsBoard.FormatTime(e.SectorTimes.Time(n)));
            }

            // Track limits
            P("Limits.Available", () => e.LimitsAvailable);
            P("Limits.SessionTotal", () => e.Limits.SessionTotal);
            P("Limits.LastTurn", () => e.Limits.LastTurn?.Label ?? "");
            P("Limits.Source", () => e.Limits.Source);
            P<object>("Car.TyresOut", () => e.TyresOut);

            // Laps
            P("Ref.Kind", () => e.Laps.ReferenceKind == ReferenceKind.Best ? "BEST" : e.Laps.ReferenceKind == ReferenceKind.Last ? "LAST" : "NONE");
            P("Ref.LapTime", () => e.Laps.Reference?.LapTimeSeconds ?? 0);
            P("Lap.Coverage", () => e.Laps.Current?.Coverage ?? 0);
            P("Lap.Valid", () => e.Laps.Current?.Valid ?? false);
            P("Lap.InvalidReason", () => e.Laps.Current?.InvalidReason ?? "");

            // Tyres
            P("Tyre.Compound", () => e.Tyres.Compound ?? "");
            P("Tyre.Preset", () => e.Tyres.Preset.Id);
            P("Tyre.PressureUnit", () => e.Tyres.PressureUnit);
            P("Tyre.TempUnit", () => e.Tyres.TemperatureUnit);
            P("Tyre.ZonesDetected", () => e.Tyres.ZonesDetected);
            for (var i = 0; i < 4; i++)
            {
                var corner = e.Tyres.Corners[i];
                var n = "Tyre." + TyreCorner.Names[i];
                P(n + ".Temp.I", () => corner.TempInner);
                P(n + ".Temp.M", () => corner.TempMiddle);
                P(n + ".Temp.O", () => corner.TempOuter);
                P(n + ".Temp.Avg", () => corner.TempAverage);
                P(n + ".Color.I", () => corner.ColorInner);
                P(n + ".Color.M", () => corner.ColorMiddle);
                P(n + ".Color.O", () => corner.ColorOuter);
                P(n + ".Color.Avg", () => corner.ColorAverage);
                P<object>(n + ".Pressure", () => double.IsNaN(corner.Pressure) ? (object)null : corner.Pressure);
                P(n + ".PressureState", () => corner.PressureState.ToString().ToUpperInvariant());
                P(n + ".PressureColor", () => corner.PressureColor);
                P<object>(n + ".Wear", () => double.IsNaN(corner.WearRemaining) ? (object)null : corner.WearRemaining);
                P(n + ".WearColor", () => corner.WearColor);
                P(n + ".ColorNeon.I", () => corner.NeonInner);
                P(n + ".ColorNeon.M", () => corner.NeonMiddle);
                P(n + ".ColorNeon.O", () => corner.NeonOuter);
                P(n + ".ColorNeon.Avg", () => corner.NeonAverage);
                P(n + ".PressureColorNeon", () => corner.NeonPressure);
                P(n + ".WearColorNeon", () => corner.NeonWear);
            }

            // Car settings the sim adapter reads (null / "N/A" when the sim doesn't report them)
            P<object>("Car.TCCut", () => e.TcCut);
            P<object>("Car.Wipers", () => e.Wipers);
            P<object>("Car.Lights", () => e.Lights);
            P("Car.WipersText", () => TelemetryEngine.WipersText(e.Wipers));
            P("Car.LightsText", () => TelemetryEngine.LightsText(e.Lights));

            // Standings window (StandingsBoard.RowCount rows: top of the order, or leader + cars around the player)
            P("Standings.DriverCount", () => e.Standings.DriverCount);
            for (var i = 0; i < StandingsBoard.RowCount; i++)
            {
                var row = e.Standings.Rows[i];
                var n = $"Standings.Row{i + 1}.";
                P(n + "Visible", () => row.Visible);
                P(n + "Position", () => row.Visible ? row.Position.ToString() : "");
                P(n + "Name", () => row.Visible ? row.Name : "");
                P(n + "IsPlayer", () => row.Visible && row.IsPlayer);
                for (var s = 0; s < 3; s++)
                {
                    var sector = s;
                    P(n + "S" + (sector + 1), () => row.Visible ? row.Sectors[sector] : "");
                    P(n + "S" + (sector + 1) + "Fastest", () => row.Visible && row.SectorFastest[sector]);
                }
                P(n + "Best", () => row.Visible ? row.Best : "");
                P(n + "BestFastest", () => row.Visible && row.BestFastest);
                P(n + "BestDelta", () => row.Visible ? row.BestDelta : "");
                P(n + "Gap", () => row.Visible ? row.Gap : "");
            }

            // Recorder
            P("Recorder.Status", () => e.RecorderStatus);

            // SET LINE (dashboard)
            P("StartLine.UndoAvailable", () => e.StartLine.UndoAvailable(e.Now));
            P("StartLine.MessageVisible", () => e.StartLine.MessageVisible(e.Now));
            P("StartLine.Message", () => e.StartLine.Message);
            P("Turns.UserNumbering", () => e.Catalog.Source == TurnSource.User);
        }
    }
}
