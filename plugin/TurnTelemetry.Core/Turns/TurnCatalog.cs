using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TurnTelemetry.Core.Sims;

namespace TurnTelemetry.Core.Turns
{
    public enum TurnSource
    {
        None,
        User,
        Curated,
        AcSections,
        /// <summary>Official numbering from a circuit template, aligned to this sim's SimHub map.</summary>
        Template,
        /// <summary>AC section names, numbered in order (a starting point; refine in the turn editor).</summary>
        AutoSections,
        /// <summary>Corners detected from the SimHub track map, numbered in order (a starting point).</summary>
        AutoMap,
    }

    public sealed class CatalogResult
    {
        public static readonly CatalogResult Empty = new CatalogResult();

        public TurnSource Source = TurnSource.None;
        public IReadOnlyList<TurnDefinition> Turns = Array.Empty<TurnDefinition>();
        /// <summary>File the turns came from, or the user file path that would be written.</summary>
        public string Path;
        public string Detail;

        public bool Supported => Turns.Count > 0;

        public static string SourceId(TurnSource source)
        {
            switch (source)
            {
                case TurnSource.User: return "user";
                case TurnSource.Curated: return "curated";
                case TurnSource.AcSections: return "ac-sections";
                case TurnSource.Template: return "template";
                case TurnSource.AutoSections: return "auto-sections";
                case TurnSource.AutoMap: return "auto-map";
                default: return "none";
            }
        }
    }

    /// <summary>
    /// Resolves turn definitions for a sim and track. First match wins: user file, bundled curated file, AC sections.ini
    /// with numbered labels, then an automatic starting point: AC section names numbered in order, or corners detected
    /// from SimHub's track map (docs/plan.md D3, amended 2026-10-03). Automatic sources are flagged so the user knows to
    /// review them in the turn editor.
    /// </summary>
    public sealed class TurnCatalog
    {
        private readonly string _userRoot;
        private readonly IBundledData _bundled;
        private readonly List<CircuitTemplate> _templates;

        /// <param name="userRoot">PluginsData/TurnTelemetry; user files live under turns/{game}/{track}.json.</param>
        public TurnCatalog(string userRoot, IBundledData bundled)
        {
            _userRoot = userRoot;
            _bundled = bundled;
            _templates = CircuitTemplates.LoadAll(bundled);
        }

        public int TemplateCount => _templates.Count;

        public string UserFilePath(string game, string track) =>
            System.IO.Path.Combine(_userRoot, TrackKey.RelativePath(game, track).Replace('/', System.IO.Path.DirectorySeparatorChar));

