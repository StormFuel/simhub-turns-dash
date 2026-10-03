using System;
using System.Collections.Generic;
using System.Globalization;
using TurnTelemetry.Core.Input;

namespace TurnTelemetry.Core.Sims
{
    public enum WearMeaning
    {
        Remaining,
        Used,
    }

    public sealed class SimCapabilities
    {
        public bool Steering;
        public bool TcActive;
        public bool AbsActive;
        /// <summary>False when the sim reports no tyre wear (SimHub then passes 0).</summary>
        public bool TyreWear = true;
        public WearMeaning Wear = WearMeaning.Remaining;
        /// <summary>Tyres outside the track limits that make an excursion (AC 3, ACC 4); 0 when the sim doesn't report it.</summary>
        public int TyresOutLimit;
        /// <summary>
        /// False when the sim doesn't invalidate laps for track limits in races (ACC gives warnings instead), so with
        /// no tyres-out signal there's nothing to detect excursions from in a race.
        /// </summary>
        public bool InvalidatesLapsInRace = true;
    }

    /// <summary>Location of an AC-style track folder: content/tracks/{Track}/{Layout}.</summary>
    public sealed class TrackFolder
    {
        public string Track;
        public string Layout;
    }

    /// <summary>
    /// Hides per-sim differences. Values SimHub already normalises come from <see cref="GameSnapshot"/>;
    /// adapters only fill the gaps (steering, compound, track folder).
    /// </summary>
    public interface ISimAdapter
    {
        string Id { get; }
        bool Handles(string gameName);
        SimCapabilities Capabilities { get; }

        /// <summary>Steering in -1..+1 of lock, positive = right; NaN when unavailable.</summary>
        double ReadSteer(IRawData raw);

        /// <summary>Player tyre compound label, or null.</summary>
        string ReadCompound(IRawData raw);

        /// <summary>AC-style track folder for sections.ini lookup, or null.</summary>
        TrackFolder ReadTrackFolder(IRawData raw);

        /// <summary>
        /// Raw tyre temperatures in °C, used when SimHub's normalised ones are empty (as for ACC).
        /// Fills <paramref name="celsius"/>[tyre FL/FR/RL/RR, zone inner/middle/outer/average]; false when unavailable.
        /// </summary>
        bool ReadTyreTemps(IRawData raw, double[,] celsius);

        /// <summary>TC cut setting, or null when the sim doesn't report it (SimHub doesn't normalise it).</summary>
        int? ReadTcCut(IRawData raw);

        /// <summary>Wiper stage (0 = off), or null when unknown.</summary>
        int? ReadWipers(IRawData raw);

        /// <summary>Headlight stage (0 = off, 1 = on, 2 = high), or null when unknown.</summary>
        int? ReadLights(IRawData raw);
    }

    /// <summary>Normalised data only; works for any sim SimHub supports.</summary>
    public class GenericAdapter : ISimAdapter
    {
        public virtual string Id => "generic";
        public virtual bool Handles(string gameName) => true;
        public virtual SimCapabilities Capabilities { get; } = new SimCapabilities { TcActive = true, AbsActive = true };
        public virtual double ReadSteer(IRawData raw) => double.NaN;
        public virtual string ReadCompound(IRawData raw) => null;
        public virtual TrackFolder ReadTrackFolder(IRawData raw) => null;
        public virtual bool ReadTyreTemps(IRawData raw, double[,] celsius) => false;
        public virtual int? ReadTcCut(IRawData raw) => null;
        public virtual int? ReadWipers(IRawData raw) => null;
        public virtual int? ReadLights(IRawData raw) => null;

        protected static int? ReadInt(IRawData raw, string path)
        {
            var v = ReadDouble(raw, path);
            return double.IsNaN(v) ? (int?)null : (int)Math.Round(v);
        }

        /// <summary>
        /// One element of a raw array. SimHub's naming for array elements is unconfirmed, so the whole-array form and
        /// the common flattened forms are all tried (diagnostics list the real names via IRawData.FindPaths).
        /// </summary>
        protected static double ReadElement(IRawData raw, string path, int index)
        {
            var value = raw?.Get(path);
            if (value is System.Collections.IList list)
                return index < list.Count ? ToDouble(list[index]) : double.NaN;
            foreach (var suffix in new[] { $"[{index}]", (index + 1).ToString("00"), (index + 1).ToString(), $"_{index}", index.ToString() })
            {
                var flat = ReadDouble(raw, path + suffix);
                if (!double.IsNaN(flat)) return flat;
            }
            return double.NaN;
        }

        private static double ToDouble(object value)
        {
            try { return value == null ? double.NaN : Convert.ToDouble(value, CultureInfo.InvariantCulture); }
            catch (Exception) { return double.NaN; }
        }

