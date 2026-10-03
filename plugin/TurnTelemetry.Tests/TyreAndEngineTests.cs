using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TurnTelemetry.Core.Engine;
using TurnTelemetry.Core.Input;
using TurnTelemetry.Core.Sims;
using TurnTelemetry.Core.Turns;
using TurnTelemetry.Core.Tyres;
using Xunit;

namespace TurnTelemetry.Tests
{
    public class TyreTests
    {
        private static readonly TyrePreset.TempWindow Window = new TyrePreset.TempWindow { Cold = 60, OptLow = 80, OptHigh = 95, Hot = 105 };

        [Theory]
        [InlineData(0, TyreColors.NoData)]
        [InlineData(40, TyreColors.Cold)]
        [InlineData(79, TyreColors.Cold)]
        [InlineData(80, TyreColors.Optimal)]
        [InlineData(95, TyreColors.Optimal)]
        [InlineData(96, TyreColors.Warm)]
        [InlineData(104, TyreColors.Warm)]
        [InlineData(105, TyreColors.Hot)]
        [InlineData(130, TyreColors.Hot)]
        public void Temperature_bands(double celsius, string expected)
        {
            Assert.Equal(expected, TyreColors.ForTemperature(celsius, Window));
        }

        [Fact]
        public void Neon_palette_is_computed_alongside_classic()
        {
            var model = new TyreModel();
            model.SetPreset(new TyrePreset { Temp = Window }, null);
            var s = new GameSnapshot();
            s.Tyres[0].TempInner = s.Tyres[0].TempMiddle = s.Tyres[0].TempOuter = 85;
            s.Tyres[1].TempInner = s.Tyres[1].TempMiddle = s.Tyres[1].TempOuter = 120;
            model.Update(s, WearMeaning.Remaining);

            Assert.Equal(TyreColors.Optimal, model.Corners[0].ColorMiddle);
            Assert.Equal(TyrePalette.Neon.Optimal, model.Corners[0].NeonMiddle);
            Assert.Equal(TyrePalette.Neon.Hot, model.Corners[1].NeonAverage);
            Assert.Equal(TyrePalette.Neon.NoData, model.Corners[2].NeonMiddle);
        }

        [Fact]
        public void Only_four_distinct_temperature_colours_are_used()
        {
            var seen = Enumerable.Range(1, 160).Select(c => TyreColors.ForTemperature(c, Window)).Distinct().ToList();
            Assert.Equal(new[] { TyreColors.Cold, TyreColors.Optimal, TyreColors.Warm, TyreColors.Hot }, seen);
        }

        [Fact]
        public void Pressure_units_round_trip()
        {
            Assert.Equal(27.5, PressureUnits.Convert(PressureUnits.Convert(27.5, "psi", "bar"), "bar", "psi"), 6);
            Assert.Equal(100, PressureUnits.Convert(1, "Bar", "kPa"), 6);
            Assert.Equal("kpa", PressureUnits.Normalise("KPA"));
        }

        [Fact]
        public void Most_specific_preset_wins_and_user_overrides_by_id()
        {
            var bundled = new List<TyrePreset>
            {
                new TyrePreset { Id = "generic", Match = { Games = { "*" } } },
                new TyrePreset { Id = "acc-dry", Match = { Games = { "AssettoCorsaCompetizione" }, Compound = "(?i)dry" } },
                new TyrePreset { Id = "acc-any", Match = { Games = { "AssettoCorsaCompetizione" } } },
            };
            Assert.Equal("acc-dry", TyrePresetMatcher.Select(bundled, null, "AssettoCorsaCompetizione", "dry_compound", null).Id);
            Assert.Equal("acc-any", TyrePresetMatcher.Select(bundled, null, "AssettoCorsaCompetizione", "wet_compound", null).Id);
            Assert.Equal("generic", TyrePresetMatcher.Select(bundled, null, "IRacing", null, null).Id);

            var user = new List<TyrePreset>
            {
                new TyrePreset { Id = "acc-dry", Note = "mine", Match = { Games = { "AssettoCorsaCompetizione" }, Compound = "(?i)dry" } },
            };
            Assert.Equal("mine", TyrePresetMatcher.Select(bundled, user, "AssettoCorsaCompetizione", "dry_compound", null).Note);
        }

