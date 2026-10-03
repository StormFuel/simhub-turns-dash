using System;

namespace TurnTelemetry.Core.Input
{
    /// <summary>
    /// The subset of SimHub's normalised GameData the engine uses, copied once per frame by the host.
    /// Keeping this a plain DTO lets the whole pipeline be unit-tested without SimHub.
    /// </summary>
    public sealed class GameSnapshot
    {
        public string GameName;
        public string GamePath;
        public Guid SessionId;
        public string SessionTypeName;
        public string TrackId;
        public string TrackIdWithConfig;
        public string TrackName;
        public string CarId;
        public string CarModel;
        public string CarClass;

        public bool Paused;
        public bool Replay;
        public bool Spectating;

        /// <summary>Lap progress as reported by SimHub; 0..1 (or 0..100 in some readers, see FrameNormaliser).</summary>
        public double TrackPositionPercent;
        public double TrackLengthMeters;
        public double SpeedKmh;
        /// <summary>0..100</summary>
        public double Throttle;
        /// <summary>0..100</summary>
        public double Brake;
        public double BrakeBias;

        public int CompletedLaps;
        public int CurrentLap;
        public TimeSpan CurrentLapTime;
        public TimeSpan LastLapTime;
        public bool LapInvalidated;
        public bool InPitLane;
        /// <summary>The sim's sector index for the car (SimHub doesn't say where sectors start; see SectorMap).</summary>
        public int CurrentSectorIndex;
        /// <summary>Your best time per sector (S1-S3) as SimHub reports it, in seconds; 0 when unknown.</summary>
        public double[] BestSectors = new double[3];

        public bool TcActive;
        public bool AbsActive;
        public int TcLevel;
        public int AbsLevel;

        public string TyrePressureUnit;
        public string TemperatureUnit;

        /// <summary>FL, FR, RL, RR.</summary>
        public TyreReading[] Tyres = { new TyreReading(), new TyreReading(), new TyreReading(), new TyreReading() };
    }

    public sealed class TyreReading
    {
        public double TempInner;
        public double TempMiddle;
        public double TempOuter;
        public double TempAverage;
        public double Pressure;
        /// <summary>As reported by SimHub; meaning (used vs remaining) is per sim, see SimCapabilities.</summary>
        public double Wear;
    }

    /// <summary>Read access to sim-specific values under DataCorePlugin.GameRawData.</summary>
    public interface IRawData
    {
        /// <param name="path">Path relative to GameRawData, e.g. "Physics.SteerAngle".</param>
        object Get(string path);

        /// <summary>Raw property paths (relative to GameRawData) containing <paramref name="fragment"/>; for diagnostics.</summary>
        System.Collections.Generic.IReadOnlyList<string> FindPaths(string fragment);
    }
}