        protected static double ReadDouble(IRawData raw, string path)
        {
            var value = raw?.Get(path);
            if (value == null) return double.NaN;
            try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); }
            catch (Exception) { return double.NaN; }
        }

        protected static string ReadString(IRawData raw, string path)
        {
            var value = raw?.Get(path) as string;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    /// <summary>
    /// Assetto Corsa and ACC share the Kunos shared-memory layout (Physics / Graphics / StaticInfo).
    /// Physics.SteerAngle is -1..+1 (validated in the Phase 0 spike, docs/spikes.md).
    /// </summary>
    public class KunosAdapter : GenericAdapter
    {
        private readonly string _gameName;
        private readonly string _id;

        public KunosAdapter(string id, string gameName, SimCapabilities capabilities)
        {
            _id = id;
            _gameName = gameName;
            Capabilities = capabilities;
        }

        public override string Id => _id;
        public override bool Handles(string gameName) => string.Equals(gameName, _gameName, StringComparison.OrdinalIgnoreCase);
        public override SimCapabilities Capabilities { get; }

        public override double ReadSteer(IRawData raw)
        {
            var value = ReadDouble(raw, "Physics.SteerAngle");
            return double.IsNaN(value) ? value : Math.Max(-1, Math.Min(1, value));
        }

        public override string ReadCompound(IRawData raw) => ReadString(raw, "Graphics.TyreCompound");

        // ACC's Graphics page has these; AC's doesn't, so they read as null there.
        public override int? ReadTcCut(IRawData raw) => ReadInt(raw, "Graphics.TCCut");
        public override int? ReadWipers(IRawData raw) => ReadInt(raw, "Graphics.WiperLV");
        public override int? ReadLights(IRawData raw) => ReadInt(raw, "Graphics.LightsStage");

        /// <summary>
        /// Physics.TyreTempI/M/O when populated, otherwise one core temperature (TyreCoreTemperature, then tyreTemp)
        /// for every zone. Arrays are in FL, FR, RL, RR order.
        /// </summary>
        public override bool ReadTyreTemps(IRawData raw, double[,] celsius)
        {
            var any = false;
            for (var t = 0; t < 4; t++)
            {
                double inner = ReadElement(raw, "Physics.TyreTempI", t);
                double middle = ReadElement(raw, "Physics.TyreTempM", t);
                double outer = ReadElement(raw, "Physics.TyreTempO", t);
                var core = ReadElement(raw, "Physics.TyreCoreTemperature", t);
                if (!(core > 0)) core = ReadElement(raw, "Physics.tyreTemp", t);

                if (!(inner > 0 && middle > 0 && outer > 0))
                    inner = middle = outer = core;
                var avg = core > 0 ? core : (inner + middle + outer) / 3;

                celsius[t, 0] = OrZero(inner);
                celsius[t, 1] = OrZero(middle);
                celsius[t, 2] = OrZero(outer);
                celsius[t, 3] = avg = OrZero(avg);
                if (avg > 0) any = true;
            }
            return any;
        }

        private static double OrZero(double v) => double.IsNaN(v) ? 0 : v;

        public override TrackFolder ReadTrackFolder(IRawData raw)
        {
            var track = ReadString(raw, "StaticInfo.Track");
            if (track == null) return null;
            return new TrackFolder { Track = track, Layout = ReadString(raw, "StaticInfo.TrackConfiguration") };
        }
    }

    /// <summary>
    /// Forza Horizon (SimHub game names "FH4", "FH5", "FH6", ...). Data Out sends Steer as -127..127, positive = right.
    /// Horizon reports no lap position, TC/ABS intervention or tyre wear.
    /// </summary>
    public class ForzaHorizonAdapter : GenericAdapter
    {
        public override string Id => "forza-horizon";

        public override bool Handles(string gameName) =>
            gameName != null && gameName.StartsWith("FH", StringComparison.OrdinalIgnoreCase)
            && gameName.Length > 2 && char.IsDigit(gameName[2]);

        public override SimCapabilities Capabilities { get; } =
            new SimCapabilities { Steering = true, TcActive = false, AbsActive = false, TyreWear = false };

        public override double ReadSteer(IRawData raw)
        {
            var value = ReadDouble(raw, "Steer");
            return double.IsNaN(value) ? value : Math.Max(-1, Math.Min(1, value / 127.0));
        }
    }

    public static class SimAdapters
    {
        public const string AssettoCorsa = "AssettoCorsa";
        public const string AssettoCorsaCompetizione = "AssettoCorsaCompetizione";

        public static IReadOnlyList<ISimAdapter> Default { get; } = new ISimAdapter[]
        {
            new KunosAdapter("ac", AssettoCorsa,
                new SimCapabilities { Steering = true, TcActive = true, AbsActive = true, TyresOutLimit = 3 }),
            // ACC's shared memory carries no tyre wear; SimHub passes 0.
            new KunosAdapter("acc", AssettoCorsaCompetizione,
                // ACC's shared memory has NumberOfTyresOut but never fills it (always 0; checked live 2026-10-03).
                new SimCapabilities { Steering = true, TcActive = true, AbsActive = true, TyreWear = false,
                    InvalidatesLapsInRace = false }),
            new ForzaHorizonAdapter(),
            new GenericAdapter(), // must stay last: handles everything
        };

        public static ISimAdapter For(string gameName, IReadOnlyList<ISimAdapter> adapters = null)
        {
            foreach (var adapter in adapters ?? Default)
                if (adapter.Handles(gameName)) return adapter;
            return new GenericAdapter();
        }
    }
}