        [Fact]
        public void Model_converts_pressure_classifies_and_normalises_wear()
        {
            var model = new TyreModel { PressureUnit = "psi" };
            model.SetPreset(new TyrePreset { Pressure = new TyrePreset.PressureWindow { OptLow = 26, OptHigh = 27, Unit = "psi" } }, "dry");
            var s = new GameSnapshot { TyrePressureUnit = "Bar" };
            s.Tyres[0].Pressure = 1.7;  // 24.7 psi → low
            s.Tyres[1].Pressure = 1.83; // 26.5 psi → ok
            s.Tyres[0].Wear = 10;

            model.Update(s, WearMeaning.Used);

            Assert.Equal(PressureState.Low, model.Corners[0].PressureState);
            Assert.Equal(PressureState.Ok, model.Corners[1].PressureState);
            Assert.Equal(90, model.Corners[0].WearRemaining, 6);
            Assert.True(double.IsNaN(model.Corners[2].Pressure));
        }

        [Fact]
        public void Fahrenheit_readings_are_coloured_in_celsius_and_32F_is_no_data()
        {
            var model = new TyreModel();
            model.SetPreset(new TyrePreset { Temp = Window }, null);
            var s = new GameSnapshot { TemperatureUnit = "Fahrenheit" };
            s.Tyres[0].TempInner = s.Tyres[0].TempMiddle = s.Tyres[0].TempOuter = s.Tyres[0].TempAverage = 32; // sim sent 0
            s.Tyres[1].TempInner = s.Tyres[1].TempMiddle = s.Tyres[1].TempOuter = 185;                          // 85 C

            model.Update(s, WearMeaning.Remaining);

            Assert.Equal("F", model.TemperatureUnit);
            Assert.Equal(TyreColors.NoData, model.Corners[0].ColorMiddle);
            Assert.Equal(TyreColors.Optimal, model.Corners[1].ColorMiddle);
            Assert.Equal(185, model.Corners[1].TempAverage, 6); // published in SimHub's unit
            Assert.False(model.ZonesDetected);
        }

        [Fact]
        public void Zones_are_detected_only_when_inner_middle_outer_differ()
        {
            var model = new TyreModel();
            var s = new GameSnapshot();
            s.Tyres[2].TempInner = 80; s.Tyres[2].TempMiddle = 80; s.Tyres[2].TempOuter = 80;
            model.Update(s, WearMeaning.Remaining);
            Assert.False(model.ZonesDetected);

            s.Tyres[2].TempInner = 84;
            model.Update(s, WearMeaning.Remaining);
            Assert.True(model.ZonesDetected);

            model.ResetSession();
            Assert.False(model.ZonesDetected);
        }

        [Fact]
        public void Bundled_presets_are_embedded_and_parse()
        {
            var json = new EmbeddedBundledData(typeof(TyrePreset).Assembly).Read("data/tyres/presets.json");
            Assert.NotNull(json);
            var presets = TyrePresetFile.Parse(json).Presets;
            Assert.Contains(presets, p => p.Id == "generic");
            Assert.Equal("ac-street", TyrePresetMatcher.Select(presets, null, "AssettoCorsa", "Semislicks (SM)", null).Id);
            Assert.Equal("ac-slick", TyrePresetMatcher.Select(presets, null, "AssettoCorsa", "Slick Medium (SM)", null).Id);
        }
    }

    internal sealed class FakeRaw : IRawData
    {
        public readonly Dictionary<string, object> Values = new Dictionary<string, object>();
        /// <summary>False mimics SimHub with its raw-data listing off: values readable by name but not listed.</summary>
        public bool ListsPaths = true;
        public object Get(string path) => Values.TryGetValue(path, out var v) ? v : null;
        public IReadOnlyList<string> FindPaths(string fragment) => !ListsPaths ? new List<string>() :
            Values.Keys.Where(k => k.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
    }

    public class EngineTests
    {
        private static GameSnapshot AccSnapshot(double pos) => new GameSnapshot
        {
            GameName = SimAdapters.AssettoCorsaCompetizione,
            TrackIdWithConfig = "monza",
            CarId = "car",
            SessionTypeName = "PRACTICE",
            TrackPositionPercent = pos,
            Throttle = 100,
        };

        [Fact]
        public void Curated_turns_drive_next_and_current_and_steering_comes_from_raw()
        {
            using (var dir = new TempDir())
            {
                var bundle = new FakeBundle();
                bundle.Files["data/turns/AssettoCorsaCompetizione/monza.json"] = new TurnFile
                {
                    Turns = { new TurnFile.TurnEntry { Label = "1", Start = 0.10, End = 0.15 } },
                }.ToJson();
                var raw = new FakeRaw();
                raw.Values["Physics.SteerAngle"] = 0.25f;
                raw.Values["Graphics.TyreCompound"] = "dry_compound";

                var engine = new TelemetryEngine(dir.Path, bundle);
                engine.Update(AccSnapshot(0.05), raw, 0);
                Assert.Equal("acc", engine.Adapter.Id);
                Assert.Equal("NEXT", engine.TurnStateId);
                Assert.Equal("1", engine.Focal.Label);
                Assert.Equal(0.25, engine.LastFrame.Steer, 6);
                Assert.Equal("dry_compound", engine.Tyres.Compound);

                engine.Update(AccSnapshot(0.12), raw, 0.1);
                Assert.Equal("CURRENT", engine.TurnStateId);

                // ACC reports no tyre wear: SimHub's 0 must not read as "0% remaining".
                Assert.False(engine.Adapter.Capabilities.TyreWear);
                Assert.True(double.IsNaN(engine.Tyres.Corners[0].WearRemaining));
            }
        }

