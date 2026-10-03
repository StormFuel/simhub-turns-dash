using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TurnTelemetry.Core.Input;
using TurnTelemetry.Core.Lap;
using TurnTelemetry.Core.Sims;
using TurnTelemetry.Core.Standings;
using TurnTelemetry.Core.Turns;
using TurnTelemetry.Core.Tyres;

namespace TurnTelemetry.Core.Engine
{
    public sealed class EngineOptions
    {
        public int Bins = 400;
        public string PressureUnit = "psi";
        public double UnsupportedNoticeSeconds = 4;
        /// <summary>Overrides the AC install folder; otherwise derived from SimHub's GamePath.</summary>
        public string AcRootOverride;
        /// <summary>SimHub install folder, for its track maps (PluginsData/{game}/MapRecords*).</summary>
        public string SimHubRoot;
        /// <summary>Per game: whether SimHub's TyreWear values mean wear used or remaining (spike S6b).</summary>
        public Dictionary<string, WearMeaning> WearOverrides = new Dictionary<string, WearMeaning>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Owns the per-session pipeline: adapter → frame normaliser → lap store / turn tracker / tyre model.
    /// The SimHub host feeds it one snapshot per DataUpdate and publishes its state as properties.
    /// </summary>
    public sealed class TelemetryEngine
    {
        /// <summary>2: Set Turn 1 mode replaced by SET LINE (SetT1.* properties removed, StartLine.* added).</summary>
        public const int Contract = 2;
        private const double TurnRetrySeconds = 5;

        private static readonly string DefaultAcRoot =
            @"C:\Program Files (x86)\Steam\steamapps\common\assettocorsa";

        private readonly object _gate = new object();
        private readonly string _userRoot;
        private readonly IBundledData _bundled;
        private readonly IReadOnlyList<ISimAdapter> _adapters;
        private readonly FrameNormaliser _normaliser = new FrameNormaliser();
        private readonly TurnRecorder _recorder = new TurnRecorder();
        private readonly TurnCatalog _catalog;
        private readonly List<TyrePreset> _bundledPresets;

        private string _sessionKey;
        private string _compound;
        private double _nextTurnRetry;
        private GameSnapshot _snapshot;
        private IRawData _raw;

        public TelemetryEngine(string userRoot, IBundledData bundled, EngineOptions options = null,
            IReadOnlyList<ISimAdapter> adapters = null)
        {
            _userRoot = userRoot;
            _bundled = bundled;
            _adapters = adapters ?? SimAdapters.Default;
            Options = options ?? new EngineOptions();
            _catalog = new TurnCatalog(userRoot, bundled);
            Laps = new LapStore(Options.Bins);
            Tyres = new TyreModel { PressureUnit = Options.PressureUnit };
            _bundledPresets = LoadPresets(bundled?.Read("data/tyres/presets.json"));
        }

        public EngineOptions Options { get; }
        public ISimAdapter Adapter { get; private set; } = new GenericAdapter();
        public string GameName { get; private set; }
        public string TrackKeyId { get; private set; }
        public CatalogResult Catalog { get; private set; } = CatalogResult.Empty;
        public TurnState Turn { get; private set; } = TurnState.Empty;
        public LapStore Laps { get; }
        public TyreModel Tyres { get; }
        public Frame LastFrame { get; private set; }
        public bool HasFrame { get; private set; }
        public double Now { get; private set; }
        public double NoticeUntil { get; private set; }
        public string RecorderStatus { get; private set; } = "idle";
        public StartLineSetter StartLine { get; } = new StartLineSetter();

        /// <summary>Sector boundaries (learned from the sim's sector index, saved per track) and sector timing.</summary>
        public SectorMap Sectors { get; } = new SectorMap();
        public SectorTimer SectorTimes { get; } = new SectorTimer();
        public int CurrentSector => Sectors.SectorAt(LastFrame.LapPos);
        /// <summary>Your best for 1-based sector n from the game: the driver list or SimHub's own sector bests.</summary>
        public double GameBestSector(int n)
        {
            var fromList = Standings.PlayerBestSector(n);
            var own = _snapshot != null && n >= 1 && n <= 3 && _snapshot.BestSectors[n - 1] > 0 ? _snapshot.BestSectors[n - 1] : double.NaN;
            return double.IsNaN(fromList) ? own : double.IsNaN(own) ? fromList : Math.Min(fromList, own);
        }

