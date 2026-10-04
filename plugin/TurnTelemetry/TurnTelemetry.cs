using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using GameReaderCommon;
using SimHub.Plugins;
using TurnTelemetry.Core.Engine;
using TurnTelemetry.Core.Input;
using TurnTelemetry.Core.Sims;
using TurnTelemetry.Core.Turns;

namespace TurnTelemetryHost
{
    public sealed class PluginSettings
    {
        public int Bins = 400;
        public string PressureUnit = "psi";
        public string AcRootOverride = "";
        public double UnsupportedNoticeSeconds = 4;
        /// <summary>Game name → "Used" or "Remaining" (how that sim's TyreWear should be read).</summary>
        public Dictionary<string, string> WearMeaning = new Dictionary<string, string>();
    }

    /// <summary>
    /// SimHub entry point. The class name is the property prefix, so dashboards bind to [TurnTelemetry.*]
    /// (contract in docs/architecture.md).
    /// </summary>
    [PluginName("Turn Telemetry")]
    [PluginDescription("Continuous lap telemetry, NEXT/CURRENT turn tracking and tyre state for the Turn Telemetry dashboard.")]
    [PluginAuthor("StormFuel")]
    public sealed class TurnTelemetry : IPlugin, IDataPlugin, IWPFSettingsV2
    {
        private const string SettingsName = "TurnTelemetrySettings";
        private const double ErrorLogIntervalSeconds = 10;
        private const double StandingsIntervalSeconds = 0.25;

        private readonly GameSnapshot _snapshot = new GameSnapshot();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private SimHubRawData _raw;
        private double _lastErrorLog = double.NegativeInfinity;
        private double _lastStandings = double.NegativeInfinity;

        public PluginManager PluginManager { get; set; }
        public TelemetryEngine Engine { get; private set; }
        public PluginSettings Settings { get; private set; }
        public string UserRoot { get; private set; }

        /// <summary>Wall-clock seconds since the plugin started (runs in menus too).</summary>
        public double ClockSeconds => _clock.Elapsed.TotalSeconds;

        public string LeftMenuTitle => "Turn Telemetry";
        public ImageSource PictureIcon => MenuIcon.Value;

        public void Init(PluginManager pluginManager)
        {
            SimHub.Logging.Current.Info("Turn Telemetry: starting");
            Settings = this.ReadCommonSettings(SettingsName, () => new PluginSettings());
            UserRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PluginsData", "TurnTelemetry");
            var options = BuildOptions(Settings);
            options.SimHubRoot = AppDomain.CurrentDomain.BaseDirectory;
            Engine = new TelemetryEngine(UserRoot, new EmbeddedBundledData(), options);
            _raw = new SimHubRawData(pluginManager);

            PropertyPublisher.Attach(this, Engine);

            this.AddAction("MarkTurnStart", (pm, action) => Log(Engine.MarkTurnStart()), (pm, action) => { });
            this.AddAction("MarkTurnEnd", (pm, action) => Log(Engine.MarkTurnEnd()), (pm, action) => { });
            this.AddAction("ClearReferenceLap", (pm, action) => Engine.Laps.ClearReference(), (pm, action) => { });

            // SET LINE (dashboard button via ButtonItem.TriggerAction; also bindable to a wheel button).
            this.AddAction("SetStartLine", (pm, action) => Engine.SetStartLine(), (pm, action) => { });
            this.AddAction("ResetTurns", (pm, action) => Engine.ResetTurns(), (pm, action) => { });

            // RESET LAPS (dashboard button; also bindable to a wheel button): press twice within 3 s.
            this.AddAction("ResetSession", (pm, action) => Log(Engine.RequestReset(ClockSeconds)), (pm, action) => { });

            // Problem report (also a button in the settings tab); bind to a wheel button to capture the moment.
            this.AddAction("SaveReport", (pm, action) => Log("problem report saved: " + ReportWriter.Write(this)), (pm, action) => { });
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            if (!data.GameRunning || data.NewData == null) return;
            try
            {
                SnapshotMapper.Fill(_snapshot, data);
                Engine.Update(_snapshot, _raw, _clock.Elapsed.TotalSeconds);

                // Standings change slowly; 4 Hz keeps the opponent copy off the 60 Hz path.
                if (_clock.Elapsed.TotalSeconds - _lastStandings >= StandingsIntervalSeconds)
                {
                    _lastStandings = _clock.Elapsed.TotalSeconds;
                    Engine.Standings.Update(StandingsMapper.Drivers(data.NewData));
                }
            }
            catch (Exception ex)
            {
                var now = _clock.Elapsed.TotalSeconds;
                if (now - _lastErrorLog > ErrorLogIntervalSeconds)
                {
                    _lastErrorLog = now;
                    SimHub.Logging.Current.Error("Turn Telemetry: DataUpdate failed", ex);
                    Engine.Log.Add("error: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        public void End(PluginManager pluginManager)
        {
            SaveSettings();
        }

        public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager) => new SettingsControl(this);

        public void SaveSettings()
        {
            this.SaveCommonSettings(SettingsName, Settings);
            ApplyOptions(Engine.Options, Settings);
        }

        public static EngineOptions BuildOptions(PluginSettings settings)
        {
            var options = new EngineOptions();
            ApplyOptions(options, settings);
            return options;
        }

        /// <summary>Options are read at session start, so most changes apply from the next session.</summary>
        private static void ApplyOptions(EngineOptions options, PluginSettings settings)
        {
            options.Bins = Math.Max(50, Math.Min(2000, settings.Bins));
            options.PressureUnit = settings.PressureUnit;
            options.AcRootOverride = settings.AcRootOverride;
            options.UnsupportedNoticeSeconds = settings.UnsupportedNoticeSeconds;
            options.WearOverrides.Clear();
            foreach (var pair in settings.WearMeaning ?? new Dictionary<string, string>())
                if (Enum.TryParse(pair.Value, true, out WearMeaning meaning)) options.WearOverrides[pair.Key] = meaning;
        }

        private static void Log(string message) => SimHub.Logging.Current.Info("Turn Telemetry: " + message);

        private static readonly Lazy<ImageSource> MenuIcon = new Lazy<ImageSource>(() =>
        {
            // SimHub draws menu icons as one-colour glyphs (only the shape counts; it paints them in the menu colour), so
            // this is the brand mark (docs/brand/mark.svg) simplified: the track as the shape and the car as a dot set in
            // a cut-out in the line, no background and no kerbs (2026-10-03: the full-colour mark showed as a white square).
            const string track = "M 56 150 L 372 150 A 46 46 0 0 1 372 242 L 274 242 A 52 52 0 0 0 222 294 L 222 448";
            var car = new System.Windows.Point(114, 150);
            var trackPen = new Pen(Brushes.White, 56)
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            var trackShape = Geometry.Parse(track).GetWidenedPathGeometry(trackPen);
            var withGap = new CombinedGeometry(GeometryCombineMode.Exclude, trackShape, new EllipseGeometry(car, 54, 54));
            var group = new DrawingGroup();
            // Invisible square keeps the glyph's 512 x 512 frame, so it's centred and scaled like the full mark.
            group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new System.Windows.Rect(0, 0, 512, 512))));
            group.Children.Add(new GeometryDrawing(Brushes.White, null, withGap));
            group.Children.Add(new GeometryDrawing(Brushes.White, null, new EllipseGeometry(car, 38, 38)));
            var image = new DrawingImage(group);
            image.Freeze();
            return image;
        });
    }
}