        [Fact]
        public void Unsupported_track_shows_notice_briefly()
        {
            using (var dir = new TempDir())
            {
                var engine = new TelemetryEngine(dir.Path, new FakeBundle(), new EngineOptions { UnsupportedNoticeSeconds = 4 });
                engine.Update(AccSnapshot(0.05), new FakeRaw(), 0);
                Assert.True(engine.NoticeVisible);
                Assert.Equal("NONE", engine.TurnStateId);
                engine.Update(AccSnapshot(0.06), new FakeRaw(), 5);
                Assert.False(engine.NoticeVisible);
            }
        }

        [Fact]
        public void Recorder_writes_a_user_file_that_is_used_immediately()
        {
            using (var dir = new TempDir())
            {
                var engine = new TelemetryEngine(dir.Path, new FakeBundle());
                engine.Update(AccSnapshot(0.20), new FakeRaw(), 0);
                engine.MarkTurnStart();
                engine.Update(AccSnapshot(0.22), new FakeRaw(), 0.1);
                var status = engine.MarkTurnEnd();

                Assert.StartsWith("saved turn 1", status);
                Assert.True(File.Exists(engine.UserTurnFilePath));
                Assert.Equal(TurnSource.User, engine.Catalog.Source);
                engine.Update(AccSnapshot(0.21), new FakeRaw(), 0.2);
                Assert.Equal("CURRENT", engine.TurnStateId);
            }
        }

        [Fact]
        public void Recorder_refuses_when_sim_reports_no_lap_position()
        {
            using (var dir = new TempDir())
            {
                var engine = new TelemetryEngine(dir.Path, new FakeBundle());
                var openWorld = AccSnapshot(0);
                openWorld.GameName = "FH6";
                engine.Update(openWorld, new FakeRaw(), 0);

                Assert.False(engine.HasLapPosition);
                Assert.Contains("no lap position", engine.MarkTurnStart());
                Assert.False(File.Exists(engine.UserTurnFilePath));
            }
        }

        [Theory]
        [InlineData("FH6", "forza-horizon")]
        [InlineData("FH5", "forza-horizon")]
        [InlineData("FHelper", "generic")]
        [InlineData("AssettoCorsa", "ac")]
        public void Adapters_are_selected_by_game_name(string game, string adapter)
        {
            Assert.Equal(adapter, SimAdapters.For(game).Id);
        }

        [Fact]
        public void Forza_horizon_steering_is_scaled_from_127()
        {
            var raw = new FakeRaw();
            raw.Values["Steer"] = (sbyte)-127;
            Assert.Equal(-1.0, new ForzaHorizonAdapter().ReadSteer(raw), 6);
            raw.Values["Steer"] = 64;
            Assert.Equal(64 / 127.0, new ForzaHorizonAdapter().ReadSteer(raw), 6);
        }

        [Fact]
        public void Acc_tyre_temps_fall_back_to_raw_core_temperature_in_display_unit()
        {
            using (var dir = new TempDir())
            {
                var raw = new FakeRaw();
                raw.Values["Physics.TyreCoreTemperature"] = new float[] { 85, 86, 80, 81 }; // whole-array form
                var s = AccSnapshot(0.3);
                s.TemperatureUnit = "Fahrenheit";
                foreach (var t in s.Tyres) t.TempInner = t.TempMiddle = t.TempOuter = t.TempAverage = 32; // SimHub: empty

                var engine = new TelemetryEngine(dir.Path, new FakeBundle());
                engine.Update(s, raw, 0);

                Assert.True(engine.TyreTempsFromRaw);
                Assert.Equal(185, engine.Tyres.Corners[0].TempAverage, 3);   // 85 C shown in F
                Assert.Equal(TyreColors.Optimal, engine.Tyres.Corners[0].ColorMiddle); // acc-dry window is 75..95 C
                Assert.False(engine.Tyres.ZonesDetected);                     // one core value per tyre
            }
        }