        /// <summary>The sim's raw sector index, for diagnostics.</summary>
        public int SectorIndex => _snapshot?.CurrentSectorIndex ?? 0;

        /// <summary>Track-limit excursions per turn (this lap / session).</summary>
        public TrackLimits Limits { get; } = new TrackLimits();
        /// <summary>Tyres outside the limits as the sim reports it, or null; and the raw property it comes from.</summary>
        public int? TyresOut { get; private set; }
        public string TyresOutPath { get; private set; }

        /// <summary>False when this sim gives nothing to detect track-limit excursions from in this session (ACC races).</summary>
        public bool LimitsAvailable => Adapter.Capabilities.TyresOutLimit > 0 || Adapter.Capabilities.InvalidatesLapsInRace
                                       || (_snapshot?.SessionTypeName ?? "").IndexOf("RACE", StringComparison.OrdinalIgnoreCase) < 0;
        private double _nextTyresOutSearch;

        /// <summary>Standings window for the dashboard; the host feeds it from SimHub's opponent list.</summary>
        public StandingsBoard Standings { get; } = new StandingsBoard();

        /// <summary>Car settings SimHub doesn't normalise, read by the sim adapter; null when unknown.</summary>
        public int? TcCut { get; private set; }
        public int? Wipers { get; private set; }
        public int? Lights { get; private set; }

        public static string WipersText(int? stage) => stage == null ? "N/A" : stage == 0 ? "OFF" : "ON " + stage;
        public static string LightsText(int? stage) => stage == null ? "N/A" : stage == 0 ? "OFF" : stage == 1 ? "ON" : stage == 2 ? "HIGH" : "ON " + stage;
        public bool PercentScaleDetected => _normaliser.PercentScale;
        /// <summary>
        /// Latched once the sim reports a non-zero lap position this session. Open-world sims (Forza Horizon) never do,
        /// so laps, turns and the recorder do not apply there.
        /// </summary>
        public bool HasLapPosition { get; private set; }

        public bool NoticeVisible => GameName != null && !Catalog.Supported && Now < NoticeUntil;
        public TurnDefinition Focal => Turn.Current ?? Turn.Next;

        /// <summary>
        /// The turn after the focal one: NEXT while in a corner, otherwise the one after the corner being approached.
        /// </summary>
        public TurnDefinition Upcoming
        {
            get
            {
                var turns = Catalog.Turns;
                if (Turn.Current != null) return Turn.Next;
                if (Turn.Next == null || turns.Count < 2) return null;
                var ordered = turns.OrderBy(t => t.Start).ToList();
                var i = ordered.IndexOf(Turn.Next);
                return i < 0 ? null : ordered[(i + 1) % ordered.Count];
            }
        }
        public string TurnStateId => Turn.Current != null ? "CURRENT" : Turn.Next != null ? "NEXT" : "NONE";
        public WearMeaning WearMeaning =>
            GameName != null && Options.WearOverrides.TryGetValue(GameName, out var m) ? m : Adapter.Capabilities.Wear;

