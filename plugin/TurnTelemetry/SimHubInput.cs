using System;
using System.Collections.Generic;
using System.Linq;
using GameReaderCommon;
using SimHub.Plugins;
using TurnTelemetry.Core.Input;
using TurnTelemetry.Core.Standings;

namespace TurnTelemetryHost
{
    /// <summary>Copies SimHub's GameData into the Core engine's SimHub-free snapshot.</summary>
    internal static class SnapshotMapper
    {
        public static void Fill(GameSnapshot s, GameData data)
        {
            var d = data.NewData;
            s.GameName = data.GameName;
            s.GamePath = data.GamePath;
            s.SessionId = data.SessionId;
            s.Paused = data.GamePaused;
            s.Replay = data.GameReplay;
            s.Spectating = d.Spectating;

            s.SessionTypeName = d.SessionTypeName;
            s.TrackId = d.TrackId;
            s.TrackIdWithConfig = d.TrackIdWithConfig;
            s.TrackName = d.TrackName;
            s.CarId = d.CarId;
            s.CarModel = d.CarModel;
            s.CarClass = d.CarClass;

            s.TrackPositionPercent = d.TrackPositionPercent;
            s.TrackLengthMeters = d.TrackLength;
            s.SpeedKmh = d.SpeedKmh;
            s.Throttle = d.Throttle;
            s.Brake = d.Brake;
            s.BrakeBias = d.BrakeBias;

            s.CompletedLaps = d.CompletedLaps;
            s.CurrentLap = d.CurrentLap;
            s.CurrentLapTime = d.CurrentLapTime;
            s.LastLapTime = d.LastLapTime;
            s.LapInvalidated = d.LapInvalidated;
            s.CurrentSectorIndex = d.CurrentSectorIndex;
            s.BestSectors[0] = d.Sector1BestTime?.TotalSeconds ?? 0;
            s.BestSectors[1] = d.Sector2BestTime?.TotalSeconds ?? 0;
            s.BestSectors[2] = d.Sector3BestTime?.TotalSeconds ?? 0;
            s.InPitLane = d.IsInPitLane != 0 || d.IsInPit != 0;

            s.TcActive = d.TCActive != 0;
            s.AbsActive = d.ABSActive != 0;
            s.TcLevel = d.TCLevel;
            s.AbsLevel = d.ABSLevel;

            s.TyrePressureUnit = d.TyrePressureUnit;
            s.TemperatureUnit = d.TemperatureUnit;

            Tyre(s.Tyres[0], d.TyreTemperatureFrontLeftInner, d.TyreTemperatureFrontLeftMiddle, d.TyreTemperatureFrontLeftOuter,
                d.TyreTemperatureFrontLeft, d.TyrePressureFrontLeft, d.TyreWearFrontLeft);
            Tyre(s.Tyres[1], d.TyreTemperatureFrontRightInner, d.TyreTemperatureFrontRightMiddle, d.TyreTemperatureFrontRightOuter,
                d.TyreTemperatureFrontRight, d.TyrePressureFrontRight, d.TyreWearFrontRight);
            Tyre(s.Tyres[2], d.TyreTemperatureRearLeftInner, d.TyreTemperatureRearLeftMiddle, d.TyreTemperatureRearLeftOuter,
                d.TyreTemperatureRearLeft, d.TyrePressureRearLeft, d.TyreWearRearLeft);
            Tyre(s.Tyres[3], d.TyreTemperatureRearRightInner, d.TyreTemperatureRearRightMiddle, d.TyreTemperatureRearRightOuter,
                d.TyreTemperatureRearRight, d.TyrePressureRearRight, d.TyreWearRearRight);
        }

        private static void Tyre(TyreReading t, double inner, double middle, double outer, double avg, double pressure, double wear)
        {
            t.TempInner = inner;
            t.TempMiddle = middle;
            t.TempOuter = outer;
            t.TempAverage = avg;
            t.Pressure = pressure;
            t.Wear = wear;
        }
    }

    /// <summary>Copies SimHub's opponent list (which includes the player) into Core driver entries.</summary>
    internal static class StandingsMapper
    {
        public static List<DriverEntry> Drivers(StatusDataBase d)
        {
            var list = new List<DriverEntry>();
            foreach (var o in d.Opponents ?? new List<Opponent>())
            {
                if (o == null) continue;
                list.Add(new DriverEntry
                {
                    Position = o.Position,
                    Name = !string.IsNullOrWhiteSpace(o.Name) ? o.Name : o.ShortName,
                    IsPlayer = o.IsPlayer,
                    BestLap = o.BestLapTime.TotalSeconds,
                    BestS1 = o.BestSector1,
                    BestS2 = o.BestSector2,
                    BestS3 = o.BestSector3,
                    GapToLeader = o.GaptoLeader,
                    LapsToLeader = o.LapsToLeader,
                });
            }

            // Some sims leave the player out of the list; add them from the player's own data.
            if (!list.Any(x => x.IsPlayer) && d.Position > 0)
                list.Add(new DriverEntry
                {
                    Position = d.Position,
                    Name = d.PlayerName,
                    IsPlayer = true,
                    BestLap = d.BestLapTime.TotalSeconds,
                    BestS1 = d.Sector1BestTime?.TotalSeconds,
                    BestS2 = d.Sector2BestTime?.TotalSeconds,
                    BestS3 = d.Sector3BestTime?.TotalSeconds,
                });
            return list;
        }
    }

    /// <summary>Raw sim values via SimHub's property tree (DataCorePlugin.GameRawData.*).</summary>
    internal sealed class SimHubRawData : IRawData
    {
        private const string Prefix = "DataCorePlugin.GameRawData.";
        private readonly PluginManager _pm;

        public SimHubRawData(PluginManager pm)
        {
            _pm = pm;
        }

        public object Get(string path) => _pm.GetPropertyValue(Prefix + path);

        public IReadOnlyList<string> FindPaths(string fragment) => _pm.GetAllPropertiesNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
                        && n.IndexOf(fragment, Prefix.Length, StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(n => n.Substring(Prefix.Length))
            .ToList();
    }
}