        public CatalogResult Resolve(string game, string track, string acRoot = null, TrackFolder acTrack = null,
            string simHubRoot = null)
        {
            var userPath = UserFilePath(game, track);
            var fromUser = TryFile(File.Exists(userPath) ? File.ReadAllText(userPath) : null, TurnSource.User, userPath);
            if (fromUser != null) return fromUser;

            var bundledPath = "data/" + TrackKey.RelativePath(game, track);
            var fromBundle = TryFile(_bundled?.Read(bundledPath), TurnSource.Curated, bundledPath);
            if (fromBundle != null) return fromBundle;

            // AC sections.ini: explicitly numbered labels win; names alone are kept as a later fallback.
            List<TurnDefinition> named = null;
            string sectionsPath = null;
            if (acTrack != null && string.Equals(game, SimAdapters.AssettoCorsa, StringComparison.OrdinalIgnoreCase))
            {
                sectionsPath = AcSectionsReader.FindFile(acRoot, acTrack.Track, acTrack.Layout);
                if (sectionsPath != null)
                {
                    var ini = File.ReadAllText(sectionsPath);
                    var parsed = AcSectionsReader.Parse(ini);
                    if (parsed.Turns.Count > 0)
                        return new CatalogResult { Source = TurnSource.AcSections, Turns = parsed.Turns, Path = sectionsPath,
                            Detail = $"{parsed.Turns.Count} numbered of {parsed.SectionCount} sections" };
                    named = Numbered(AcSectionCandidates.FromIni(ini));
                }
            }

            List<TurnDefinition> detected = null;
            double length = 0;
            string mapProblem = null;
            var mapPath = TrackMapFile.Find(simHubRoot, game, track);
            if (mapPath != null)
            {
                try
                {
                    var corners = TrackGeometry.Detect(TrackMapFile.Load(mapPath), out length);

                    // Same real circuit curated in another sim: official numbers, positions aligned to this map.
                    var match = CircuitTemplates.Match(_templates, corners, length);
                    if (match != null)
                        return new CatalogResult { Source = TurnSource.Template, Turns = match.Turns, Path = mapPath,
                            Detail = $"official numbering from the {match.Template.Circuit} template ({match.Template.Reference}), "
                                     + $"aligned to this map, score {match.Score:0.00}; review in the turn editor" };
                    detected = Numbered(TrackGeometry.ToCandidates(corners));
                }
                catch (Exception ex)
                {
                    mapProblem = $"track map unreadable: {ex.Message}";
                }
            }

            // Automatic starting points: AC section names (they carry corner names), then map geometry.
            if (named?.Count > 0)
                return new CatalogResult { Source = TurnSource.AutoSections, Turns = named, Path = sectionsPath,
                    Detail = $"{named.Count} AC sections numbered in order (auto; review in the turn editor)" };
            if (detected?.Count > 0)
                return new CatalogResult { Source = TurnSource.AutoMap, Turns = detected, Path = mapPath,
                    Detail = $"{detected.Count} corners detected from the SimHub track map, {length:0} m (auto; review in the turn editor)" };

            return new CatalogResult { Path = userPath, Detail = mapProblem
                ?? (sectionsPath != null ? $"sections.ini has no usable sections ({sectionsPath})"
                    : "no turn data or SimHub track map for this track yet") };
        }

        private static List<TurnDefinition> Numbered(IEnumerable<TurnCandidate> candidates) => candidates
            .Where(c => c.Include && !string.IsNullOrWhiteSpace(c.Label))
            .Select(c => new TurnDefinition { Label = c.Label, Name = c.Name, Start = c.Start, End = c.End })
            .OrderBy(t => t.Start)
            .ToList();

        private static CatalogResult TryFile(string json, TurnSource source, string path)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var turns = TurnFile.Parse(json)?.ToDefinitions();
                if (turns == null || turns.Count == 0) return null;
                return new CatalogResult { Source = source, Turns = turns, Path = path, Detail = $"{turns.Count} turns" };
            }
            catch (Exception ex)
            {
                return new CatalogResult { Path = path, Detail = $"invalid turn file: {ex.Message}" };
            }
        }
    }

    /// <summary>
    /// Builds a user turn file from button presses at turn entry and exit (actions MarkTurnStart / MarkTurnEnd).
    /// Labels continue from the highest numbered turn already in the file.
    /// </summary>
    public sealed class TurnRecorder
    {
        private double? _pendingStart;

        public bool HasPendingStart => _pendingStart.HasValue;

        public void MarkStart(double lapPos) => _pendingStart = lapPos;

        public void Cancel() => _pendingStart = null;

        /// <returns>The added turn, or null when no start was marked.</returns>
        public TurnFile.TurnEntry MarkEnd(double lapPos, TurnFile file)
        {
            if (!_pendingStart.HasValue) return null;
            var entry = new TurnFile.TurnEntry
            {
                Label = NextLabel(file),
                Start = Math.Round(_pendingStart.Value, 4),
                End = Math.Round(lapPos, 4),
            };
            _pendingStart = null;
            file.Turns.Add(entry);
            file.Turns = file.Turns.OrderBy(t => t.Start).ToList();
            return entry;
        }

        public static string NextLabel(TurnFile file)
        {
            var highest = 0;
            foreach (var turn in file.Turns)
            {
                var digits = new string((turn.Label ?? string.Empty).TakeWhile(char.IsDigit).ToArray());
                if (int.TryParse(digits, out var n) && n > highest) highest = n;
            }
            return (highest + 1).ToString();
        }
    }
}