        public void Update(GameSnapshot s, IRawData raw, double now)
        {
            lock (_gate)
            {
                Now = now;
                _snapshot = s;
                _raw = raw;

                var key = string.Join("|", s.GameName, s.TrackIdWithConfig, s.CarId, s.SessionTypeName, s.SessionId);
                if (key != _sessionKey)
                {
                    _sessionKey = key;
                    StartSession(s, raw);
                }
                else if (Catalog.Source == TurnSource.None && now >= _nextTurnRetry)
                {
                    // AC's StaticInfo and SimHub's track map can both arrive after the session starts.
                    LoadTurns(s, raw, showNotice: false);
                }

                var compound = Adapter.ReadCompound(raw);
                if (compound != _compound) SelectPreset(s, compound);
                ApplyRawTyreTemps(s, raw);
                TcCut = Adapter.ReadTcCut(raw);
                Wipers = Adapter.ReadWipers(raw);
                Lights = Adapter.ReadLights(raw);
                DiscoverRawTyreProperties(raw);
                Tyres.Update(s, WearMeaning, Adapter.Capabilities.TyreWear);

                if (s.Paused || s.Replay || s.Spectating) return;

                var frame = _normaliser.Normalise(s, Adapter, raw, now);
                LastFrame = frame;
                HasFrame = true;
                if (frame.LapPos > 0.0001) HasLapPosition = true;
                Laps.Add(frame);
                Turn = TurnTracker.At(Catalog.Turns, frame.LapPos);
                if (HasLapPosition)
                {
                    ReadTyresOut(raw);
                    Limits.Update(Laps.Current, Catalog.Turns, frame.LapPos, TyresOut, Adapter.Capabilities.TyresOutLimit,
                        frame.LapInvalidated, frame.CurrentLapTime.TotalSeconds, now);
                    Sectors.Observe(s.CurrentSectorIndex, frame.LapPos, frame.Discontinuity);
                    if (Sectors.Dirty) SaveSectors();
                    SectorTimes.Update(Laps.Current, Sectors, frame.LapPos, frame.CurrentLapTime.TotalSeconds,
                        s.CurrentSectorIndex, frame.Discontinuity, Standings.FastestSector, GameBestSector,
                        Limits.Incidents, frame.InPitLane);
                }
            }
        }

        private void StartSession(GameSnapshot s, IRawData raw)
        {
            GameName = s.GameName;
            TrackKeyId = string.IsNullOrEmpty(s.TrackIdWithConfig) ? s.TrackId : s.TrackIdWithConfig;
            Adapter = SimAdapters.For(s.GameName, _adapters);
            _normaliser.Reset();
            Laps.Reset(Options.Bins);
            _recorder.Cancel();
            HasFrame = false;
            HasLapPosition = false;
            StartLine.Reset();
            Tyres.ResetSession();
            SectorTimes.Reset();
            Limits.Reset();
            TyresOut = null;
            TyresOutPath = null;
            _nextTyresOutSearch = 0;
            Standings.Clear();
            Sectors.Load(ReadUserFile(SectorFile.Split('/')));
            Turn = TurnState.Empty;
            LoadTurns(s, raw);
            SelectPreset(s, Adapter.ReadCompound(raw));
        }

        private TrackFolder _acTrack;
        private string _acRoot;

        private void LoadTurns(GameSnapshot s, IRawData raw, bool showNotice = true)
        {
            var acTrack = Adapter.ReadTrackFolder(raw);
            _acTrack = acTrack;
            _acRoot = AcRoot(s.GamePath);
            _nextTurnRetry = Now + TurnRetrySeconds;

            try
            {
                Catalog = _catalog.Resolve(s.GameName, TrackKeyId, _acRoot, acTrack, Options.SimHubRoot);
            }
            catch (Exception ex)
            {
                Catalog = new CatalogResult { Detail = "turn data error: " + ex.Message };
            }
            if (!Catalog.Supported && showNotice) NoticeUntil = Now + Options.UnsupportedNoticeSeconds;
        }

        private void SelectPreset(GameSnapshot s, string compound)
        {
            _compound = compound;
            var preset = TyrePresetMatcher.Select(_bundledPresets, LoadPresets(ReadUserFile("tyres", "presets.json")),
                s.GameName, compound, s.CarClass);
            Tyres.SetPreset(preset, compound);
        }

        // ------------------------------------------------------------------ recorder actions ---

        public string MarkTurnStart()
        {
            lock (_gate)
            {
                if (!HasFrame) return RecorderStatus = "no telemetry yet";
                if (!HasLapPosition) return RecorderStatus = "this sim reports no lap position; turns cannot be recorded";
                _recorder.MarkStart(LastFrame.LapPos);
                return RecorderStatus = $"turn start marked at {LastFrame.LapPos:P1}";
            }
        }