        [Fact]
        public void Raw_tyre_arrays_may_be_flattened_into_numbered_properties()
        {
            var raw = new FakeRaw();
            raw.Values["Physics.TyreTempI02"] = 90f;
            raw.Values["Physics.TyreTempM02"] = 88f;
            raw.Values["Physics.TyreTempO02"] = 84f;
            var temps = new double[4, 4];
            Assert.True(SimAdapters.For(SimAdapters.AssettoCorsa).ReadTyreTemps(raw, temps));
            Assert.Equal(90, temps[1, 0], 3);
            Assert.Equal(84, temps[1, 2], 3);
            Assert.Equal(0, temps[0, 3], 3);
        }

        [Fact]
        public void Real_acc_names_from_simhub_property_browser_are_used()
        {
            using (var dir = new TempDir())
            {
                // Exactly as listed by SimHub 9.11 for ACC (2026-10-03): flattened 01..04, FL FR RL RR, Celsius.
                var raw = new FakeRaw();
                raw.Values["Physics.TyreCoreTemperature01"] = 85.77339f;
                raw.Values["Physics.TyreCoreTemperature02"] = 78.9035f;
                raw.Values["Physics.TyreCoreTemperature03"] = 85.12447f;
                raw.Values["Physics.TyreCoreTemperature04"] = 78.82745f;
                var s = AccSnapshot(0.3);
                s.TemperatureUnit = "Fahrenheit";
                foreach (var t in s.Tyres) t.TempInner = t.TempMiddle = t.TempOuter = t.TempAverage = 32;

                var engine = new TelemetryEngine(dir.Path, new FakeBundle());
                engine.Update(s, raw, 0);

                Assert.True(engine.TyreTempsFromRaw, engine.RawTyreProbe);
                Assert.Equal(85.77339 * 9 / 5 + 32, engine.Tyres.Corners[0].TempMiddle, 2);
                Assert.Equal(TyreColors.Optimal, engine.Tyres.Corners[0].ColorMiddle);
            }
        }

        [Fact]
        public void Average_only_temps_are_copied_into_zones_as_acc_reports_them()
        {
            using (var dir = new TempDir())
            {
                // As seen in ACC 2026-10-03: I/M/O empty (32 F), average populated (85.8 C = 186.4 F).
                var s = AccSnapshot(0.3);
                s.TemperatureUnit = "Fahrenheit";
                foreach (var t in s.Tyres) { t.TempInner = t.TempMiddle = t.TempOuter = 32; t.TempAverage = 186.44; }

                var engine = new TelemetryEngine(dir.Path, new FakeBundle());
                engine.Update(s, new FakeRaw(), 0);

                Assert.False(engine.TyreTempsFromRaw);
                Assert.Equal(186.44, engine.Tyres.Corners[0].TempMiddle, 2);
                Assert.Equal(TyreColors.Optimal, engine.Tyres.Corners[0].ColorInner);
                Assert.False(engine.Tyres.ZonesDetected);
                Assert.Contains("only the tyre average", engine.RawTyreProbe);
            }
        }

        [Fact]
        public void Bracket_indexed_raw_names_are_read()
        {
            var raw = new FakeRaw();
            raw.Values["Physics.TyreCoreTemperature[3]"] = 77f;
            var temps = new double[4, 4];
            Assert.True(SimAdapters.For(SimAdapters.AssettoCorsaCompetizione).ReadTyreTemps(raw, temps));
            Assert.Equal(77, temps[3, 3], 3);
        }

        [Fact]
        public void Unmatched_raw_tyre_properties_are_listed_for_diagnostics()
        {
            using (var dir = new TempDir())
            {
                var raw = new FakeRaw();
                raw.Values["Physics.TyreCoreTemp_FL"] = 80f; // a naming the adapter does not know
                var engine = new TelemetryEngine(dir.Path, new FakeBundle());
                engine.Update(AccSnapshot(0.3), raw, 0);

                Assert.False(engine.TyreTempsFromRaw);
                Assert.Contains("Physics.TyreCoreTemp_FL = 80", engine.RawTyreCandidates);
            }
        }

        [Fact]
        public void Normalised_temps_win_when_simhub_provides_them()
        {
            using (var dir = new TempDir())
            {
                var raw = new FakeRaw();
                raw.Values["Physics.TyreCoreTemperature"] = new float[] { 85, 86, 80, 81 };
                var s = AccSnapshot(0.3);
                s.Tyres[0].TempMiddle = 70;
                var engine = new TelemetryEngine(dir.Path, new FakeBundle());
                engine.Update(s, raw, 0);
                Assert.False(engine.TyreTempsFromRaw);
            }
        }

