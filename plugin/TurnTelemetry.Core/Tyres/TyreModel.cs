using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using TurnTelemetry.Core.Input;
using TurnTelemetry.Core.Sims;

namespace TurnTelemetry.Core.Tyres
{
    public sealed class TyrePreset
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("note")] public string Note;
        [JsonProperty("match")] public MatchRule Match = new MatchRule();
        [JsonProperty("temp")] public TempWindow Temp = new TempWindow();
        [JsonProperty("pressure")] public PressureWindow Pressure;

        public sealed class MatchRule
        {
            /// <summary>SimHub game names; empty or "*" matches all.</summary>
            [JsonProperty("games")] public List<string> Games = new List<string>();
            [JsonProperty("compound")] public string Compound;
            [JsonProperty("carClass")] public string CarClass;
        }

        /// <summary>Degrees Celsius.</summary>
        public sealed class TempWindow
        {
            [JsonProperty("cold")] public double Cold = 60;
            [JsonProperty("optLow")] public double OptLow = 75;
            [JsonProperty("optHigh")] public double OptHigh = 100;
            [JsonProperty("hot")] public double Hot = 115;
        }

        public sealed class PressureWindow
        {
            [JsonProperty("optLow")] public double OptLow;
            [JsonProperty("optHigh")] public double OptHigh;
            [JsonProperty("unit")] public string Unit = "psi";
        }
    }

    public sealed class TyrePresetFile
    {
        [JsonProperty("presets")] public List<TyrePreset> Presets = new List<TyrePreset>();

        public static TyrePresetFile Parse(string json) => JsonConvert.DeserializeObject<TyrePresetFile>(json) ?? new TyrePresetFile();
    }

    public static class TyrePresetMatcher
    {
        public static readonly TyrePreset Fallback = new TyrePreset { Id = "fallback" };

        /// <summary>
        /// All rules must match; the most specific wins (game 4, compound 2, car class 1), ties go to the earlier preset.
        /// User presets replace bundled ones with the same id.
        /// </summary>
        public static TyrePreset Select(IEnumerable<TyrePreset> bundled, IEnumerable<TyrePreset> user,
            string game, string compound, string carClass)
        {
            var merged = new List<TyrePreset>();
            var userList = (user ?? Enumerable.Empty<TyrePreset>()).ToList();
            merged.AddRange(userList);
            merged.AddRange((bundled ?? Enumerable.Empty<TyrePreset>()).Where(b => userList.All(u => u.Id != b.Id)));

            TyrePreset best = null;
            var bestScore = -1;
            foreach (var preset in merged)
            {
                var score = Score(preset.Match ?? new TyrePreset.MatchRule(), game, compound, carClass);
                if (score > bestScore)
                {
                    best = preset;
                    bestScore = score;
                }
            }
            return best ?? Fallback;
        }

        private static int Score(TyrePreset.MatchRule rule, string game, string compound, string carClass)
        {
            var score = 0;
            var games = rule.Games ?? new List<string>();
            if (games.Count > 0 && !games.Contains("*"))
            {
                if (!games.Any(g => string.Equals(g, game, StringComparison.OrdinalIgnoreCase))) return -1;
                score += 4;
            }
            if (!string.IsNullOrEmpty(rule.Compound))
            {
                if (compound == null || !Regex.IsMatch(compound, rule.Compound)) return -1;
                score += 2;
            }
            if (!string.IsNullOrEmpty(rule.CarClass))
            {
                if (carClass == null || !Regex.IsMatch(carClass, rule.CarClass)) return -1;
                score += 1;
            }
            return score;
        }
    }

    public enum PressureState
    {
        Unknown,
        Low,
        Ok,
        High,
    }

    /// <summary>Colours for one dashboard theme. Classic follows docs/design.md; Neon is the high-saturation theme.</summary>
    public sealed class TyrePalette
    {
        public string NoData, Cold, Optimal, Warm, Hot, Text;

        public static readonly TyrePalette Classic = new TyrePalette
        {
            NoData = TyreColors.NoData, Cold = TyreColors.Cold, Optimal = TyreColors.Optimal,
            Warm = TyreColors.Warm, Hot = TyreColors.Hot, Text = TyreColors.Text,
        };

        public static readonly TyrePalette Neon = new TyrePalette
        {
            NoData = "#FF262B26", Cold = "#FF00B3FF", Optimal = "#FF39FF14",
            Warm = "#FFFFEA00", Hot = "#FFFF1744", Text = "#FFFFFFFF",
        };
    }

    public static class TyreColors
    {
        public const string NoData = "#FF3A414C";
        public const string Cold = "#FF3B82F6";
        public const string Optimal = "#FF2EE66B";
        public const string Warm = "#FFFFD60A";
        public const string Hot = "#FFFF3B30";
        public const string Text = "#FFF7F9FC";

        /// <summary>
        /// Four unambiguous states (docs/design.md): blue below the ideal window, green inside it, yellow above it,
        /// red at or beyond "hot". No blending: in-between hues read as neither state (user feedback 2026-10-03).
        /// </summary>
        public static string ForTemperature(double celsius, TyrePreset.TempWindow w, TyrePalette p = null)
        {
            p = p ?? TyrePalette.Classic;
            if (double.IsNaN(celsius) || celsius <= 0) return p.NoData;
            if (celsius < w.OptLow) return p.Cold;
            if (celsius <= w.OptHigh) return p.Optimal;
            if (celsius < w.Hot) return p.Warm;
            return p.Hot;
        }

        public static string ForPressure(PressureState state, TyrePalette p = null)
        {
            p = p ?? TyrePalette.Classic;
            switch (state)
            {
                case PressureState.Low: return p.Cold;
                case PressureState.High: return p.Hot;
                default: return p.Text;
            }
        }

        public static string ForWear(double remainingPercent, TyrePalette p = null)
        {
            p = p ?? TyrePalette.Classic;
            if (double.IsNaN(remainingPercent)) return p.NoData;
            if (remainingPercent < 25) return p.Hot;
            if (remainingPercent < 50) return p.Warm;
            return p.Text;
        }

        public static string Blend(string a, string b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            var ca = Convert.ToUInt32(a.Substring(1), 16);
            var cb = Convert.ToUInt32(b.Substring(1), 16);
            uint result = 0;
            for (var shift = 0; shift <= 24; shift += 8)
            {
                var x = (ca >> shift) & 0xFF;
                var y = (cb >> shift) & 0xFF;
                result |= (uint)Math.Round(x + (y - x) * t) << shift;
            }
            return "#" + result.ToString("X8");
        }
    }

    /// <summary>
    /// SimHub reports temperatures in the user's display unit. Its names (GameReaderCommon.LocalTemperatureUnit) are
    /// "Celcius" (sic), "Fahrenheit" and "Kelvin"; colours are always decided in Celsius.
    /// </summary>
    public static class TemperatureUnits
    {
        /// <summary>"C", "F" or "K" for a SimHub unit name (or a short code / "°F" style label); unknown means Celsius.</summary>
        public static string Code(string unit)
        {
            var u = (unit ?? string.Empty).Trim().TrimStart('\u00b0').ToUpperInvariant();
            if (u.StartsWith("F")) return "F";
            if (u.StartsWith("K")) return "K";
            return "C";
        }

        public static double ToCelsius(double value, string code) =>
            code == "F" ? (value - 32) * 5 / 9 : code == "K" ? value - 273.15 : value;

        public static double FromCelsius(double celsius, string code) =>
            code == "F" ? celsius * 9 / 5 + 32 : code == "K" ? celsius + 273.15 : celsius;
    }

    public static class PressureUnits
    {
        private const double PsiPerBar = 14.5037738;

        /// <summary>Normalises SimHub/preset unit labels to psi, bar or kpa; null when unknown.</summary>
        public static string Normalise(string unit)
        {
            var u = (unit ?? string.Empty).Trim().ToLowerInvariant();
            if (u.Contains("psi")) return "psi";
            if (u.Contains("kpa")) return "kpa";
            if (u.Contains("bar")) return "bar";
            return null;
        }

        public static double Convert(double value, string from, string to)
        {
            from = Normalise(from) ?? "psi";
            to = Normalise(to) ?? "psi";
            if (from == to || double.IsNaN(value)) return value;
            var bar = from == "psi" ? value / PsiPerBar : from == "kpa" ? value / 100.0 : value;
            return to == "psi" ? bar * PsiPerBar : to == "kpa" ? bar * 100.0 : bar;
        }
    }

    public sealed class TyreCorner
    {
        public static readonly string[] Names = { "FL", "FR", "RL", "RR" };

        public double TempInner, TempMiddle, TempOuter, TempAverage;
        public string ColorInner = TyreColors.NoData, ColorMiddle = TyreColors.NoData,
            ColorOuter = TyreColors.NoData, ColorAverage = TyreColors.NoData;
        public double Pressure = double.NaN;
        public PressureState PressureState;
        public string PressureColor = TyreColors.Text;
        public double WearRemaining = double.NaN;
        public string WearColor = TyreColors.NoData;

        /// <summary>The same colours in the Neon palette (published as *.ColorNeon.*, *.PressureColorNeon, *.WearColorNeon).</summary>
        public string NeonInner = TyrePalette.Neon.NoData, NeonMiddle = TyrePalette.Neon.NoData,
            NeonOuter = TyrePalette.Neon.NoData, NeonAverage = TyrePalette.Neon.NoData,
            NeonPressure = TyrePalette.Neon.Text, NeonWear = TyrePalette.Neon.NoData;
    }

    /// <summary>Per-corner temperatures, pressure and wear with colours from the matched preset.</summary>
    /// <remarks>
    /// SimHub reports temperatures in the user's display unit (TemperatureUnit), so a sim that sends nothing shows up as
    /// 0 °C or 32 °F. Presets and the "no data" test therefore work in Celsius; published temperatures stay in SimHub's unit.
    /// </remarks>
    public sealed class TyreModel
    {
        /// <summary>Inner/middle/outer must differ by more than this before the sim counts as reporting three zones.</summary>
        public const double ZoneSpreadCelsius = 0.5;

        public TyreCorner[] Corners { get; } = { new TyreCorner(), new TyreCorner(), new TyreCorner(), new TyreCorner() };
        public TyrePreset Preset { get; private set; } = TyrePresetMatcher.Fallback;
        public string Compound { get; private set; }
        public string PressureUnit { get; set; } = "psi";
        /// <summary>"C", "F" or "K": the unit of the published temperatures (SimHub's display unit).</summary>
        public string TemperatureUnit { get; private set; } = "C";
        /// <summary>Latched once inner/middle/outer differ on any tyre this session; until then draw one colour per tyre.</summary>
        public bool ZonesDetected { get; private set; }

        public void ResetSession() => ZonesDetected = false;

        public void SetPreset(TyrePreset preset, string compound)
        {
            Preset = preset ?? TyrePresetMatcher.Fallback;
            Compound = compound;
        }

        public void Update(GameSnapshot s, WearMeaning wear, bool wearAvailable = true)
        {
            var window = Preset.Temp ?? new TyrePreset.TempWindow();
            var unit = TemperatureUnits.Code(s.TemperatureUnit);
            TemperatureUnit = unit;
            double C(double v) => TemperatureUnits.ToCelsius(v, unit);

            for (var i = 0; i < 4; i++)
            {
                var r = s.Tyres[i];
                var c = Corners[i];
                double inner = C(r.TempInner), middle = C(r.TempMiddle), outer = C(r.TempOuter), avg = C(r.TempAverage);
                if (avg <= 0) avg = Average(inner, middle, outer);

                c.TempInner = r.TempInner;
                c.TempMiddle = r.TempMiddle;
                c.TempOuter = r.TempOuter;
                c.TempAverage = avg <= 0 ? 0 : TemperatureUnits.FromCelsius(avg, unit);
                c.ColorInner = TyreColors.ForTemperature(inner, window);
                c.ColorMiddle = TyreColors.ForTemperature(middle, window);
                c.ColorOuter = TyreColors.ForTemperature(outer, window);
                c.ColorAverage = TyreColors.ForTemperature(avg, window);
                var neon = TyrePalette.Neon;
                c.NeonInner = TyreColors.ForTemperature(inner, window, neon);
                c.NeonMiddle = TyreColors.ForTemperature(middle, window, neon);
                c.NeonOuter = TyreColors.ForTemperature(outer, window, neon);
                c.NeonAverage = TyreColors.ForTemperature(avg, window, neon);

                if (inner > 0 && middle > 0 && outer > 0
                    && Math.Max(inner, Math.Max(middle, outer)) - Math.Min(inner, Math.Min(middle, outer)) > ZoneSpreadCelsius)
                    ZonesDetected = true;

                c.Pressure = r.Pressure > 0 ? PressureUnits.Convert(r.Pressure, s.TyrePressureUnit, PressureUnit) : double.NaN;
                c.PressureState = Classify(c.Pressure);
                c.PressureColor = TyreColors.ForPressure(c.PressureState);
                c.NeonPressure = TyreColors.ForPressure(c.PressureState, TyrePalette.Neon);

                c.WearRemaining = !wearAvailable || double.IsNaN(r.Wear) ? double.NaN
                    : Math.Max(0, Math.Min(100, wear == WearMeaning.Used ? 100 - r.Wear : r.Wear));
                c.WearColor = TyreColors.ForWear(c.WearRemaining);
                c.NeonWear = TyreColors.ForWear(c.WearRemaining, TyrePalette.Neon);
            }
        }

        private PressureState Classify(double pressure)
        {
            var window = Preset.Pressure;
            if (window == null || double.IsNaN(pressure)) return PressureState.Unknown;
            var low = PressureUnits.Convert(window.OptLow, window.Unit, PressureUnit);
            var high = PressureUnits.Convert(window.OptHigh, window.Unit, PressureUnit);
            if (pressure < low) return PressureState.Low;
            if (pressure > high) return PressureState.High;
            return PressureState.Ok;
        }

        private static double Average(params double[] values)
        {
            var present = values.Where(v => v > 0).ToArray();
            return present.Length == 0 ? 0 : present.Average();
        }
    }
}