        public string MarkTurnEnd()
        {
            lock (_gate)
            {
                if (!HasFrame || _snapshot == null) return RecorderStatus = "no telemetry yet";
                if (!_recorder.HasPendingStart) return RecorderStatus = "press MarkTurnStart first";

                var path = _catalog.UserFilePath(GameName, TrackKeyId);
                var file = LoadOrSeedUserFile(path);
                var entry = _recorder.MarkEnd(LastFrame.LapPos, file);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, file.ToJson());
                LoadTurns(_snapshot, _raw);
                return RecorderStatus = $"saved turn {entry.Label} ({entry.Start:P1} to {entry.End:P1})";
            }
        }

        public string DeleteUserTurnFile()
        {
            lock (_gate)
            {
                if (GameName == null || _snapshot == null) return RecorderStatus = "no session";
                var path = _catalog.UserFilePath(GameName, TrackKeyId);
                if (File.Exists(path)) File.Delete(path);
                LoadTurns(_snapshot, _raw);
                return RecorderStatus = "user turn file deleted";
            }
        }

        public string UserTurnFilePath => GameName == null ? null : _catalog.UserFilePath(GameName, TrackKeyId);

        // ------------------------------------------------------------------ set start line (dashboard) ---

        /// <summary>
        /// Dashboard SET LINE / wheel button (D15): numbering begins here, so the next corner ahead becomes Turn 1.
        /// Within a few seconds of a change the same button undoes it.
        /// </summary>
        public void SetStartLine()
        {
            lock (_gate)
            {
                if (StartLine.UndoAvailable(Now))
                {
                    UndoStartLine();
                    return;
                }
                if (!HasLapPosition)
                {
                    StartLine.Say(Now, "THIS SIM REPORTS NO LAP POSITION");
                    return;
                }
                if (Turn.Next == null)
                {
                    StartLine.Say(Now, "NO TURNS TO NUMBER YET");
                    return;
                }
                var first = Turn.Next;
                var name = string.IsNullOrWhiteSpace(first.Name) ? "TURN " + first.Label : first.Name.ToUpperInvariant();
                var renumbered = StartLineSetter.Renumber(Catalog.Turns, first);
                if (renumbered == null)
                {
                    StartLine.Say(Now, $"ALREADY NUMBERED FROM HERE \u00b7 TURN 1 = {name}");
                    return;
                }
                var path = _catalog.UserFilePath(GameName, TrackKeyId);
                var had = File.Exists(path);
                StartLine.BeforeSave(Now, had, had ? File.ReadAllText(path) : null);
                SaveTurns(renumbered);
                StartLine.Say(Now, $"LINE SET \u00b7 TURN 1 = {name}", StartLineSetter.UndoSeconds);
            }
        }

        /// <summary>Removes the user's turn file so curated / template / automatic numbering applies again.</summary>
        public void ResetTurns()
        {
            lock (_gate)
            {
                if (GameName == null || _snapshot == null) return;
                var path = _catalog.UserFilePath(GameName, TrackKeyId);
                var had = File.Exists(path);
                if (had) File.Delete(path);
                LoadTurns(_snapshot, _raw, showNotice: false);
                StartLine.Reset();
                StartLine.Say(Now, had ? "TURNS RESET \u00b7 " + CatalogResult.SourceId(Catalog.Source).ToUpperInvariant() : "NOTHING TO RESET");
            }
        }

        private void UndoStartLine()
        {
            if (!StartLine.TryTakeUndo(Now, out var had, out var previous)) return;
            var path = _catalog.UserFilePath(GameName, TrackKeyId);
            if (had) File.WriteAllText(path, previous);
            else if (File.Exists(path)) File.Delete(path);
            LoadTurns(_snapshot, _raw, showNotice: false);
            StartLine.Say(Now, "UNDONE");
        }

        // ------------------------------------------------------------------ turn editor ---

        /// <summary>Minimum coverage for a lap to be used for corner detection.</summary>
        public const double DetectionMinCoverage = 0.9;
        /// <summary>Below this a partial lap is not worth detecting from.</summary>
        public const double DetectionPartialCoverage = 0.3;

        /// <summary>The turns currently loaded, as editable candidates.</summary>
        public List<TurnCandidate> CurrentTurnCandidates()
        {
            lock (_gate)
                return Catalog.Turns.Select(t => new TurnCandidate
                    { Label = t.Label, Name = t.Name ?? "", Start = t.Start, End = t.End }).ToList();
        }

