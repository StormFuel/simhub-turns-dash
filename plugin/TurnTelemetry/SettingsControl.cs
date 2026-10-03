using System;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TurnTelemetry.Core.Engine;
using TurnTelemetry.Core.Lap;
using TurnTelemetry.Core.Turns;
using TurnTelemetry.Core.Tyres;

namespace TurnTelemetryHost
{
    /// <summary>
    /// Settings and live diagnostics, mirroring the Turns app's diagnostics panel.
    /// Built in code so the host project needs no XAML tooling.
    /// </summary>
    internal sealed class SettingsControl : UserControl
    {
        private readonly TurnTelemetry _plugin;
        private readonly TextBlock _diagnostics;
        private readonly DispatcherTimer _timer;

        public SettingsControl(TurnTelemetry plugin)
        {
            _plugin = plugin;
            var settings = plugin.Settings;

            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(Heading("Turn Telemetry " + typeof(TurnTelemetry).Assembly.GetName().Version.ToString(3)));

            // --- Settings
            root.Children.Add(Heading("Settings", 14));
            var unit = new ComboBox { Width = 120, ItemsSource = new[] { "psi", "bar", "kPa" }, SelectedItem = settings.PressureUnit };
            unit.SelectionChanged += (s, a) => { settings.PressureUnit = (string)unit.SelectedItem; Save(); };
            root.Children.Add(Row("Pressure unit", unit));

            var acRoot = new TextBox { Width = 420, Text = settings.AcRootOverride ?? "" };
            acRoot.LostFocus += (s, a) => { settings.AcRootOverride = acRoot.Text.Trim(); Save(); };
            root.Children.Add(Row("AC install folder (blank = auto)", acRoot));

            var wear = new ComboBox { Width = 120, ItemsSource = new[] { "Sim default", "Remaining", "Used" }, SelectedIndex = 0 };
            wear.DropDownOpened += (s, a) => wear.SelectedItem = CurrentWearSetting();
            wear.SelectionChanged += (s, a) =>
            {
                var game = _plugin.Engine.GameName;
                if (game == null || wear.SelectedItem == null) return;
                if ((string)wear.SelectedItem == "Sim default") settings.WearMeaning.Remove(game);
                else settings.WearMeaning[game] = (string)wear.SelectedItem;
                Save();
            };
            root.Children.Add(Row("Tyre wear values mean (current sim)", wear));

            // --- Turns
            root.Children.Add(Heading("Turn editor", 14));
            root.Children.Add(new TurnEditor(plugin));

            root.Children.Add(Heading("Turn recorder (manual)", 14));
            root.Children.Add(Note("Bind MarkTurnStart / MarkTurnEnd to wheel buttons in Controls and events, or use these buttons while driving."));
            var buttons = new WrapPanel();
            buttons.Children.Add(Button("Mark turn start", () => _plugin.Engine.MarkTurnStart()));
            buttons.Children.Add(Button("Mark turn end", () => _plugin.Engine.MarkTurnEnd()));
            buttons.Children.Add(Button("Delete user turn file", () => _plugin.Engine.DeleteUserTurnFile()));
            buttons.Children.Add(Button("Open data folder", OpenDataFolder));
            buttons.Children.Add(Button("Clear reference lap", () => _plugin.Engine.Laps.ClearReference()));
            root.Children.Add(buttons);

            // --- What each sim supports (also in docs/user-guide.md)
            root.Children.Add(Heading("Good to know", 14));
            root.Children.Add(Note("Track limits: counted per turn from the tyres-out count in Assetto Corsa (3+ tyres), and from lap " +
                                   "invalidation elsewhere (first offence per lap). In ACC races the game reports nothing for track " +
                                   "limits (its tyres-out value is always 0 and laps aren't invalidated), so the dashboard shows " +
                                   "\"TRACK LIMITS N/A IN RACE\". ACC practice and qualifying work."));
            root.Children.Add(Note("Sectors: SimHub doesn't say where sector lines are, so each boundary is learned the first time you " +
                                   "cross it and saved per track. Colours: purple = fastest of anyone, green = your best, yellow = " +
                                   "slower, red = track limits exceeded in that sector."));
            root.Children.Add(Note("TC cut, wipers and lights are read from ACC's own data; other sims show N/A."));
            root.Children.Add(Note("Turns: curated official numbering for some tracks; others are auto-numbered from the SimHub track " +
                                   "map. Check them in the turn editor above, or tap SET LINE on the dashboard as you cross the start line."));
            var guide = new TextBlock { Margin = new Thickness(0, 2, 0, 6) };
            var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("User guide and known limits on GitHub"))
            {
                NavigateUri = new Uri("https://github.com/StormFuel/simhub-turns-dash/blob/main/docs/user-guide.md")
            };
            link.RequestNavigate += (s, a) => Process.Start(a.Uri.AbsoluteUri);
            guide.Inlines.Add(link);
            root.Children.Add(guide);