        [Fact]
        public void Saving_editor_turns_writes_a_user_file_and_loads_it()
        {
            using (var dir = new TempDir())
            {
                var engine = new TelemetryEngine(dir.Path, new FakeBundle());
                engine.Update(AccSnapshot(0.20), new FakeRaw(), 0);
                var status = engine.SaveTurns(new[]
                {
                    new TurnCandidate { Label = "1", Name = "Copse", Start = 0.06, End = 0.12 },
                    new TurnCandidate { Label = "", Start = 0.3, End = 0.4 },               // unnumbered: skipped
                    new TurnCandidate { Label = "2", Include = false, Start = 0.5, End = 0.6 }, // unticked: skipped
                });

                Assert.Equal("saved 1 turns", status);
                Assert.Equal(TurnSource.User, engine.Catalog.Source);
                Assert.Equal("Copse", engine.CurrentTurnCandidates().Single().Name);
            }
        }

        [Fact]
        public void Detection_requires_a_complete_lap()
        {
            using (var dir = new TempDir())
            {
                var engine = new TelemetryEngine(dir.Path, new FakeBundle());
                engine.Update(AccSnapshot(0.20), new FakeRaw(), 0);
                Assert.Empty(engine.DetectCorners(out var message));
                Assert.Contains("drive more of the lap", message);
            }
        }

        [Fact]
        public void Acc_tc_cut_wipers_and_lights_come_from_raw_graphics_and_ac_reports_none()
        {
            using (var dir = new TempDir())
            {
                var raw = new FakeRaw();
                raw.Values["Graphics.TCCut"] = 3;
                raw.Values["Graphics.WiperLV"] = 2;
                raw.Values["Graphics.LightsStage"] = 1;
                var engine = new TelemetryEngine(dir.Path, new FakeBundle());
                engine.Update(AccSnapshot(0.3), raw, 0);
                Assert.Equal(3, engine.TcCut);
                Assert.Equal("ON 2", TelemetryEngine.WipersText(engine.Wipers));
                Assert.Equal("ON", TelemetryEngine.LightsText(engine.Lights));

                var ac = AccSnapshot(0.3);
                ac.GameName = SimAdapters.AssettoCorsa;
                engine.Update(ac, new FakeRaw(), 1);
                Assert.Null(engine.TcCut);
                Assert.Equal("N/A", TelemetryEngine.WipersText(engine.Wipers));
            }
        }

        [Fact]
        public void Tyres_out_is_read_by_name_even_when_simhub_does_not_list_raw_properties()
        {
            using (var dir = new TempDir())
            {
                var raw = new FakeRaw { ListsPaths = false };
                raw.Values["Physics.NumberOfTyresOut"] = 4;
                var engine = new TelemetryEngine(dir.Path, new FakeBundle());
                var ac = AccSnapshot(0.3);
                ac.GameName = SimAdapters.AssettoCorsa;
                engine.Update(ac, raw, 0);
                Assert.Equal("Physics.NumberOfTyresOut", engine.TyresOutPath);
                Assert.Equal(4, engine.TyresOut);
            }
        }

        [Fact]
        public void Acc_races_report_track_limits_as_unavailable_but_practice_and_ac_races_do_not()
        {
            using (var dir = new TempDir())
            {
                var engine = new TelemetryEngine(dir.Path, new FakeBundle());
                var s = AccSnapshot(0.3);
                engine.Update(s, new FakeRaw(), 0);
                Assert.True(engine.LimitsAvailable);       // ACC practice: lap invalidation works

                s = AccSnapshot(0.3);
                s.SessionTypeName = "RACE";
                engine.Update(s, new FakeRaw(), 1);
                Assert.False(engine.LimitsAvailable);
                Assert.Null(engine.TyresOut);               // not read in ACC: always 0 there

                s = AccSnapshot(0.3);
                s.SessionTypeName = "RACE";
                s.GameName = SimAdapters.AssettoCorsa;
                engine.Update(s, new FakeRaw(), 2);
                Assert.True(engine.LimitsAvailable);
            }
        }

        [Fact]
        public void New_session_resets_laps()
        {
            using (var dir = new TempDir())
            {
                var engine = new TelemetryEngine(dir.Path, new FakeBundle());
                engine.Update(AccSnapshot(0.20), new FakeRaw(), 0);
                var first = engine.Laps.Current;
                var next = AccSnapshot(0.21);
                next.SessionId = Guid.NewGuid();
                engine.Update(next, new FakeRaw(), 0.1);
                Assert.NotSame(first, engine.Laps.Current);
            }
        }
    }
}