        /// <summary>Every section of the current AC track's sections.ini; empty with a message otherwise.</summary>
        public List<TurnCandidate> AcSectionCandidates(out string message)
        {
            lock (_gate)
            {
                if (!string.Equals(GameName, SimAdapters.AssettoCorsa, StringComparison.OrdinalIgnoreCase))
                {
                    message = "sections.ini is only available in Assetto Corsa";
                    return new List<TurnCandidate>();
                }
                var path = _acTrack == null ? null : AcSectionsReader.FindFile(_acRoot, _acTrack.Track, _acTrack.Layout);
                if (path == null)
                {
                    message = "no sections.ini found for this track";
                    return new List<TurnCandidate>();
                }
                var list = AcSectionCandidates_FromFile(path);
                message = $"{list.Count} sections from {path}";
                return list;
            }
        }

        /// <summary>Corners detected from SimHub's track map for this game and track (no driving needed).</summary>
        public List<TurnCandidate> MapCandidates(out string message)
        {
            lock (_gate)
            {
                var path = TrackMapFile.Find(Options.SimHubRoot, GameName, TrackKeyId);
                if (path == null)
                {
                    message = "SimHub has no track map for this track yet (it downloads or records one while you drive)";
                    return new List<TurnCandidate>();
                }
                var list = TrackGeometry.ToCandidates(TrackGeometry.Detect(TrackMapFile.Load(path), out var length));
                message = $"{list.Count} corners from {Path.GetFileName(path)} ({length:0} m)";
                return list;
            }
        }

        private static List<TurnCandidate> AcSectionCandidates_FromFile(string path) =>
            Turns.AcSectionCandidates.FromIni(File.ReadAllText(path));

        /// <summary>Corners detected in the best lap (or the last lap with enough coverage).</summary>
        public List<TurnCandidate> DetectCorners(out string message)
        {
            lock (_gate)
            {
                if (!HasLapPosition)
                {
                    message = "this sim reports no lap position";
                    return new List<TurnCandidate>();
                }
                if (!Adapter.Capabilities.Steering)
                {
                    message = $"corner detection needs steering, which the '{Adapter.Id}' adapter cannot read";
                    return new List<TurnCandidate>();
                }
                // Best lap if it is complete, otherwise whichever lap covers most of the track (the out-lap counts).
                var lap = Laps.Best != null && Laps.Best.Coverage >= DetectionMinCoverage ? Laps.Best
                    : new[] { Laps.Last, Laps.Current }.Where(l => l != null).OrderByDescending(l => l.Coverage).FirstOrDefault();
                if (lap == null || lap.Coverage < DetectionPartialCoverage)
                {
                    message = $"drive more of the lap first ({(lap?.Coverage ?? 0):P0} covered; or use Load from track map)";
                    return new List<TurnCandidate>();
                }
                var list = CornerDetector.Detect(lap);
                TurnNumbering.AutoNumber(list, 0);
                var which = ReferenceEquals(lap, Laps.Best) ? "best lap" : ReferenceEquals(lap, Laps.Current) ? "current lap" : "last lap";
                message = $"{list.Count} corners detected in the {which} ({lap.Coverage:P0} covered)"
                          + (lap.Coverage < DetectionMinCoverage ? "; the rest of the lap is missing" : "");
                return list;
            }
        }

        /// <summary>Saves the reviewed list as the user turn file for this track and loads it.</summary>
        public string SaveTurns(IEnumerable<TurnCandidate> candidates)
        {
            lock (_gate)
            {
                if (GameName == null || _snapshot == null) return RecorderStatus = "no session";
                var path = _catalog.UserFilePath(GameName, TrackKeyId);
                var file = TurnNumbering.ToFile(candidates, new TurnFile
                {
                    Game = GameName,
                    Track = TrackKeyId,
                    TrackName = _snapshot.TrackName,
                    LengthMeters = _snapshot.TrackLengthMeters > 0 ? _snapshot.TrackLengthMeters : (double?)null,
                    Source = $"turn editor, Turn Telemetry, {DateTime.Now:yyyy-MM}",
                });
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, file.ToJson());
                LoadTurns(_snapshot, _raw);
                return RecorderStatus = $"saved {file.Turns.Count} turns";
            }
        }