            // --- Diagnostics
            root.Children.Add(Heading("Live diagnostics", 14));
            _diagnostics = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 12, TextWrapping = TextWrapping.Wrap };
            root.Children.Add(_diagnostics);

            Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += (s, a) => _diagnostics.Text = Diagnostics(_plugin.Engine);
            Loaded += (s, a) => _timer.Start();
            Unloaded += (s, a) => _timer.Stop();
        }

        private string CurrentWearSetting()
        {
            var game = _plugin.Engine.GameName;
            return game != null && _plugin.Settings.WearMeaning.TryGetValue(game, out var v) ? v : "Sim default";
        }

        private void Save()
        {
            try { _plugin.SaveSettings(); }
            catch (Exception ex) { SimHub.Logging.Current.Error("Turn Telemetry: saving settings failed", ex); }
        }

        private void OpenDataFolder()
        {
            Directory.CreateDirectory(_plugin.UserRoot);
            Process.Start("explorer.exe", _plugin.UserRoot);
        }

        internal static string Diagnostics(TelemetryEngine e)
        {
            var sb = new StringBuilder();
            if (e.GameName == null) return "Waiting for a game session...";

            var caps = e.Adapter.Capabilities;
            sb.AppendLine($"Game        {e.GameName}   adapter={e.Adapter.Id}   steering={caps.Steering} tc={caps.TcActive} abs={caps.AbsActive} wear={caps.TyreWear} lapPos={e.HasLapPosition}");
            sb.AppendLine($"Track key   {e.TrackKeyId}");
            sb.AppendLine($"Turns       source={CatalogResult.SourceId(e.Catalog.Source)}   count={e.Catalog.Turns.Count}   {e.Catalog.Detail}");
            sb.AppendLine($"            {e.Catalog.Path}");
            sb.AppendLine($"Turn state  {e.TurnStateId,-8} focal={e.Focal?.Label ?? "-"}   last={e.Turn.Last?.Label ?? "-"}   notice={e.NoticeVisible}");

            var f = e.LastFrame;
            var steer = double.IsNaN(f.Steer) ? "n/a" : f.Steer.ToString("+0.000;-0.000");
            sb.AppendLine($"Live        pos={f.LapPos:0.0000}{(e.PercentScaleDetected ? " (reader reports 0..100)" : "")}   thr={f.Throttle:P0} brk={f.Brake:P0} steer={steer} tc={f.Tc} abs={f.Abs}");

            var cur = e.Laps.Current;
            if (cur != null)
                sb.AppendLine($"Lap         coverage={cur.Coverage:P1}   valid={cur.Valid}{(cur.Valid ? "" : " (" + cur.InvalidReason + ")")}");
            sb.AppendLine($"Laps        last={Time(e.Laps.Last)}   best={Time(e.Laps.Best)}   reference={e.Laps.ReferenceKind}");
            var last = e.Laps.Last;
            if (last != null)
                sb.AppendLine($"Last lap    #{last.LapNumber}   coverage={last.Coverage:P1}   {(last.Valid ? "valid" : "invalid: " + last.InvalidReason)}");

            sb.AppendLine($"Tyres       preset={e.Tyres.Preset.Id}   compound={e.Tyres.Compound ?? "-"}   wear={e.WearMeaning}   unit={e.Tyres.PressureUnit}/°{e.Tyres.TemperatureUnit}   zones={e.Tyres.ZonesDetected}   temps={(e.TyreTempsFromRaw ? "raw" : "simhub")}");
            for (var i = 0; i < 4; i++)
            {
                var c = e.Tyres.Corners[i];
                sb.AppendLine($"  {TyreCorner.Names[i]}  I/M/O {c.TempInner,5:0.0} {c.TempMiddle,5:0.0} {c.TempOuter,5:0.0}  avg {c.TempAverage,5:0.0}   " +
                              $"P {(double.IsNaN(c.Pressure) ? "  n/a" : c.Pressure.ToString("0.00"))} {c.PressureState,-7}   wear {(double.IsNaN(c.WearRemaining) ? "  n/a" : c.WearRemaining.ToString("0.0") + "%")}");
            }
            sb.AppendLine($"Tyre temps  {e.RawTyreProbe}");
            if (e.RawTyreProbe.StartsWith("no SimHub"))
            {
                sb.AppendLine($"Raw tyres   {(e.RawTyreCandidates.Count == 0 ? "no TyreCore/TyreTemp raw properties found" : "")}");
                foreach (var candidate in e.RawTyreCandidates) sb.AppendLine("            " + candidate);
            }
            sb.AppendLine($"Sectors     count={e.Sectors.Count}   boundaries={string.Join(", ", e.Sectors.Boundaries.Select(b => b.ToString("0.000")))}   current=S{e.CurrentSector}   sim index={e.SectorIndex}");
            sb.AppendLine($"            last rating: {e.SectorTimes.LastRating}");
            sb.AppendLine($"            bests: {e.SectorTimes.Bests(e.Sectors.Count)}");
            sb.AppendLine($"Limits      available={e.LimitsAvailable}   total={e.Limits.SessionTotal}   last=T{e.Limits.LastTurn?.Label ?? "-"} ({e.Limits.Source})   tyres out={(e.TyresOut?.ToString() ?? "n/a")}/{e.Adapter.Capabilities.TyresOutLimit}   raw={e.TyresOutPath ?? "not found"}");
            sb.AppendLine($"Recorder    {e.RecorderStatus}");
            sb.AppendLine($"User file   {e.UserTurnFilePath}");
            return sb.ToString();
        }

        private static string Time(LapTrace lap)
        {
            if (lap == null) return "-";
            var t = lap.LapTimeSeconds > 0 ? TimeSpan.FromSeconds(lap.LapTimeSeconds).ToString(@"m\:ss\.fff") : "pending";
            return lap.Valid ? t : t + " (invalid)";
        }

        private static TextBlock Heading(string text, double size = 18) =>
            new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) };

        private static TextBlock Note(string text) =>
            new TextBlock { Text = text, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };

        private static FrameworkElement Row(string label, FrameworkElement control)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            row.Children.Add(new TextBlock { Text = label, Width = 240, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(control);
            return row;
        }

        private static Button Button(string text, Action action)
        {
            var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(10, 4, 10, 4) };
            button.Click += (s, a) => action();
            return button;
        }
    }
}