        private TurnFile LoadOrSeedUserFile(string path)
        {
            if (File.Exists(path))
            {
                var existing = TurnFile.Parse(File.ReadAllText(path));
                if (existing != null) return existing;
            }

            // Start from whatever turns are loaded now, so recording extends curated / sections.ini data.
            return new TurnFile
            {
                Game = GameName,
                Track = TrackKeyId,
                TrackName = _snapshot.TrackName,
                LengthMeters = _snapshot.TrackLengthMeters > 0 ? _snapshot.TrackLengthMeters : (double?)null,
                Source = $"recorded with Turn Telemetry, {DateTime.Now:yyyy-MM}",
                Turns = Catalog.Turns.Select(t => new TurnFile.TurnEntry
                    { Label = t.Label, Name = t.Name, Start = t.Start, End = t.End }).ToList(),
            };
        }

        // ------------------------------------------------------------------ helpers ---

        private readonly double[,] _rawTemps = new double[4, 4];

        /// <summary>Outcome of the last raw tyre temperature attempt, for diagnostics.</summary>
        public string RawTyreProbe { get; private set; } = "not run";

        /// <summary>True when the last frame's tyre temperatures came from the adapter's raw fallback.</summary>
        public bool TyreTempsFromRaw { get; private set; }

        private const double RawDiscoverySeconds = 5;
        private double _nextRawDiscovery;

        /// <summary>Raw properties that look like tyre temperatures ("path = value"), refreshed while the fallback finds nothing.</summary>
        public IReadOnlyList<string> RawTyreCandidates { get; private set; } = Array.Empty<string>();

        private void DiscoverRawTyreProperties(IRawData raw)
        {
            if (TyreTempsFromRaw || !RawTyreProbe.StartsWith("no SimHub") || Now < _nextRawDiscovery || raw == null) return;
            _nextRawDiscovery = Now + RawDiscoverySeconds;
            try
            {
                RawTyreCandidates = raw.FindPaths("TyreCore").Concat(raw.FindPaths("TyreTemp")).Concat(raw.FindPaths("tyreTemp"))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(12)
                    .Select(p => $"{p} = {Describe(raw.Get(p))}").ToList();
            }
            catch (Exception ex)
            {
                RawTyreCandidates = new[] { "discovery failed: " + ex.Message };
            }
        }

        private static string Describe(object value)
        {
            if (value is System.Collections.IList list)
                return "[" + string.Join(", ", list.Cast<object>().Select(v => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture))) + "]";
            return value == null ? "null" : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Fills gaps in SimHub's tyre temperatures. Some sims (ACC) give only the per-tyre average: copy it into
        /// inner/middle/outer. When every value is empty, use the adapter's raw °C values instead, converted to
        /// SimHub's display unit so the snapshot stays consistent.
        /// </summary>
        private void ApplyRawTyreTemps(GameSnapshot s, IRawData raw)
        {
            var fahrenheit = TemperatureUnits.IsFahrenheit(s.TemperatureUnit);
            double ToCelsius(double v) => fahrenheit ? TemperatureUnits.ToCelsius(v) : v;
            bool Present(double v) => ToCelsius(v) > 0;

            TyreTempsFromRaw = false;
            var anyPresent = false;
            var filled = 0;
            foreach (var t in s.Tyres)
            {
                var zones = Present(t.TempInner) || Present(t.TempMiddle) || Present(t.TempOuter);
                if (!zones && Present(t.TempAverage))
                {
                    t.TempInner = t.TempMiddle = t.TempOuter = t.TempAverage;
                    filled++;
                }
                anyPresent |= zones || Present(t.TempAverage);
            }
            if (anyPresent)
            {
                RawTyreProbe = filled > 0
                    ? $"SimHub gives only the tyre average on {filled} tyre(s); used for all zones"
                    : "SimHub temperatures used";
                return;
            }

            if (!Adapter.ReadTyreTemps(raw, _rawTemps))
            {
                RawTyreProbe = $"no SimHub temperatures; adapter '{Adapter.Id}' raw read found nothing";
                return;
            }

            double FromCelsius(double c) => c > 0 ? (fahrenheit ? TemperatureUnits.ToFahrenheit(c) : c) : 0;
            for (var i = 0; i < 4; i++)
            {
                s.Tyres[i].TempInner = FromCelsius(_rawTemps[i, 0]);
                s.Tyres[i].TempMiddle = FromCelsius(_rawTemps[i, 1]);
                s.Tyres[i].TempOuter = FromCelsius(_rawTemps[i, 2]);
                s.Tyres[i].TempAverage = FromCelsius(_rawTemps[i, 3]);
            }
            TyreTempsFromRaw = true;
            RawTyreProbe = $"raw: FL {_rawTemps[0, 0]:0.0}/{_rawTemps[0, 1]:0.0}/{_rawTemps[0, 2]:0.0} core {_rawTemps[0, 3]:0.0} C";
        }

        /// <summary>SimHub's AC and ACC readers both expose it here (ACSharedMemory Physics.NumberOfTyresOut).</summary>
        private static readonly string[] TyresOutPaths = { "Physics.NumberOfTyresOut", "Physics.numberOfTyresOut" };

        /// <summary>
        /// Reads the known raw path directly: SimHub only lists GameRawData properties when its raw-data option is on,
        /// but reading one by name works regardless (2026-10-03: a name search alone never found it in an ACC race).
        /// A name search over the listed properties is the fallback for other readers. Retried every few seconds.
        /// </summary>
        private void ReadTyresOut(IRawData raw)
        {
            TyresOut = null;
            if (raw == null || Adapter.Capabilities.TyresOutLimit <= 0) return;
            if (TyresOutPath == null && Now >= _nextTyresOutSearch)
            {
                _nextTyresOutSearch = Now + RawDiscoverySeconds;
                TyresOutPath = TyresOutPaths.FirstOrDefault(p => raw.Get(p) != null);
                if (TyresOutPath == null)
                {
                    var paths = raw.FindPaths("TyresOut");
                    TyresOutPath = paths.FirstOrDefault(p => p.IndexOf("Physics", StringComparison.OrdinalIgnoreCase) >= 0)
                                   ?? paths.FirstOrDefault();
                }
            }
            if (TyresOutPath == null) return;
            try
            {
                var v = raw.Get(TyresOutPath);
                if (v != null) TyresOut = (int)Math.Round(Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (FormatException) { }
            catch (InvalidCastException) { }
        }

        private string SectorFile => $"sectors/{TrackKey.Sanitize(GameName)}/{TrackKey.Sanitize(TrackKeyId)}.txt";

        private void SaveSectors()
        {
            var text = Sectors.Serialize();
            if (string.IsNullOrEmpty(_userRoot)) return;
            try
            {
                var path = Path.Combine(new[] { _userRoot }.Concat(SectorFile.Split('/')).ToArray());
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, text);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private string ReadUserFile(params string[] parts)
        {
            if (string.IsNullOrEmpty(_userRoot)) return null;
            var path = Path.Combine(new[] { _userRoot }.Concat(parts).ToArray());
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        private static List<TyrePreset> LoadPresets(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<TyrePreset>();
            try { return TyrePresetFile.Parse(json).Presets ?? new List<TyrePreset>(); }
            catch (Exception) { return new List<TyrePreset>(); }
        }

        private string AcRoot(string gamePath)
        {
            if (!string.IsNullOrWhiteSpace(Options.AcRootOverride)) return Options.AcRootOverride;
            if (!string.IsNullOrWhiteSpace(gamePath))
            {
                var dir = File.Exists(gamePath) ? Path.GetDirectoryName(gamePath) : gamePath;
                for (var i = 0; i < 3 && !string.IsNullOrEmpty(dir); i++)
                {
                    if (Directory.Exists(Path.Combine(dir, "content", "tracks"))) return dir;
                    dir = Path.GetDirectoryName(dir);
                }
            }
            return DefaultAcRoot;
        }
    }
}
